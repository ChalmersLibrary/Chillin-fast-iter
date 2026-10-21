using System;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Claudeborgen
{
    // The human side. The overview lives in the host app where its changelog is (the host renders
    // "~/Views/Claudeborgen/_StartPageSection.cshtml"); a conversation opens at
    // /Claude/Conversation/{id}, as a full page the host wraps in its layout
    // (IClaudeborgenHost.ConversationPage). Login is always required; who may write is the host's
    // roles via IClaudeborgenHost.AccessFor, checked per action here.
    [Authorize]
    [Route("Claude")]
    public class ClaudeController : Controller
    {
        public const string ErrorKey = "ClaudeborgenError";
        private const string ViewRoot = "~/Views/Claudeborgen/";

        private readonly ClaudeborgenService _service;
        private readonly IClaudeborgenHost _host;

        public ClaudeController(ClaudeborgenService service, IClaudeborgenHost host)
        {
            _service = service;
            _host = host;
        }

        private bool IsOff => !_service.Settings.IsOn;
        private ClaudeAccess Access => _host.AccessFor(User);
        private string Author => User.Identity?.Name ?? "?";

        // Nothing of its own at /Claude - the overview is where the host renders it.
        [HttpGet("")]
        public IActionResult Index() => IsOff ? NotFound() : Redirect("/");

        [HttpGet("Conversation/{id:int}")]
        public IActionResult Conversation(int id)
        {
            if (IsOff) return NotFound();
            var model = BuildModel(conversationId: id);
            if (model.Conversation == null) return NotFound();
            if (model.Conversation.IsIdea) return Redirect(IdeaUrl(id)); // ideas are chatted on the start page
            return _host.ConversationPage(this, model);
        }

        // Fragments the page re-fetches when the hub pings (see wwwroot/claude.js).
        [HttpGet("IndexContent")]
        public IActionResult IndexContent()
        {
            if (IsOff) return NotFound();
            return PartialView(ViewRoot + "_IndexContent.cshtml", BuildModel());
        }

        [HttpGet("ConversationContent/{id:int}")]
        public IActionResult ConversationContent(int id)
        {
            if (IsOff) return NotFound();
            var model = BuildModel(conversationId: id);
            if (model.Conversation == null) return NotFound();
            return PartialView(ViewRoot + "_ConversationContent.cshtml", model);
        }

        [HttpPost("Start")]
        [ValidateAntiForgeryToken]
        public IActionResult Start(string title, string body)
        {
            if (IsOff) return NotFound();
            if (!Access.CanStartAndClose) return Forbid();

            return Attempt(() =>
            {
                var conversation = _service.Start(title, body, Author);
                return RedirectToAction(nameof(Conversation), new { id = conversation.Id });
            }, () => Redirect("/"));
        }

        [HttpPost("Reply/{id:int}")]
        [ValidateAntiForgeryToken]
        public IActionResult Reply(int id, string body)
        {
            if (IsOff) return NotFound();
            if (!Access.CanWrite) return Forbid();

            var back = BackTo(id);
            return Attempt(() =>
            {
                _service.AddMessage(id, body, Author);
                return back;
            }, () => back);
        }

        // DESIGN.md, "Idéer": a loose chat on the start page. Anyone who may write in conversations
        // may bounce ideas; only the object owner turns one into a task.
        [HttpPost("Idea")]
        [ValidateAntiForgeryToken]
        public IActionResult Idea(string body)
        {
            if (IsOff) return NotFound();
            if (!Access.CanWrite) return Forbid();

            return Attempt(() =>
            {
                var idea = _service.StartIdea(body, Author);
                return Redirect(IdeaUrl(idea.Id));
            }, () => Redirect("/"));
        }

        [HttpGet("IdeaChat/{id:int}")]
        public IActionResult IdeaChat(int id)
        {
            if (IsOff) return NotFound();
            var model = BuildModel(conversationId: id);
            // Someone else may have the chat open when it's turned into work or let go of: tell them,
            // rather than leave a chat that silently stops updating.
            if (model.Conversation == null || !model.Conversation.IsIdea)
                return PartialView(ViewRoot + "_IdeaGone.cshtml", model.Conversation);
            return PartialView(ViewRoot + "_IdeaChat.cshtml", model);
        }

        [HttpPost("Convert/{id:int}")]
        [ValidateAntiForgeryToken]
        public IActionResult Convert(int id, string title)
        {
            if (IsOff) return NotFound();
            if (!Access.CanStartAndClose) return Forbid();

            return Attempt(() =>
            {
                _service.ConvertToTask(id, title, Author);
                return RedirectToAction(nameof(Conversation), new { id });
            }, () => Redirect(IdeaUrl(id)));
        }

        public static string IdeaUrl(int id) => $"/?idea={id}#claude-idea";

        // Ideas live on the start page, tasks on their own page.
        private IActionResult BackTo(int id) =>
            _service.Get(id)?.IsIdea == true ? Redirect(IdeaUrl(id)) : RedirectToAction(nameof(Conversation), new { id });

        [HttpPost("Done/{id:int}")]
        [ValidateAntiForgeryToken]
        public IActionResult Done(int id, string changelogText)
        {
            if (IsOff) return NotFound();
            if (!Access.CanStartAndClose) return Forbid();

            return Attempt(() =>
            {
                _service.MarkDone(id, Author, changelogText);
                // The conversation is removed once its changelog entry is committed - back to
                // where the changelog is.
                return Redirect("/");
            }, () => RedirectToAction(nameof(Conversation), new { id }));
        }

        // Also "Släpp idén" - letting go of an idea is open to whoever may bounce ideas.
        [HttpPost("Cancel/{id:int}")]
        [ValidateAntiForgeryToken]
        public IActionResult Cancel(int id)
        {
            if (IsOff) return NotFound();
            var isIdea = _service.Get(id)?.IsIdea == true;
            if (!(isIdea ? Access.CanWrite : Access.CanStartAndClose)) return Forbid();

            return Attempt(() =>
            {
                _service.Cancel(id, Author);
                return Redirect("/"); // a cancelled conversation is removed right away
            }, () => RedirectToAction(nameof(Conversation), new { id }));
        }

        private IActionResult Attempt(Func<IActionResult> action, Func<IActionResult> onError)
        {
            try
            {
                return action();
            }
            catch (ClaudeborgenException e)
            {
                TempData[ErrorKey] = e.Message;
                return onError();
            }
        }

        private ClaudePageModel BuildModel(int? conversationId = null)
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

            return model;
        }
    }
}
