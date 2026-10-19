using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace Chalmers.ILL.ClaudeIntegration
{
    // Stands in for the runner + Claude in isolated mode (see claudeborgen-DESIGN.md, "Fejkad
    // Claude"), so the integration's UI and flow can be tried on the fast test server without real
    // Claude, cost or pushes. Goes through ClaudeIntegrationService exactly like the agent API does.
    //
    // Steered by markers in the latest human message:
    //   [röd]     red tests once -> a fix round -> green
    //   [röd!]    tests stay red -> "behöver IT" after MaxFixAttempts
    //   [krasch]  the round crashes
    //   [offline] heartbeats stop for a while, so the status goes offline
    public class FakeClaude : BackgroundService
    {
        public const string FakeCommitPrefix = "fejk";

        private readonly ClaudeIntegrationService _service;
        private readonly TimeSpan _phaseDelay;
        private readonly TimeSpan _tick;
        private DateTime _silentUntil = DateTime.MinValue;

        public FakeClaude(ClaudeIntegrationService service)
            : this(service, service.Settings.FakePhaseDelay, TimeSpan.FromSeconds(2))
        {
        }

        public FakeClaude(ClaudeIntegrationService service, TimeSpan phaseDelay, TimeSpan tick)
        {
            _service = service;
            _phaseDelay = phaseDelay;
            _tick = tick;
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
                    log4net.LogManager.GetLogger(typeof(FakeClaude)).Error("FakeClaude: varvet misslyckades.", e);
                }

                await Task.Delay(_tick, stoppingToken);
            }
        }

        // One pass of the runner loop: heartbeat, then at most one round. Public for tests.
        public async Task RunOnceAsync(CancellationToken cancellationToken)
        {
            if (DateTime.Now < _silentUntil)
                return;

            Heartbeat(ClaudeActivity.Idle, null, null);

            var conversation = _service.Work().FirstOrDefault();
            if (conversation == null)
                return;

            var round = _service.Claim(conversation.Id);
            var instruction = conversation.Messages.LastOrDefault(m => m.AuthorKind == MessageAuthorKind.Human)?.Body ?? "";

            if (instruction.Contains("[offline]") && !round.IsFixRound)
            {
                // Go quiet mid-round, long enough to pass the offline threshold, then carry on.
                Heartbeat(ClaudeActivity.Working, conversation.Id, "läser in");
                _silentUntil = DateTime.Now + _service.Settings.OfflineAfter + TimeSpan.FromSeconds(15);
                await Task.Delay(_service.Settings.OfflineAfter + TimeSpan.FromSeconds(15), cancellationToken);
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
