using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace Claudeborgen
{
    public class ClaudeAccess
    {
        public bool CanRead { get; set; }

        // Write in conversations (object owner and IT).
        public bool CanWrite { get; set; }

        // Start conversations, mark them done or cancelled (object owner).
        public bool CanStartAndClose { get; set; }

        // Sees the attention list prominently.
        public bool IsIt { get; set; }

        public static readonly ClaudeAccess None = new ClaudeAccess();
    }

    // The host app's side of the boundary (DESIGN.md, "Gränsen mot värdappen"). Everything that
    // depends on the host lives behind this: who may do what (the host's roles), and the full page
    // around a conversation (the host's layout).
    public interface IClaudeborgenHost
    {
        ClaudeAccess AccessFor(ClaimsPrincipal user);

        // Render /Claude/Conversation/{id} as a full page in the host's layout. The host's view
        // typically renders "~/Views/Claudeborgen/_ConversationPage.cshtml" with `model`.
        IActionResult ConversationPage(Controller controller, ClaudePageModel model);
    }
}
