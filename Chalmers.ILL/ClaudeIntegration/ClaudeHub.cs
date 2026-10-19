using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Chalmers.ILL.ClaudeIntegration
{
    // Server -> client only, like NotificationHub. Clients get a "something changed" ping with no
    // content and re-fetch the parts of the page they show, so nothing readable travels over the
    // hub - but it's still login-only, same as the pages.
    [Authorize]
    public class ClaudeHub : Hub
    {
    }

    public class SignalRClaudeUpdateBroadcaster : IClaudeUpdateBroadcaster
    {
        private readonly IHubContext<ClaudeHub> _hubContext;

        public SignalRClaudeUpdateBroadcaster(IHubContext<ClaudeHub> hubContext)
        {
            _hubContext = hubContext;
        }

        // Fire-and-forget: a missed ping only delays a page refresh until the client's own poll.
        public void ConversationChanged(int conversationId) =>
            _ = _hubContext.Clients.All.SendAsync("claudeUpdate", conversationId);

        public void StatusChanged() =>
            _ = _hubContext.Clients.All.SendAsync("claudeUpdate", (int?)null);
    }
}
