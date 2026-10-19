using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Chalmers.ILL.ClaudeIntegration
{
    // The runner's way in (see claudeborgen-DESIGN.md, "Agent-API"). Machine-to-machine, so it's
    // [AllowAnonymous] past the cookie login like Chillin's other such endpoints, and authenticates
    // with its own token instead: "Authorization: Bearer <token>", compared by SHA-256 against
    // ClaudeIntegration:AgentTokenSha256. Only ever open in Agent mode - 404 otherwise, so the
    // fast test server (Fake mode) has no way in at all.
    //
    // Gives access to the conversations and nothing else in the app.
    [AllowAnonymous]
    [ApiController]
    [Route("api/claude")]
    public class ClaudeAgentApiController : Controller
    {
        private readonly ClaudeIntegrationService _service;

        public ClaudeAgentApiController(ClaudeIntegrationService service)
        {
            _service = service;
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            var settings = _service.Settings;
            if (settings.Mode != ClaudeIntegrationMode.Agent)
            {
                context.Result = NotFound();
                return;
            }

            if (!IsValidToken(Request.Headers.Authorization.ToString(), settings.AgentTokenSha256))
            {
                context.Result = Unauthorized();
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
            public string Activity { get; set; }
            public int? ConversationId { get; set; }
            public string Phase { get; set; }
        }

        public class ReplyRequest
        {
            public string Body { get; set; }
            public List<string> Commits { get; set; }
            public List<string> Links { get; set; }
            public string SessionId { get; set; }
            public decimal? CostUsd { get; set; }
            public int? DurationSeconds { get; set; }
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

        [HttpPost("heartbeat")]
        public IActionResult Heartbeat([FromBody] HeartbeatRequest request)
        {
            var activity = string.Equals(request?.Activity, "Working", StringComparison.OrdinalIgnoreCase) ? ClaudeActivity.Working : ClaudeActivity.Idle;
            _service.Heartbeat(activity, request?.ConversationId, request?.Phase);
            return Ok();
        }

        [HttpGet("work")]
        public IActionResult Work() =>
            Ok(_service.Work().Select(c => new { c.Id, c.Title, State = c.State.ToString(), c.SessionId }));

        [HttpGet("conversations/{id:int}")]
        public IActionResult Get(int id)
        {
            var conversation = _service.Get(id);
            return conversation == null ? NotFound() : Ok(conversation);
        }

        [HttpPost("conversations/{id:int}/claim")]
        public IActionResult Claim(int id) => Guarded(() => _service.Claim(id));

        [HttpPost("conversations/{id:int}/reply")]
        public IActionResult Reply(int id, [FromBody] ReplyRequest request) =>
            Guarded(() => _service.Reply(id, request?.Body, request?.Commits, request?.Links, request?.SessionId, request?.CostUsd, request?.DurationSeconds));

        [HttpPost("conversations/{id:int}/tests")]
        public IActionResult Tests(int id, [FromBody] TestsRequest request) =>
            Guarded(() => _service.ReportTests(id, request?.Commit, request?.Green ?? false, request?.Summary));

        [HttpPost("conversations/{id:int}/crash")]
        public IActionResult Crash(int id, [FromBody] CrashRequest request) =>
            Guarded(() => _service.ReportCrash(id, request?.Summary));

        private IActionResult Guarded(Action action) => Guarded(() => { action(); return null; });

        private IActionResult Guarded(Func<object> action)
        {
            try
            {
                var result = action();
                return result == null ? Ok() : Ok(result);
            }
            catch (ClaudeIntegrationException e)
            {
                return Conflict(new { e.Message });
            }
        }
    }
}
