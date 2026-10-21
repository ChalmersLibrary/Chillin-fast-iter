using System;
using System.IO;
using Microsoft.Extensions.Configuration;

namespace Claudeborgen
{
    public enum ClaudeborgenMode
    {
        // Integration hidden entirely: no pages, no agent API.
        Off,

        // The real channel: the agent API is open to the runner in the devcontainer, authenticated
        // by token. Never on the isolated fast test server (see DESIGN.md, "Kanalen och
        // testservern") - the host app must enforce that (Chillin: IsolationGuard).
        Agent,

        // Isolated fast test server, default: the UI is on, Claude is FakeClaude in-process, the
        // agent API is closed. The whole flow can be tried without anything else running.
        InAppFake,

        // Isolated fast test server, to test the runner itself: the agent API is open, but only to
        // a runner in --fake mode (header X-Claudeborgen-Fake-Runner: true) - a real runner, which
        // would push and redeploy this very server, is refused.
        FakeRunner
    }

    public class ClaudeborgenSettings
    {
        public const string SectionName = "Claudeborgen";
        public const string FakeRunnerHeader = "X-Claudeborgen-Fake-Runner";

        public ClaudeborgenMode Mode { get; set; }
        public string DataDirectory { get; set; }

        // SHA-256 (hex) of the agent token; the token itself only ever lives in the devcontainer.
        public string AgentTokenSha256 { get; set; }

        // e.g. "https://github.com/ChalmersLibrary/Chillin-fast-iter/commit/{0}". Optional.
        public string CommitUrlTemplate { get; set; }

        // The deployed changelog file (DESIGN.md, "Changelog"), set by the host - in the repo it's the
        // file the runner's claudeborgen.json "changelogFile" points at.
        public string ChangelogPath { get; set; }

        // Red-test fix rounds Claude gets before the conversation is flagged "behöver IT".
        public int MaxFixAttempts { get; set; } = 2;

        // No heartbeat for this long = Claude shown as offline.
        public TimeSpan OfflineAfter { get; set; } = TimeSpan.FromSeconds(60);

        // InAppFake only: pause per phase, long enough for the status changes to be visible.
        public TimeSpan FakePhaseDelay { get; set; } = TimeSpan.FromSeconds(3);

        public bool IsOn => Mode != ClaudeborgenMode.Off;
        public bool AgentApiOpen => Mode == ClaudeborgenMode.Agent || Mode == ClaudeborgenMode.FakeRunner;
        public bool IsFake => Mode == ClaudeborgenMode.InAppFake || Mode == ClaudeborgenMode.FakeRunner;

        // Reads the "Claudeborgen" section. `isolated` is the host's own "fake integrations" switch:
        // isolated always means a fake mode (InAppFake unless IsolatedMode=FakeRunner), never Agent.
        // Outside isolated mode the integration is Off unless Enabled=true, so production doesn't
        // grow new pages just because a deploy carried the code.
        public static ClaudeborgenSettings FromConfiguration(IConfiguration configuration, bool isolated, string dataPath)
        {
            var section = configuration.GetSection(SectionName);
            var settings = new ClaudeborgenSettings
            {
                DataDirectory = Path.Combine(dataPath, "claudeborgen"),
                AgentTokenSha256 = section["AgentTokenSha256"],
                CommitUrlTemplate = section["CommitUrlTemplate"],
            };

            if (isolated)
                settings.Mode = string.Equals(section["IsolatedMode"], nameof(ClaudeborgenMode.FakeRunner), StringComparison.OrdinalIgnoreCase)
                    ? ClaudeborgenMode.FakeRunner
                    : ClaudeborgenMode.InAppFake;
            else
                settings.Mode = section.GetValue<bool>("Enabled") ? ClaudeborgenMode.Agent : ClaudeborgenMode.Off;

            if (int.TryParse(section["MaxFixAttempts"], out var maxFixAttempts))
                settings.MaxFixAttempts = maxFixAttempts;
            if (int.TryParse(section["OfflineAfterSeconds"], out var offlineAfterSeconds))
                settings.OfflineAfter = TimeSpan.FromSeconds(offlineAfterSeconds);
            if (int.TryParse(section["FakePhaseDelayMilliseconds"], out var fakePhaseDelayMs))
                settings.FakePhaseDelay = TimeSpan.FromMilliseconds(fakePhaseDelayMs);

            return settings;
        }

        public string CommitUrl(string commit) =>
            string.IsNullOrWhiteSpace(CommitUrlTemplate) || string.IsNullOrWhiteSpace(commit)
                ? null
                : string.Format(CommitUrlTemplate, commit);
    }
}
