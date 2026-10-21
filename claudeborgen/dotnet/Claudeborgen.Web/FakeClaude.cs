using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Claudeborgen
{
    // Stands in for the runner + Claude in InAppFake mode (see DESIGN.md, "Fejkad Claude"), so the
    // UI and flow can be tried on the fast test server without real Claude, cost or pushes. Goes
    // through ClaudeborgenService exactly like the agent API does.
    //
    // Steered by markers in the latest human message:
    //   [röd]     red tests once -> a fix round -> green
    //   [röd!]    tests stay red -> "behöver IT" after MaxFixAttempts
    //   [krasch]  the round crashes
    //   [offline] heartbeats stop for a while, so the status goes offline
    //   [fråga]   Claude asks a question instead of building anything, and waits for the answer
    //   [kvot]    the account's quota is "used up": the conversation goes back to the queue and
    //             Claude shows as paused for a while, then the round runs (once per conversation)
    public class FakeClaude : BackgroundService
    {
        public const string FakeCommitPrefix = "fejk";

        private readonly ClaudeborgenService _service;
        private readonly TimeSpan _phaseDelay;
        private readonly TimeSpan _tick;
        private readonly ILogger _logger;
        private DateTime _silentUntil = DateTime.MinValue;
        private DateTime _pausedUntil = DateTime.MinValue;
        private readonly System.Collections.Generic.HashSet<int> _quotaPausedOnce = new System.Collections.Generic.HashSet<int>();

        // How long a [kvot] pause lasts. Public for tests.
        public TimeSpan QuotaPause { get; set; } = TimeSpan.FromSeconds(45);

        public FakeClaude(ClaudeborgenService service, ILogger<FakeClaude> logger = null)
            : this(service, service.Settings.FakePhaseDelay, TimeSpan.FromSeconds(2), logger)
        {
        }

        public FakeClaude(ClaudeborgenService service, TimeSpan phaseDelay, TimeSpan tick, ILogger<FakeClaude> logger = null)
        {
            _service = service;
            _phaseDelay = phaseDelay;
            _tick = tick;
            _logger = (ILogger)logger ?? NullLogger.Instance;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunOnceAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "FakeClaude: varvet misslyckades.");
                }

                await Task.Delay(_tick, stoppingToken);
            }
        }

        // One pass of the runner loop: heartbeat, then at most one round. Public for tests.
        public async Task RunOnceAsync(CancellationToken cancellationToken)
        {
            if (DateTime.Now < _silentUntil)
                return;

            if (DateTime.Now < _pausedUntil)
            {
                _service.Heartbeat(ClaudeActivity.Paused, null, "kvoten är slut (fejk)", _pausedUntil);
                return;
            }

            Heartbeat(ClaudeActivity.Idle, null, null);

            // The real runner commits a done conversation's changelog entry to the repo; the fake
            // only pretends to, so the entry shows as "(fejk)" and nothing is ever committed.
            foreach (var done in _service.PendingChangelog())
                _service.MarkChangelogCommitted(done.Id, FakeCommitPrefix + "-changelog");

            var conversation = _service.Work().FirstOrDefault();
            if (conversation == null)
                return;

            var round = _service.Claim(conversation.Id);
            var instruction = conversation.Messages.LastOrDefault(m => m.AuthorKind == MessageAuthorKind.Human)?.Body ?? "";

            if (instruction.Contains("[kvot]") && _quotaPausedOnce.Add(conversation.Id))
            {
                _service.Release(conversation.Id);
                _pausedUntil = DateTime.Now + QuotaPause;
                _service.Heartbeat(ClaudeActivity.Paused, null, "kvoten är slut (fejk)", _pausedUntil);
                return;
            }

            if (instruction.Contains("[offline]") && !round.IsFixRound)
            {
                // Go quiet mid-round, long enough to pass the offline threshold, then carry on.
                Heartbeat(ClaudeActivity.Working, conversation.Id, "läser in");
                _silentUntil = DateTime.Now + _service.Settings.OfflineAfter + TimeSpan.FromSeconds(15);
                await Task.Delay(_service.Settings.OfflineAfter + TimeSpan.FromSeconds(15), cancellationToken);
            }

            if (instruction.Contains("[fråga]") && !round.IsFixRound)
            {
                Heartbeat(ClaudeActivity.Working, conversation.Id, "läser in");
                await Task.Delay(_phaseDelay, cancellationToken);
                _service.Reply(conversation.Id,
                    "(Fejkad Claude) Innan jag börjar: ska knappen ligga överst eller nederst på sidan? Jag har inte ändrat något än.",
                    null, null, "fejk-session-" + conversation.Id, 0m, (int)_phaseDelay.TotalSeconds, question: true);
                Heartbeat(ClaudeActivity.Idle, null, null);
                return;
            }

            foreach (var phase in new[] { "läser in", "implementerar", "bygger" })
            {
                Heartbeat(ClaudeActivity.Working, conversation.Id, phase);
                await Task.Delay(_phaseDelay, cancellationToken);
            }

            if (instruction.Contains("[krasch]") && !round.IsFixRound)
            {
                _service.ReportCrash(conversation.Id, "FakeClaude låtsades krascha ([krasch] i meddelandet).");
                Heartbeat(ClaudeActivity.Idle, null, null);
                return;
            }

            Heartbeat(ClaudeActivity.Working, conversation.Id, "pushar");
            await Task.Delay(_phaseDelay, cancellationToken);

            var commit = FakeCommitPrefix + Guid.NewGuid().ToString("N").Substring(0, 7);
            var body = round.IsFixRound
                ? $"(Fejkad Claude) Jag har rättat testerna i varv {round.Number}. Fejkad commit {commit}."
                : $"(Fejkad Claude) Klart: \"{Shorten(instruction)}\". Fejkad commit {commit}. Inget har ändrats på riktigt.";
            _service.Reply(conversation.Id, body, new[] { commit }, new[] { "/" }, "fejk-session-" + conversation.Id, 0m, (int)(_phaseDelay.TotalSeconds * 4));

            Heartbeat(ClaudeActivity.Working, conversation.Id, "kör tester");
            await Task.Delay(_phaseDelay, cancellationToken);

            var red = instruction.Contains("[röd!]") || (instruction.Contains("[röd]") && !round.IsFixRound);
            _service.ReportTests(conversation.Id, commit, !red, red ? "FakeClaude låtsades att 1 test är rött." : "Alla tester gröna (fejkat).");

            Heartbeat(ClaudeActivity.Idle, null, null);
        }

        private void Heartbeat(ClaudeActivity activity, int? conversationId, string phase) =>
            _service.Heartbeat(activity, conversationId, phase);

        private static string Shorten(string text)
        {
            var oneLine = text.Replace("\r", " ").Replace("\n", " ").Trim();
            return oneLine.Length <= 80 ? oneLine : oneLine.Substring(0, 77) + "...";
        }
    }
}
