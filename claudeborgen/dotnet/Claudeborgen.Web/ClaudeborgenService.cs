using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Claudeborgen
{
    // Thrown for a request that's valid in shape but not in the conversation's current state
    // (writing in a closed conversation, claiming while Claude already works on another, ...).
    // The message is shown to the user / returned to the runner as-is.
    public class ClaudeborgenException : Exception
    {
        public ClaudeborgenException(string message) : base(message) { }
    }

    public interface IClaudeborgenBroadcaster
    {
        void ConversationChanged(int conversationId);
        void StatusChanged();
    }

    // All conversation state changes go through here - from the pages (humans), the agent API (the
    // runner) and FakeClaude alike, so the fake exercises exactly the code the real runner does.
    // Single instance per app (AddClaudeborgen registers the instance, not the type, so every
    // ServiceProvider a host builds from its IServiceCollection shares it). One lock for everything:
    // volume is tiny, and Claudeborgen assumes a single app instance (file storage, in-memory
    // heartbeat, SignalR without backplane).
    public class ClaudeborgenService
    {
        private readonly object _lock = new object();
        private readonly ConversationStore _store;
        private readonly ClaudeborgenSettings _settings;
        private readonly Func<DateTime> _now;
        private IClaudeborgenBroadcaster _broadcaster;

        // Completed (and replaced) on every conversation change - what a long-polling runner waits
        // on (PROTOCOL.md, "GET work"). Heartbeats don't signal: they never create work.
        private readonly object _signalLock = new object();
        private TaskCompletionSource _changeSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Heartbeat state is deliberately in memory only: after an app restart Claude shows as
        // offline until the runner's next heartbeat, which is the truth anyway.
        private DateTime? _lastHeartbeatAt;
        private ClaudeActivity _reportedActivity = ClaudeActivity.Idle;
        private int? _reportedConversationId;
        private string _reportedPhase;
        private DateTime? _reportedPausedUntil;

        public ClaudeborgenService(ConversationStore store, ClaudeborgenSettings settings, Func<DateTime> now = null)
        {
            _store = store;
            _settings = settings;
            _now = now ?? (() => DateTime.Now);
        }

        public ClaudeborgenSettings Settings => _settings;

        // IHubContext only exists once the app is built - wired by MapClaudeborgen.
        public void SetBroadcaster(IClaudeborgenBroadcaster broadcaster) => _broadcaster = broadcaster;

        #region Humans

        public Conversation Start(string title, string body, string author)
        {
            if (string.IsNullOrWhiteSpace(title))
                throw new ClaudeborgenException("Samtalet behöver en rubrik.");
            if (string.IsNullOrWhiteSpace(body))
                throw new ClaudeborgenException("Skriv vad Claude ska göra.");

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

        // A loose idea chat (DESIGN.md, "Idéer"): no title asked for - it's made from the first message.
        public Conversation StartIdea(string body, string author)
        {
            if (string.IsNullOrWhiteSpace(body))
                throw new ClaudeborgenException("Skriv något först.");

            Conversation conversation;
            lock (_lock)
            {
                var now = _now();
                conversation = new Conversation
                {
                    Id = _store.NextId(),
                    Kind = ConversationKind.Idea,
                    Title = Shorten(body, 60),
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

        // The idea is worth building: it becomes a task, and the owner's "build it" is a human message,
        // so the next round is a normal round in the same session - Claude has the whole idea
        // discussion in mind.
        public void ConvertToTask(int id, string title, string author)
        {
            if (string.IsNullOrWhiteSpace(title))
                throw new ClaudeborgenException("Arbetet behöver en rubrik.");

            lock (_lock)
            {
                var conversation = LoadOrThrow(id);
                if (!conversation.IsIdea)
                    throw new ClaudeborgenException("Det här är redan ett arbete.");
                if (!conversation.IsOpen)
                    throw new ClaudeborgenException("Idén är avslutad.");
                if (conversation.State == ConversationState.ClaudeWorking)
                    throw new ClaudeborgenException("Claude svarar just nu. Vänta tills svaret har kommit.");

                conversation.Kind = ConversationKind.Task;
                conversation.Title = title.Trim();
                conversation.NeedsIt = false;
                conversation.NeedsItReason = null;
                conversation.Messages.Add(new ConversationMessage
                {
                    AuthorKind = MessageAuthorKind.Human,
                    Author = author,
                    Body = $"Nu bygger vi det här: \"{conversation.Title}\". Implementera det vi har pratat om.",
                    At = _now(),
                });
                conversation.State = ConversationState.WaitingForClaude;
                _store.Save(conversation);
            }

            Changed(id);
        }

        private static string Shorten(string text, int max)
        {
            var oneLine = string.Join(" ", text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
            return oneLine.Length <= max ? oneLine : oneLine.Substring(0, max - 1).TrimEnd() + "…";
        }

        // Every human message starts a new round - also one written while Claude is mid-round; it's
        // queued and picked up when the current round ends (see Reply).
        public void AddMessage(int id, string body, string author)
        {
            if (string.IsNullOrWhiteSpace(body))
                throw new ClaudeborgenException("Meddelandet är tomt.");

            lock (_lock)
            {
                var conversation = LoadOrThrow(id);
                if (!conversation.IsOpen)
                    throw new ClaudeborgenException("Samtalet är avslutat.");
                if (conversation.State == ConversationState.Reverting)
                    throw new ClaudeborgenException("Arbetet håller på att tas bort.");

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
                    throw new ClaudeborgenException("Samtalet är redan avslutat.");
                if (conversation.IsIdea)
                    throw new ClaudeborgenException("En idé blir klar först när den har gjorts om till ett arbete och byggts.");
                if (conversation.State == ConversationState.Reverting)
                    throw new ClaudeborgenException("Arbetet håller på att tas bort.");
                if (conversation.State == ConversationState.ClaudeWorking)
                    throw new ClaudeborgenException("Claude arbetar i samtalet just nu. Vänta tills varvet är klart.");

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
                    throw new ClaudeborgenException("Samtalet är redan avslutat.");
                if (conversation.State == ConversationState.ClaudeWorking)
                    throw new ClaudeborgenException("Claude arbetar i samtalet just nu. Vänta tills varvet är klart.");
                if (conversation.State == ConversationState.Reverting && !conversation.NeedsIt)
                    throw new ClaudeborgenException("Arbetet håller redan på att tas bort.");

                // Work that has pushed something: everything it pushed is reverted first, by the
                // runner (DESIGN.md, "Avbryta ett arbete"). Asking again after a failed revert retries.
                if (!conversation.IsIdea && CommitsOf(conversation).Count > 0)
                {
                    conversation.State = ConversationState.Reverting;
                    conversation.NeedsIt = false;
                    conversation.NeedsItReason = null;
                    conversation.Messages.Add(new ConversationMessage { AuthorKind = MessageAuthorKind.System, Author = author, Body = "Avbrutet. Ändringarna tas bort.", At = _now() });
                    _store.Save(conversation);
                }
                else
                {
                    // Nothing pushed, nothing to undo: removed right away (DESIGN.md, "Samtal tas bort").
                    conversation.State = ConversationState.Cancelled;
                    Remove(conversation, cancelled: true);
                }
            }

            Changed(id);
        }

        // Every commit the work pushed, oldest first.
        private static List<string> CommitsOf(Conversation conversation) =>
            conversation.Rounds.Where(r => r.Outcome == RoundOutcome.Delivered).SelectMany(r => r.Commits).Distinct().ToList();

        // Cancelled work whose commits the runner should revert.
        public List<(int ConversationId, string Title, List<string> Commits)> PendingReverts()
        {
            lock (_lock)
            {
                return _store.LoadAll()
                    .Where(c => c.State == ConversationState.Reverting && !c.NeedsIt)
                    .Select(c => (c.Id, c.Title, CommitsOf(c)))
                    .ToList();
            }
        }

        // The revert is pushed: now the conversation goes, like any cancelled one.
        public void MarkReverted(int id, string commit)
        {
            lock (_lock)
            {
                var conversation = LoadOrThrow(id);
                if (conversation.State != ConversationState.Reverting)
                    throw new ClaudeborgenException("Arbetet håller inte på att tas bort.");
                conversation.State = ConversationState.Cancelled;
                Remove(conversation, cancelled: true);
            }

            Changed(id);
        }

        // The revert couldn't be done (a conflict with later work, a red build): nothing was pushed,
        // a human decides. "Avbryt och ta bort arbete" again retries.
        public void MarkRevertFailed(int id, string summary)
        {
            lock (_lock)
            {
                var conversation = LoadOrThrow(id);
                if (conversation.State != ConversationState.Reverting)
                    throw new ClaudeborgenException("Arbetet håller inte på att tas bort.");
                conversation.NeedsIt = true;
                conversation.NeedsItReason = "Ändringarna kunde inte tas bort.";
                conversation.Messages.Add(new ConversationMessage
                {
                    AuthorKind = MessageAuthorKind.System,
                    Author = "System",
                    Body = "Ändringarna kunde inte tas bort" + (string.IsNullOrWhiteSpace(summary) ? "." : ": " + summary.Trim()) + " Inget har pushats. IT behöver titta på det.",
                    At = _now(),
                });
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

                status.Activity = _reportedActivity;
                if (status.Activity == ClaudeActivity.Working)
                {
                    status.ConversationId = _reportedConversationId;
                    status.ConversationTitle = conversations.FirstOrDefault(c => c.Id == _reportedConversationId)?.Title;
                    status.Phase = _reportedPhase;
                }
                else if (status.Activity == ClaudeActivity.Paused)
                {
                    status.Phase = _reportedPhase;
                    status.PausedUntil = _reportedPausedUntil;
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

        // What the start page shows: the deployed changelog file, plus done conversations whose
        // entry isn't in it yet.
        //
        // Done conversations come from two places: still in the store (marked done, entry not yet
        // committed) and removed (entry committed, conversation deleted, maybe not deployed here).
        // A removed conversation's record is dropped once the deployed file has its entry and the
        // runner has deleted its sessions - from then on the file alone carries it.
        public List<ChangelogEntry> ChangelogView()
        {
            var file = ChangelogFile.Load(_settings.ChangelogPath);
            var inFile = new HashSet<int>(file.Where(e => e.ConversationId != null).Select(e => e.ConversationId.Value));

            lock (_lock)
            {
                RemoveLeftovers();
                var removed = _store.LoadRemoved();
                var stillNeeded = removed.Where(r => !(r.SessionsCleaned && (r.Cancelled || inFile.Contains(r.ConversationId)))).ToList();
                if (stillNeeded.Count != removed.Count)
                    _store.SaveRemoved(stillNeeded);

                var done = _store.LoadAll().Where(c => c.State == ConversationState.Done).ToList();
                var committed = stillNeeded.Where(r => !r.Cancelled).Select(r => new Conversation
                {
                    Id = r.ConversationId,
                    State = ConversationState.Done,
                    DoneAt = DateTime.TryParse(r.ChangelogDate, out var date) ? date : r.RemovedAt,
                    ChangelogText = r.ChangelogText,
                    ChangelogCommit = r.ChangelogCommit,
                });

                var entries = ChangelogFile.Merge(file, done.Concat(committed), _settings.IsFake);

                // Only a done conversation still waiting here can be opened. Never an entry from the
                // file: its number may be another server's conversation (the fast test server has
                // its own #12).
                var existing = new HashSet<int>(done.Select(c => c.Id));
                foreach (var entry in entries)
                    entry.Linkable = entry.Pending && entry.ConversationId != null && existing.Contains(entry.ConversationId.Value);
                return entries;
            }
        }

        // Done conversations whose changelog entry hasn't been committed yet - the runner's job.
        public List<Conversation> PendingChangelog()
        {
            lock (_lock)
            {
                RemoveLeftovers();
                return _store.LoadAll()
                    .Where(c => c.State == ConversationState.Done && c.ChangelogCommit == null)
                    .OrderBy(c => c.DoneAt)
                    .ToList();
            }
        }

        public void MarkChangelogCommitted(int id, string commit)
        {
            if (string.IsNullOrWhiteSpace(commit))
                throw new ClaudeborgenException("Commit saknas.");

            lock (_lock)
            {
                var conversation = LoadOrThrow(id);
                if (conversation.State != ConversationState.Done)
                    throw new ClaudeborgenException("Samtalet är inte markerat som klart.");

                conversation.ChangelogCommit = commit.Trim();

                // The entry is safe in the repo now - the discussion is not kept any longer than
                // needed (DESIGN.md, "Samtal tas bort").
                Remove(conversation, cancelled: false);
            }

            Changed(id);
        }

        // Delivered rounds whose commit isn't confirmed live on the fast test server yet (DESIGN.md,
        // "Driftsatt"). Only the last day's: an older one is never going to show up.
        public List<(int ConversationId, int Round, string Commit)> PendingDeployments()
        {
            lock (_lock)
            {
                var since = _now().AddDays(-1);
                return _store.LoadAll()
                    .SelectMany(c => c.Rounds
                        .Where(r => r.Outcome == RoundOutcome.Delivered && r.Commits.Count > 0 && r.DeployedAt == null && r.FinishedAt > since)
                        .Select(r => (c.Id, r.Number, r.Commits.Last())))
                    .ToList();
            }
        }

        public void MarkDeployed(int id, int roundNumber)
        {
            lock (_lock)
            {
                var conversation = LoadOrThrow(id);
                var round = conversation.Rounds.FirstOrDefault(r => r.Number == roundNumber)
                    ?? throw new ClaudeborgenException($"Samtal {id} har inget varv {roundNumber}.");
                if (round.DeployedAt != null)
                    return;

                var now = _now();
                round.DeployedAt = now;

                // Tell the owner - but only about the newest change: an older round going live at the
                // same time is old news.
                var newest = conversation.Rounds.LastOrDefault(r => r.Outcome == RoundOutcome.Delivered && r.Commits.Count > 0);
                if (newest == round)
                    conversation.Messages.Add(new ConversationMessage
                    {
                        AuthorKind = MessageAuthorKind.System,
                        Author = "System",
                        Body = "Ändringen är ute på snabbtestservern och går att prova.",
                        At = now,
                    });
                _store.Save(conversation);
            }

            Changed(id);
        }

        // Removed conversations whose Claude Code sessions the runner still has to delete.
        public List<RemovedConversation> PendingSessionCleanup()
        {
            lock (_lock)
            {
                return _store.LoadRemoved().Where(r => !r.SessionsCleaned).ToList();
            }
        }

        public void MarkSessionsCleaned(int id)
        {
            lock (_lock)
            {
                var removed = _store.LoadRemoved();
                var record = removed.FirstOrDefault(r => r.ConversationId == id)
                    ?? throw new ClaudeborgenException($"Samtal {id} är inte borttaget.");
                record.SessionsCleaned = true;
                _store.SaveRemoved(removed);
            }
        }

        // Conversations that should already have been removed: done with their changelog entry
        // committed, cancelled, or ideas idle for longer than IdeaRetention. Data from before removal existed (2026-10-21) - otherwise they'd
        // linger, linkable, forever. Caller holds _lock.
        private void RemoveLeftovers()
        {
            var now = _now();
            foreach (var conversation in _store.LoadAll())
            {
                if (conversation.State == ConversationState.Cancelled)
                    Remove(conversation, cancelled: true);
                // An idea left alone is let go of - nothing sensitive lingers (DESIGN.md, "Idéer").
                else if (conversation.IsIdea && conversation.State != ConversationState.ClaudeWorking && now - conversation.LastActivityAt > _settings.IdeaRetention)
                    Remove(conversation, cancelled: true);
                else if (conversation.State == ConversationState.Done && conversation.ChangelogCommit != null)
                    Remove(conversation, cancelled: false);
            }
        }

        // Deletes the conversation (the discussion) and keeps only a RemovedConversation record.
        // Caller holds _lock.
        private void Remove(Conversation conversation, bool cancelled)
        {
            var sessionIds = conversation.Rounds.Select(r => r.SessionId)
                .Append(conversation.SessionId)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct()
                .ToList();

            var removed = _store.LoadRemoved();
            removed.RemoveAll(r => r.ConversationId == conversation.Id);
            removed.Add(new RemovedConversation
            {
                ConversationId = conversation.Id,
                Cancelled = cancelled,
                RemovedAt = _now(),
                ChangelogDate = cancelled ? null : (conversation.DoneAt ?? _now()).ToString("yyyy-MM-dd"),
                ChangelogText = cancelled ? null : conversation.ChangelogText ?? conversation.Title,
                ChangelogCommit = conversation.ChangelogCommit,
                SessionIds = sessionIds,
                // Nothing for the runner to delete if Claude never ran.
                SessionsCleaned = sessionIds.Count == 0,
            });
            _store.SaveRemoved(removed);
            _store.Delete(conversation.Id);
        }

        #endregion

        #region Claude (runner via agent API, or FakeClaude)

        public void Heartbeat(ClaudeActivity activity, int? conversationId, string phase, DateTime? pausedUntil = null)
        {
            // Offline is never reported - it's the absence of heartbeats.
            if (activity == ClaudeActivity.Offline)
                activity = ClaudeActivity.Idle;
            if (activity != ClaudeActivity.Paused)
                pausedUntil = null;

            bool changed;
            lock (_lock)
            {
                var wasOffline = IsOffline();
                changed = wasOffline
                    || _reportedActivity != activity
                    || _reportedConversationId != conversationId
                    || _reportedPhase != phase
                    || _reportedPausedUntil != pausedUntil;

                _lastHeartbeatAt = _now();
                _reportedActivity = activity;
                _reportedConversationId = conversationId;
                _reportedPhase = phase;
                _reportedPausedUntil = pausedUntil;
            }

            if (changed)
                _broadcaster?.StatusChanged();
        }

        // A round Claude was in the middle of comes first, so a restarted runner resumes it; then
        // conversations waiting for Claude, oldest first.
        // `kind`: the runner handles ideas and tasks in separate loops, so an idea chat never waits
        // behind an implementation round.
        public List<Conversation> Work(ConversationKind? kind = null)
        {
            lock (_lock)
            {
                var conversations = _store.LoadAll().Where(c => kind == null || c.Kind == kind).ToList();
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
                    throw new ClaudeborgenException("Samtalet väntar inte på Claude.");

                // One at a time per kind: an idea reply (read-only) may run alongside a task round.
                var busy = _store.LoadAll().FirstOrDefault(c => c.State == ConversationState.ClaudeWorking && c.Kind == conversation.Kind);
                if (busy != null)
                    throw new ClaudeborgenException($"Claude arbetar redan i samtal {busy.Id}. Ett samtal i taget.");

                var now = _now();
                // Counted by position in the conversation, never by timestamp: clocks differ (a
                // seeded or imported message, a server in another time zone) and a wrong answer
                // here means paying for rounds nobody asked for.
                var humanMessageSinceLastRound = HumanMessageAfter(conversation, conversation.CurrentRound?.MessageCountAtStart ?? 0);

                round = new Round
                {
                    Number = conversation.Rounds.Count + 1,
                    StartedAt = now,
                    IsFixRound = !humanMessageSinceLastRound,
                    MessageCountAtStart = conversation.Messages.Count,
                };
                conversation.Rounds.Add(round);
                conversation.State = ConversationState.ClaudeWorking;
                conversation.ClaimedAt = now;
                _store.Save(conversation);
            }

            Changed(id);
            return round;
        }

        // `question`: Claude asks the owner something and waits for the answer (DESIGN.md, "Frågor").
        public void Reply(int id, string body, IEnumerable<string> commits, IEnumerable<string> links, string sessionId, decimal? costUsd, int? durationSeconds, bool question = false)
        {
            if (string.IsNullOrWhiteSpace(body))
                throw new ClaudeborgenException("Svaret är tomt.");

            lock (_lock)
            {
                var conversation = LoadOrThrow(id);
                if (conversation.State != ConversationState.ClaudeWorking)
                    throw new ClaudeborgenException("Claude arbetar inte i samtalet.");

                var now = _now();
                var round = conversation.CurrentRound;
                round.FinishedAt = now;
                round.Outcome = RoundOutcome.Delivered;
                round.Commits = (commits ?? Enumerable.Empty<string>()).Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
                round.Links = (links ?? Enumerable.Empty<string>()).Where(IsSafeLink).ToList();
                round.SessionId = sessionId;
                round.CostUsd = costUsd;
                round.DurationSeconds = durationSeconds;
                round.AskedQuestion = question;
                if (!string.IsNullOrWhiteSpace(sessionId))
                    conversation.SessionId = sessionId;

                conversation.Messages.Add(new ConversationMessage { AuthorKind = MessageAuthorKind.Claude, Author = "Claude", Body = body.Trim(), At = now, IsQuestion = question });
                conversation.State = HumanMessageAfter(conversation, round.MessageCountAtStart)
                    ? ConversationState.WaitingForClaude
                    : question ? ConversationState.QuestionForOwner : ConversationState.WaitingForOwner;
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
                    throw new ClaudeborgenException("Samtalet har inget levererat varv att rapportera tester för.");

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

        // The runner took the conversation but couldn't run Claude (quota used up): hand it back to
        // the queue untouched - no crash, no "behöver IT", the unstarted round is dropped.
        public void Release(int id)
        {
            lock (_lock)
            {
                var conversation = LoadOrThrow(id);
                if (conversation.State != ConversationState.ClaudeWorking)
                    throw new ClaudeborgenException("Claude arbetar inte i samtalet.");

                var round = conversation.CurrentRound;
                if (round != null && round.Outcome == null)
                    conversation.Rounds.Remove(round);
                conversation.State = ConversationState.WaitingForClaude;
                conversation.ClaimedAt = null;
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
                    throw new ClaudeborgenException("Claude arbetar inte i samtalet.");

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

        // Was a human message added at or after position `messageCount` in the conversation?
        private static bool HumanMessageAfter(Conversation conversation, int messageCount) =>
            conversation.Messages.Skip(messageCount).Any(m => m.AuthorKind == MessageAuthorKind.Human);

        private Conversation LoadOrThrow(int id) =>
            _store.Load(id) ?? throw new ClaudeborgenException($"Samtal {id} finns inte.");

        private void Changed(int id)
        {
            _broadcaster?.ConversationChanged(id);

            TaskCompletionSource signalled;
            lock (_signalLock)
            {
                signalled = _changeSignal;
                _changeSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            signalled.TrySetResult();
        }

        // Completes at the next conversation change. Take it *before* checking for work, so a change
        // in between isn't missed.
        public Task NextChange()
        {
            lock (_signalLock)
            {
                return _changeSignal.Task;
            }
        }
    }
}
