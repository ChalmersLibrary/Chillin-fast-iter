using System;
using Chalmers.ILL.Members;
using Microsoft.AspNetCore.Mvc;

namespace Chalmers.ILL.ClaudeIntegration
{
    // The integration's pages, at /Claude. Everyone who can see Chillin can read everything here
    // (claudeborgen-DESIGN.md, "Synlighet"). [AllowViewer] lets the read-only accounts - Viewer and
    // the two Claudeborgen roles, see ViewerReadOnlyFilter - past that filter for the whole
    // controller, including the POSTs: the filter is about Chillin's own data, and every write here
    // checks IClaudeIntegrationAccess itself instead.
    [AllowViewer]
    public class ClaudeController : Controller
    {
        private const string ViewRoot = "~/ClaudeIntegration/Views/";
        private const string ErrorKey = "ClaudeError";

        private readonly ClaudeIntegrationService _service;
        private readonly IClaudeIntegrationAccess _access;
        private readonly IMemberInfoManager _memberInfoManager;

        public ClaudeController(ClaudeIntegrationService service, IClaudeIntegrationAccess access, IMemberInfoManager memberInfoManager)
        {
            _service = service;
            _access = access;
            _memberInfoManager = memberInfoManager;
        }

        private bool IsOff => !_service.Settings.IsOn;
        private ClaudeAccess Access => _access.For(User);
        private string Author => User.Identity?.Name ?? "?";

        [HttpGet]
        public IActionResult Index()
        {
            if (IsOff) return NotFound();
            return View(ViewRoot + "Index.cshtml", BuildModel(includeLayout: true));
        }

        [HttpGet]
        public IActionResult Conversation(int id)
        {
            if (IsOff) return NotFound();
            var model = BuildModel(includeLayout: true, conversationId: id);
            if (model.Conversation == null) return NotFound();
            return View(ViewRoot + "Conversation.cshtml", model);
        }

        // Fragments the page re-fetches when ClaudeHub pings (see wwwroot/scripts/claude.js).
        [HttpGet]
        public IActionResult Badge()
        {
            if (IsOff) return NotFound();
            return PartialView(ViewRoot + "_Badge.cshtml", _service.GetStatus());
        }

        [HttpGet]
        public IActionResult IndexContent()
        {
            if (IsOff) return NotFound();
            return PartialView(ViewRoot + "_IndexContent.cshtml", BuildModel(includeLayout: false));
        }

        [HttpGet]
        public IActionResult ConversationContent(int id)
        {
            if (IsOff) return NotFound();
            var model = BuildModel(includeLayout: false, conversationId: id);
            if (model.Conversation == null) return NotFound();
            return PartialView(ViewRoot + "_ConversationContent.cshtml", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Start(string title, string body)
        {
            if (IsOff) return NotFound();
            if (!Access.CanStartAndClose) return Forbid();

            return Attempt(() =>
            {
                var conversation = _service.Start(title, body, Author);
                return RedirectToAction(nameof(Conversation), new { id = conversation.Id });
            }, () => RedirectToAction(nameof(Index)));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Reply(int id, string body)
        {
            if (IsOff) return NotFound();
            if (!Access.CanWrite) return Forbid();

            return Attempt(() =>
            {
                _service.AddMessage(id, body, Author);
                return RedirectToAction(nameof(Conversation), new { id });
            }, () => RedirectToAction(nameof(Conversation), new { id }));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Done(int id, string changelogText)
        {
            if (IsOff) return NotFound();
            if (!Access.CanStartAndClose) return Forbid();

            return Attempt(() =>
            {
                _service.MarkDone(id, Author, changelogText);
                return RedirectToAction(nameof(Conversation), new { id });
            }, () => RedirectToAction(nameof(Conversation), new { id }));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Cancel(int id)
        {
            if (IsOff) return NotFound();
            if (!Access.CanStartAndClose) return Forbid();

            return Attempt(() =>
            {
                _service.Cancel(id, Author);
                return RedirectToAction(nameof(Conversation), new { id });
            }, () => RedirectToAction(nameof(Conversation), new { id }));
        }

        private IActionResult Attempt(Func<IActionResult> action, Func<IActionResult> onError)
        {
            try
            {
                return action();
            }
            catch (ClaudeIntegrationException e)
            {
                TempData[ErrorKey] = e.Message;
                return onError();
            }
        }

        private ClaudePageModel BuildModel(bool includeLayout, int? conversationId = null)
        {
            var model = new ClaudePageModel
            {
                Settings = _service.Settings,
                Access = Access,
                Status = _service.GetStatus(),
                NeedingAttention = _service.NeedingAttention(),
                Error = TempData[ErrorKey] as string,
            };

            if (conversationId != null)
                model.Conversation = _service.Get(conversationId.Value);
            else
                model.Conversations = _service.All();

            if (includeLayout)
                _memberInfoManager.PopulateModelWithMemberData(Request, Response, model);

            return model;
        }
    }
}
