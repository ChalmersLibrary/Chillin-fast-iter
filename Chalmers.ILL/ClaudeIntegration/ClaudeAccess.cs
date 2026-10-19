using System.Security.Claims;

namespace Chalmers.ILL.ClaudeIntegration
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

    // The host app's side of the boundary (see claudeborgen-DESIGN.md, "Gränsen mot värdappen"):
    // who may do what is the host's roles, not the integration's.
    public interface IClaudeIntegrationAccess
    {
        ClaudeAccess For(ClaimsPrincipal user);
    }
}
