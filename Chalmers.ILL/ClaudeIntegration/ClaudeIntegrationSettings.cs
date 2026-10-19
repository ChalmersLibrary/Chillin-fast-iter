using System;
using System.IO;
using Microsoft.Extensions.Configuration;

namespace Chalmers.ILL.ClaudeIntegration
{
    public enum ClaudeIntegrationMode
    {
        // Integration hidden entirely: no pages, no status badge, no agent API.
        Off,

        // The real channel: the agent API is open to the runner in the devcontainer, authenticated
        // by token. Never on the isolated test server (see claudeborgen-DESIGN.md, "Kanalen och
        // testservern") - IsolationGuard enforces it.
        Agent,

        // Isolated mode only: the UI is on, but Claude is FakeClaude, in-process, and the agent API
        // is closed. Lets the whole flow be tried on the fast test server without real Claude.
        Fake
    }

    public class ClaudeIntegrationSettings
    {
        public const string SectionName = "ClaudeIntegration";

        public ClaudeIntegrationMode Mode { get; set; }
        public string DataDirectory { get; set; }

        // SHA-256 (hex) of the agent token; the token itself is only ever in the devcontainer.
        public string AgentTokenSha256 { get; set; }

        // e.g. "https://github.com/ChalmersLibrary/Chillin-fast-iter/commit/{0}". Optional; without
        // it commits are shown as plain hashes.
        public string CommitUrlTemplate { get; set; }

        // Red-test fix rounds Claude gets before the conversation is flagged "behöver IT".
        public int MaxFixAttempts { get; set; } = 2;

        // No heartbeat for this long = Claude shown as offline.
        public TimeSpan OfflineAfter { get; set; } = TimeSpan.FromSeconds(60);

        // FakeClaude only: pause per phase, long enough for the status changes to be visible.
        public TimeSpan FakePhaseDelay { get; set; } = TimeSpan.FromSeconds(3);

        public bool IsOn => Mode != ClaudeIntegrationMode.Off;

        // Isolated mode always means Fake, whatever the settings say - the real channel must never
        // be reachable there. Outside isolated mode the integration is Off unless explicitly
        // enabled, so production doesn't grow new pages just because a deploy carried the code.
        public static ClaudeIntegrationSettings FromConfiguration(IConfiguration configuration, bool isolated, string dataPath)
        {
            var section = configuration.GetSection(SectionName);
            var settings = new ClaudeIntegrationSettings
            {
                DataDirectory = Path.Combine(dataPath, "claude"),
                AgentTokenSha256 = section["AgentTokenSha256"],
                CommitUrlTemplate = section["CommitUrlTemplate"],
            };

            if (isolated)
                settings.Mode = ClaudeIntegrationMode.Fake;
            else
                settings.Mode = section.GetValue<bool>("Enabled") ? ClaudeIntegrationMode.Agent : ClaudeIntegrationMode.Off;

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
