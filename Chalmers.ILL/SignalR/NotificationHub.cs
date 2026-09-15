using Microsoft.AspNetCore.SignalR;

namespace Chalmers.ILL.SignalR
{
    /*
     * A signalR hub, simply defines the connection point and available serverside method
     * but since we only use it to do perform server -> client communication
     * We can leave it blank
     * The actual notification happens in the notifier class
     */

    public class NotificationHub : Hub
    {
    }
}
