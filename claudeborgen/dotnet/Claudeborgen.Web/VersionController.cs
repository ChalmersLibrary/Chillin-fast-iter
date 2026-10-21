using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Claudeborgen
{
    // Which commit this deployment was built from (DESIGN.md, "Driftsatt"). The runner asks the fast
    // test server, to tell the owner when a change is actually there to try. Open without login: the
    // runner doesn't log in to the app, and a commit id is all it shows.
    [AllowAnonymous]
    public class ClaudeborgenVersionController : Controller
    {
        private readonly ClaudeborgenSettings _settings;

        public ClaudeborgenVersionController(ClaudeborgenSettings settings)
        {
            _settings = settings;
        }

        [HttpGet("claudeborgen/version")]
        public IActionResult Version()
        {
            if (!_settings.IsOn) return NotFound();
            return new JsonResult(new { commit = _settings.DeployedCommit() }, ClaudeAgentApiController.ApiJson);
        }
    }
}
