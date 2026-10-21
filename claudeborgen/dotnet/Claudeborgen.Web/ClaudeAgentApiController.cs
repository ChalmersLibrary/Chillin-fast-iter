using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Claudeborgen
{
    // The runner's way in - the contract is claudeborgen/PROTOCOL.md, and this is its .NET
    // implementation. Machine-to-machine: [AllowAnonymous] past the host's cookie login, and
    // authenticated by its own token instead ("Authorization: Bearer <token>", compared by SHA-256
    // against Claudeborgen:AgentTokenSha256). 404 unless the mode opens it (Agent, FakeRunner).
    //
    // JSON in and out is camelCase with string enums, set here rather than inherited from the host's
    // MVC options - the protocol must not change with whatever a host app configures globally.
    [AllowAnonymous]
    [Route("api/claude")]
    public class ClaudeAgentApiController : Controller
    {
        public const int ProtocolVersion = 1;

        internal static readonly JsonSerializerOptions ApiJson = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        };

        private readonly ClaudeborgenService _service;

        public ClaudeAgentApiController(ClaudeborgenService service)
        {
            _service = service;
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            var settings = _service.Settings;
            if (!settings.AgentApiOpen)
            {
                context.Result = Error(StatusCodes.Status404NotFound, "Agent-API:t är inte öppet på den här servern.");
                return;
            }

            if (!IsValidToken(Request.Headers.Authorization.ToString(), settings.AgentTokenSha256))
            {
                context.Result = Error(StatusCodes.Status401Unauthorized, "Fel eller saknad agent-token.");
                return;
            }

            // On the isolated fast test server only a --fake runner may connect: a real one would
            // push and redeploy the very server its channel runs on.
            var declaresFake = string.Equals(Request.Headers[ClaudeborgenSettings.FakeRunnerHeader].ToString(), "true", StringComparison.OrdinalIgnoreCase);
            if (settings.Mode == ClaudeborgenMode.FakeRunner && !declaresFake)
            {
                context.Result = Error(StatusCodes.Status403Forbidden,
                    "Den här servern är isolerad och tar bara emot en runner i fejkläge (--fake).");
            }
        }

        public static bool IsValidToken(string authorizationHeader, string expectedSha256Hex)
        {
            const string prefix = "Bearer ";
            if (string.IsNullOrWhiteSpace(expectedSha256Hex) || authorizationHeader == null || !authorizationHeader.StartsWith(prefix, StringComparison.Ordinal))
                return false;

            var token = authorizationHeader.Substring(prefix.Length).Trim();
            if (token.Length == 0)
                return false;

            byte[] expected;
            try
            {
                expected = Convert.FromHexString(expectedSha256Hex.Trim());
            }
            catch (FormatException)
            {
                return false;
            }

            var actual = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }

        public class HeartbeatRequest
        {
            public ClaudeActivity Activity { get; set; }
            public int? ConversationId { get; set; }
            public string Phase { get; set; }
            public DateTimeOffset? PausedUntil { get; set; }
        }

        public class ReplyRequest
        {
            public string Body { get; set; }
            public List<string> Commits { get; set; }
            public List<string> Links { get; set; }
            public string SessionId { get; set; }
            public decimal? CostUsd { get; set; }
            public int? DurationSeconds { get; set; }
            public bool Question { get; set; }
        }

        public class TestsRequest
        {
            public string Commit { get; set; }
            public bool Green { get; set; }
            public string Summary { get; set; }
        }

        public class CrashRequest
        {
            public string Summary { get; set; }
        }

        public class ChangelogCommittedRequest
        {
            public string Commit { get; set; }
        }

        [HttpGet("info")]
        public IActionResult Info() =>
            Json(new { protocolVersion = ProtocolVersion, mode = _service.Settings.Mode.ToString(), maxFixAttempts = _service.Settings.MaxFixAttempts });

        [HttpPost("heartbeat")]
        public async Task<IActionResult> Heartbeat()
        {
            var request = await ReadBody<HeartbeatRequest>();
            _service.Heartbeat(request?.Activity ?? ClaudeActivity.Idle, request?.ConversationId, request?.Phase, request?.PausedUntil?.LocalDateTime);
            return NoContent();
        }

        public const int MaxWaitSeconds = 30;

        // ?kind=idea|task - the runner has a loop for each. ?wait=N (long polling, max 30 s): with
        // nothing to do, hold the request until something changes or N seconds pass - the runner
        // reacts at once instead of on its next poll, and asks far less often.
        [HttpGet("work")]
        public async Task<IActionResult> Work([FromQuery] string kind = null, [FromQuery] int wait = 0)
        {
            ConversationKind? filter = Enum.TryParse<ConversationKind>(kind, ignoreCase: true, out var k) ? k : null;
            var deadline = DateTime.UtcNow.AddSeconds(Math.Clamp(wait, 0, MaxWaitSeconds));
            var aborted = HttpContext.RequestAborted;

            for (;;)
            {
                var change = _service.NextChange();
                var work = _service.Work(filter);
                var remaining = deadline - DateTime.UtcNow;
                if (work.Count > 0 || remaining <= TimeSpan.Zero || aborted.IsCancellationRequested)
                    return Json(work.Select(c => new { c.Id, c.Kind, c.Title, c.State, c.SessionId }));

                await Task.WhenAny(change, Task.Delay(remaining, aborted));
            }
        }

        [HttpGet("conversations/{id:int}")]
        public IActionResult Get(int id)
        {
            var conversation = _service.Get(id);
            return conversation == null ? Error(StatusCodes.Status404NotFound, $"Samtal {id} finns inte.") : Json(conversation);
        }

        [HttpPost("conversations/{id:int}/claim")]
        public IActionResult Claim(int id) => Guarded(() => _service.Claim(id));

        [HttpPost("conversations/{id:int}/reply")]
        public async Task<IActionResult> Reply(int id)
        {
            var request = await ReadBody<ReplyRequest>();
            return Guarded(() => _service.Reply(id, request?.Body, request?.Commits, request?.Links, request?.SessionId, request?.CostUsd, request?.DurationSeconds, request?.Question ?? false));
        }

        [HttpPost("conversations/{id:int}/tests")]
        public async Task<IActionResult> Tests(int id)
        {
            var request = await ReadBody<TestsRequest>();
            return Guarded(() => _service.ReportTests(id, request?.Commit, request?.Green ?? false, request?.Summary));
        }

        [HttpPost("conversations/{id:int}/release")]
        public IActionResult Release(int id) => Guarded(() => _service.Release(id));

        [HttpPost("conversations/{id:int}/crash")]
        public async Task<IActionResult> Crash(int id)
        {
            var request = await ReadBody<CrashRequest>();
            return Guarded(() => _service.ReportCrash(id, request?.Summary));
        }

        // Done conversations whose changelog entry the runner should commit (PROTOCOL.md).
        [HttpGet("changelog/pending")]
        public IActionResult PendingChangelog() =>
            Json(_service.PendingChangelog().Select(c => new
            {
                conversationId = c.Id,
                date = (c.DoneAt ?? c.CreatedAt).ToString("yyyy-MM-dd"),
                text = c.ChangelogText ?? c.Title,
            }));

        // Cancelled work whose commits the runner should revert (PROTOCOL.md).
        [HttpGet("reverts/pending")]
        public IActionResult PendingReverts() =>
            Json(_service.PendingReverts().Select(r => new { conversationId = r.ConversationId, title = r.Title, commits = r.Commits }));

        public class RevertedRequest
        {
            public string Commit { get; set; }
            public string Summary { get; set; }
        }

        [HttpPost("conversations/{id:int}/reverted")]
        public async Task<IActionResult> Reverted(int id)
        {
            var request = await ReadBody<RevertedRequest>();
            return Guarded(() => _service.MarkReverted(id, request?.Commit));
        }

        [HttpPost("conversations/{id:int}/revert-failed")]
        public async Task<IActionResult> RevertFailed(int id)
        {
            var request = await ReadBody<RevertedRequest>();
            return Guarded(() => _service.MarkRevertFailed(id, request?.Summary));
        }

        // Delivered rounds not yet seen live on the fast test server (PROTOCOL.md).
        [HttpGet("deployments/pending")]
        public IActionResult PendingDeployments() =>
            Json(_service.PendingDeployments().Select(d => new { conversationId = d.ConversationId, round = d.Round, commit = d.Commit }));

        [HttpPost("conversations/{id:int}/rounds/{round:int}/deployed")]
        public IActionResult Deployed(int id, int round) => Guarded(() => _service.MarkDeployed(id, round));

        // Removed conversations whose Claude Code sessions the runner should delete (PROTOCOL.md).
        [HttpGet("cleanup")]
        public IActionResult PendingCleanup() =>
            Json(_service.PendingSessionCleanup().Select(r => new { conversationId = r.ConversationId, sessionIds = r.SessionIds }));

        [HttpPost("conversations/{id:int}/cleaned")]
        public IActionResult Cleaned(int id) => Guarded(() => _service.MarkSessionsCleaned(id));

        [HttpPost("conversations/{id:int}/changelog")]
        public async Task<IActionResult> ChangelogCommitted(int id)
        {
            var request = await ReadBody<ChangelogCommittedRequest>();
            return Guarded(() => _service.MarkChangelogCommitted(id, request?.Commit));
        }

        private async Task<T> ReadBody<T>() where T : class
        {
            if (Request.ContentLength == 0)
                return null;
            try
            {
                return await JsonSerializer.DeserializeAsync<T>(Request.Body, ApiJson);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private new IActionResult Json(object value) => new JsonResult(value, ApiJson);

        private IActionResult Error(int status, string message) =>
            new JsonResult(new { message }, ApiJson) { StatusCode = status };

        private IActionResult Guarded(Action action) => Guarded(() => { action(); return null; });

        private IActionResult Guarded(Func<object> action)
        {
            try
            {
                var result = action();
                return result == null ? NoContent() : Json(result);
            }
            catch (ClaudeborgenException e)
            {
                return Error(StatusCodes.Status409Conflict, e.Message);
            }
        }
    }
}
