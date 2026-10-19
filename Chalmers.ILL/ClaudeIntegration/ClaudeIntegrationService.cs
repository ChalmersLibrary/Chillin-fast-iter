using System;
using System.Collections.Generic;
using System.Linq;

namespace Chalmers.ILL.ClaudeIntegration
{
    // Thrown for a request that's valid in shape but not in the conversation's current state
    // (writing in a closed conversation, claiming while Claude already works on another, ...).
    // The message is shown to the user / returned to the runner as-is.
    public class ClaudeIntegrationException : Exception
    {
        public ClaudeIntegrationException(string message) : base(message) { }
    }

    public interface IClaudeUpdateBroadcaster
    {
        void ConversationChanged(int conversationId);
        void StatusChanged();
    }

    // All conversation state changes go through here - from the pages (humans), the agent API (the
    // runner) and FakeClaude alike, so the fake exercises exactly the code the real runner does.
    // Single instance per app (registered as an instance, see Bootstrapper - type-based singletons
    // diverge across Bootstrapper's several ServiceProviders). One lock for everything: volume is
    // tiny, and the app runs on one instance (see CLAUDE.md, "Enkelinstans").
    public class ClaudeIntegrationService
    {
        private readonly object _lock = new object();
        private readonly ConversationStore _store;
        private readonly ClaudeIntegrationSettings _settings;
        private readonly Func<DateTime> _now;
        private IClaudeUpdateBroadcaster _broadcaster;

        // Heartbeat state is deliberately in memory only: after an app restart Claude shows as
        // offline until the runner's next heartbeat, which is the truth anyway.
        private DateTime? _lastHeartbeatAt;
        private ClaudeActivity _reportedActivity = ClaudeActivity.Idle;
        private int? _reportedConversationId;
        private string _reportedPhase;

        public ClaudeIntegrationService(ConversationStore store, ClaudeIntegrationSettings settings, Func<DateTime> now = null)
        {
            _store = store;
            _settings = settings;
            _now = now ?? (() => DateTime.Now);
        }

        public ClaudeIntegrationSettings Settings => _settings;

        // IHubContext only exists once the app is built - same late wiring as
        // FileOrderItemManager.SetNotifier, see Program.cs.
        public void SetBroadcaster(IClaudeUpdateBroadcaster broadcaster) => _broadcaster = broadcaster;

        #region Humans

        public Conversation Start(string title, string body, string author)
        {
            if (string.IsNullOrWhiteSpace(title))
                throw new ClaudeIntegrationException("Samtalet behöver en rubrik.");
            if (string.IsNullOrWhiteSpace(body))
                throw new ClaudeIntegrationException("Skriv vad Claude ska göra.");

            Conversation conversation;
            lock (_lock)
            {
                var now = _now();
                conversation = new Conversation
                {
                    Id = _store.NextId(),
                    Title = title.Trim(),
                    CreatedBy = author,
                    CreatedAt = now,
                    State = ConversationState.WaitingForClaude,
                };
                conversation.Messages.Add(new ConversationMessage { AuthorKind = MessageAuthorKind.Human, Author = author, Body = body.Trim(), At = now });
                _store.Save(conversation);
            }

            Changed(conversation.Id);
            return conversation;
        }

        // Every human message starts a new round - also one written while Claude is mid-round; it's
        // queued and picked up when the current round ends (see Reply).
        public void AddMessage(int id, string body, string author)
        {
            if (string.IsNullOrWhiteSpace(body))
                throw new ClaudeIntegrationException("Meddelandet är tomt.");

            lock (_lock)
            {
                var conversation = LoadOrThrow(id);
                if (!conversation.IsOpen)
                    throw new ClaudeIntegrationException("Samtalet är avslutat.");

                conversation.Messages.Add(new ConversationMessage { AuthorKind = MessageAuthorKind.Human, Author = author, Body = body.Trim(), At = _now() });
                conversation.NeedsIt = false;
                conversation.NeedsItReason = null;
                conversation.ConsecutiveRedRounds = 0;
                if (conversation.State != ConversationState.ClaudeWorking)
                    conversation.State = ConversationState.WaitingForClaude;
                _store.Save(conversation);
            }

            Changed(id);
        }

        public void MarkDone(int id, string author, string changelogText)
        {
            lock (_lock)
            {
                var conversation = LoadOrThrow(id);
                if (!conversation.IsOpen)
                    throw new ClaudeIntegrationException("Samtalet är redan avslutat.");
                if (conversation.State == ConversationState.ClaudeWorking)
                    throw new ClaudeIntegrationException("Claude arbetar i samtalet just nu. Vänta tills varvet är klart.");

                var now = _now();
                conversation.State = ConversationState.Done;
                conversation.DoneAt = now;
                conversation.DoneBy = author;
                conversation.ChangelogText = string.IsNullOrWhiteSpace(changelogText) ? conversation.Title : changelogText.Trim();
                conversation.NeedsIt = false;
                conversation.NeedsItReason = null;
                conversation.Messages.Add(new ConversationMessage { AuthorKind = MessageAuthorKind.System, Author = author, Body = "Markerat som klart.", At = now });
                _store.Save(conversation);
            }

            Changed(id);
        }

        public void Cancel(int id, string author)
        {
            lock (_lock)
            {
                var conversation = LoadOrThrow(id);
                if (!conversation.IsOpen)
                    throw new ClaudeIntegrationException("Samtalet är redan avslutat.");
                if (conversation.State == ConversationState.ClaudeWorking)
                    throw new ClaudeIntegrationException("Claude arbetar i samtalet just nu. Vänta tills varvet är klart.");

                conversation.State = ConversationState.Cancelled;
                conversation.NeedsIt = false;
                conversation.NeedsItReason = null;
                conversation.Messages.Add(new ConversationMessage { AuthorKind = MessageAuthorKind.System, Author = author, Body = "Avbrutet.", At = _now() });
                _store.Save(conversation);
            }

            Changed(id);
        }

        #endregion

        #region Reading

        public List<Conversation> All()
        {
            lock (_lock)
            {
                return _store.LoadAll();
            }
        }

        public Conversation Get(int id)
        {
            lock (_lock)
            {
                return _store.Load(id);
            }
        }

        public ClaudeStatus GetStatus()
        {
            lock (_lock)
            {
                var conversations = _store.LoadAll();
                var status = new ClaudeStatus
                {
                    LastHeartbeatAt = _lastHeartbeatAt,
                    QueueLength = conversations.Count(c => c.State == ConversationState.WaitingForClaude),
                };

                if (IsOffline())
                {
                    status.Activity = ClaudeActivity.Offline;
                    return status;
                }

                status.Activity = _reportedActivity == ClaudeActivity.Working ? ClaudeActivity.Working : ClaudeActivity.Idle;
                if (status.Activity == ClaudeActivity.Working)
                {
                    status.ConversationId = _reportedConversationId;
                    status.ConversationTitle = conversations.FirstOrDefault(c => c.Id == _reportedConversationId)?.Title;
                    status.Phase = _reportedPhase;
                }

                return status;
            }
        }

        // What ObjektSpecialistIT needs to look at: Claude gave up (tests stay red, a round crashed),
        // or a round is stuck because Claude went offline in the middle of it.
        public List<Conversation> NeedingAttention()
        {
            lock (_lock)
            {
                var offline = IsOffline();
                return _store.LoadAll()
                    .Where(c => c.IsOpen && (c.NeedsIt || (offline && c.State == ConversationState.ClaudeWorking)))
                    .ToList();
            }
        }

        public List<Conversation> Changelog()
        {
            lock (_lock)
            {
                return _store.LoadAll()
                    .Where(c => c.State == ConversationState.Done)
                    .OrderByDescending(c => c.DoneAt)
                    .ToList();
            }
        }

        #endregion

        #region Claude (runner via agent API, or FakeClaude)

        public void Heartbeat(ClaudeActivity activity, int? conversationId, string phase)
        {
            bool changed;
            lock (_lock)
            {
                var wasOffline = IsOffline();
                changed = wasOffline
                    || _reportedActivity != activity
                    || _reportedConversationId != conversationId
                    || _reportedPhase != phase;

                _lastHeartbeatAt = _now();
                _reportedActivity = activity == ClaudeActivity.Working ? ClaudeActivity.Working : ClaudeActivity.Idle;
                _reportedConversationId = conversationId;
                _reportedPhase = phase;
            }

            if (changed)
                _broadcaster?.StatusChanged();
        }

        // A round Claude was in the middle of comes first, so a restarted runner resumes it; then
        // conversations waiting for Claude, oldest first.
        public List<Conversation> Work()
        {
            lock (_lock)
            {
                var conversations = _store.LoadAll();
                return conversations.Where(c => c.State == ConversationState.ClaudeWorking)
                    .Concat(conversations.Where(c => c.State == ConversationState.WaitingForClaude).OrderBy(c => c.LastActivityAt))
                    .ToList();
            }
        }

        public Round Claim(int id)
        {
            Round round;
            lock (_lock)
            {
                var conversation = LoadOrThrow(id);
                if (conversation.State == ConversationState.ClaudeWorking)
                    return conversation.CurrentRound; // re-claim after a runner restart

                if (conversation.State != ConversationState.WaitingForClaude)
                    throw new ClaudeIntegrationException("Samtalet väntar inte på Claude.");

                var busy = _store.LoadAll().FirstOrDefault(c => c.State == ConversationState.ClaudeWorking);
                if (busy != null)
                    throw new ClaudeIntegrationException($"Claude arbetar redan i samtal {busy.Id}. Ett samtal i taget.");

                var now = _now();
                var previousRoundStart = conversation.CurrentRound?.StartedAt ?? DateTime.MinValue;
                var humanMessageSinceLastRound = conversation.Messages.Any(m => m.AuthorKind == MessageAuthorKind.Human && m.At > previousRoundStart);

                round = new Round
                {
                    Number = conversation.Rounds.Count + 1,
                    StartedAt = now,
                    IsFixRound = !humanMessageSinceLastRound,
                };
                conversation.Rounds.Add(round);
                conversation.State = ConversationState.ClaudeWorking;
                conversation.ClaimedAt = now;
                _store.Save(conversation);
            }

            Changed(id);
            return round;
        }

        public void Reply(int id, string body, IEnumerable<string> commits, IEnumerable<string> links, string sessionId, decimal? costUsd, int? durationSeconds)
        {
            if (string.IsNullOrWhiteSpace(body))
                throw new ClaudeIntegrationException("Svaret är tomt.");

            lock (_lock)
            {
                var conversation = LoadOrThrow(id);
                if (conversation.State != ConversationState.ClaudeWorking)
                    throw new ClaudeIntegrationException("Claude arbetar inte i samtalet.");

                var now = _now();
                var round = conversation.CurrentRound;
                round.FinishedAt = now;
                round.Outcome = RoundOutcome.Delivered;
                round.Commits = (commits ?? Enumerable.Empty<string>()).Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
                round.Links = (links ?? Enumerable.Empty<string>()).Where(IsSafeLink).ToList();
                round.SessionId = sessionId;
                round.CostUsd = costUsd;
                round.DurationSeconds = durationSeconds;
                if (!string.IsNullOrWhiteSpace(sessionId))
                    conversation.SessionId = sessionId;

                conversation.Messages.Add(new ConversationMessage { AuthorKind = MessageAuthorKind.Claude, Author = "Claude", Body = body.Trim(), At = now });
                conversation.State = HumanMessageSinceClaim(conversation) ? ConversationState.WaitingForClaude : ConversationState.WaitingForOwner;
                _store.Save(conversation);
            }

            Changed(id);
        }

        // Tests run after the push (see claudeborgen-DESIGN.md, "Tester"), so the result arrives
        // after the reply. Red sends the conversation straight back to Claude for a fix round, up
        // to MaxFixAttempts; after that it's flagged for IT instead of looping.
        public void ReportTests(int id, string commit, bool green, string summary)
        {
            lock (_lock)
            {
                var conversation = LoadOrThrow(id);
                var round = conversation.Rounds.LastOrDefault(r => !string.IsNullOrWhiteSpace(commit) && r.Commits.Contains(commit))
                    ?? conversation.Rounds.LastOrDefault(r => r.Outcome == RoundOutcome.Delivered);
                if (round == null)
                    throw new ClaudeIntegrationException("Samtalet har inget levererat varv att rapportera tester för.");

                round.Tests = green ? TestStatus.Green : TestStatus.Red;
                round.TestSummary = summary;

                var now = _now();
                if (green)
                {
                    conversation.ConsecutiveRedRounds = 0;
                }
                else
                {
                    conversation.ConsecutiveRedRounds++;
                    var text = "Testerna är röda" + (string.IsNullOrWhiteSpace(summary) ? "." : ": " + summary.Trim());

                    if (conversation.ConsecutiveRedRounds > _settings.MaxFixAttempts)
                    {
                        conversation.NeedsIt = true;
                        conversation.NeedsItReason = $"Testerna är fortfarande röda efter {_settings.MaxFixAttempts} rättningsförsök.";
                        conversation.Messages.Add(new ConversationMessage { AuthorKind = MessageAuthorKind.System, Author = "System", Body = text + " " + conversation.NeedsItReason + " IT behöver titta på det.", At = now });
                    }
                    else
                    {
                        conversation.Messages.Add(new ConversationMessage { AuthorKind = MessageAuthorKind.System, Author = "System", Body = text + " Claude rättar.", At = now });
                        if (conversation.State == ConversationState.WaitingForOwner)
                            conversation.State = ConversationState.WaitingForClaude;
                    }
                }

                _store.Save(conversation);
            }

            Changed(id);
        }

        public void ReportCrash(int id, string summary)
        {
            lock (_lock)
            {
                var conversation = LoadOrThrow(id);
                if (conversation.State != ConversationState.ClaudeWorking)
                    throw new ClaudeIntegrationException("Claude arbetar inte i samtalet.");

                var now = _now();
                var round = conversation.CurrentRound;
                round.FinishedAt = now;
                round.Outcome = RoundOutcome.Crashed;
                round.TestSummary = summary;

                conversation.NeedsIt = true;
                conversation.NeedsItReason = "Varvet kraschade.";
                conversation.Messages.Add(new ConversationMessage
                {
                    AuthorKind = MessageAuthorKind.System,
                    Author = "System",
                    Body = "Varvet kraschade" + (string.IsNullOrWhiteSpace(summary) ? "." : ": " + summary.Trim()) + " Inget har pushats. IT behöver titta på det.",
                    At = now
                });
                conversation.State = ConversationState.WaitingForOwner;
                _store.Save(conversation);
            }

            Changed(id);
        }

        #endregion

        // Links end up as href in the pages: only http(s) or app-relative, never javascript: etc.
        public static bool IsSafeLink(string link)
        {
            if (string.IsNullOrWhiteSpace(link))
                return false;
            if (link.StartsWith("/") && !link.StartsWith("//"))
                return true;
            return Uri.TryCreate(link, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
        }

        private bool IsOffline() => _lastHeartbeatAt == null || _now() - _lastHeartbeatAt.Value > _settings.OfflineAfter;

        private static bool HumanMessageSinceClaim(Conversation conversation) =>
            conversation.ClaimedAt != null && conversation.Messages.Any(m => m.AuthorKind == MessageAuthorKind.Human && m.At > conversation.ClaimedAt.Value);

        private Conversation LoadOrThrow(int id) =>
            _store.Load(id) ?? throw new ClaudeIntegrationException($"Samtal {id} finns inte.");

        private void Changed(int id)
        {
            _broadcaster?.ConversationChanged(id);
        }
    }
}
