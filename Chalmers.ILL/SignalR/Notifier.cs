using System;
using Microsoft.AspNetCore.SignalR;
using Chalmers.ILL.Models;

namespace Chalmers.ILL.SignalR
{
    public class Notifier : INotifier
    {
        private readonly IHubContext<NotificationHub> _hubContext;

        public Notifier(IHubContext<NotificationHub> hubContext)
        {
            _hubContext = hubContext;
        }

        public void ReportNewOrderItemUpdate(OrderItemModel orderItem)
        {
            // Extract the chillin order status code from the Status string "NN:Description"
            int chillinOrderStatusId = 0;
            if (!string.IsNullOrEmpty(orderItem.Status))
            {
                var prefix = orderItem.Status.Split(':')[0];
                int.TryParse(prefix, out chillinOrderStatusId);
            }

            // create a notication object to send to the clients
            var n = new OrderItemNotification
            {
                NodeId = orderItem.NodeId,
                EditedBy = orderItem.EditedBy,
                EditedByMemberName = orderItem.EditedByMemberName,
                SignificantUpdate = true,
                IsPending = chillinOrderStatusId == 1 || chillinOrderStatusId == 2 || chillinOrderStatusId == 9 || (chillinOrderStatusId > 2 && chillinOrderStatusId < 5 && DateTime.Now > orderItem.FollowUpDate),
                UpdateFromMail = false
            };

            Broadcast(n);
        }

        public void UpdateOrderItemUpdate(int nodeId, string editedBy, string editedByMemberName, bool significant = false, bool isPending = false, bool updateFromMail = false)
        {
            var n = new OrderItemNotification
            {
                NodeId = nodeId,
                EditedBy = editedBy,
                EditedByMemberName = editedByMemberName,
                SignificantUpdate = significant,
                IsPending = isPending,
                UpdateFromMail = updateFromMail
            };

            Broadcast(n);
        }

        // ReportNewOrderItemUpdate/UpdateOrderItemUpdate are called synchronously (void) from many
        // places throughout the app; making INotifier async would ripple through all of them, so
        // this blocks on the hub send instead. SendAsync to a single "All" group completes fast
        // (in-memory broadcast, single instance - no backplane, see Fastställda designbeslut), so
        // the blocking window is small.
        private void Broadcast(OrderItemNotification n) =>
            _hubContext.Clients.All.SendAsync("updateStream", n).GetAwaiter().GetResult();
    }

    // Significant update indicates that this is something else than a lock/unlock event.
    // IsPending indicates that the item should exist in the list on the order page.
    public class OrderItemNotification
    {
        public int NodeId { get; set; }
        public string EditedBy { get; set; }
        public string EditedByMemberName { get; set; }
        public bool SignificantUpdate { get; set; }
        public bool IsPending { get; set; }
        public bool UpdateFromMail { get; set; }
    }
}
