using System;
using Microsoft.AspNet.SignalR;
using System.Configuration;
using Chalmers.ILL.Models;

namespace Chalmers.ILL.SignalR
{
    public class Notifier : INotifier
    {
        public void ReportNewOrderItemUpdate(OrderItemModel orderItem)
        {
            // get the NotificationHub
            var context = GlobalHost.ConnectionManager.GetHubContext<NotificationHub>();

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

            // this calls the javascript method updateStream(message) in all connected browsers
            context.Clients.All.updateStream(n);
        }

        public void UpdateOrderItemUpdate(int nodeId, string editedBy, string editedByMemberName, bool significant = false, bool isPending = false, bool updateFromMail = false)
        {
            // get the NotificationHub
            var context = GlobalHost.ConnectionManager.GetHubContext<NotificationHub>();

            var n = new OrderItemNotification
            {
                NodeId = nodeId,
                EditedBy = editedBy,
                EditedByMemberName = editedByMemberName,
                SignificantUpdate = significant,
                IsPending = isPending,
                UpdateFromMail = updateFromMail
            };

            // this calls the javascript method updateStream(message) in all connected browsers
            context.Clients.All.updateStream(n);
        }
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