using System;
using System.Collections.Generic;
using System.Linq;

namespace Chalmers.ILL.Models.Mail
{
    public class OutgoingMailModel
    {
        public string OrderId { get; set; }
        public string recipientName { get; set; }
        public string recipientEmail { get; set; }
        public string message { get; set; }
        public List<string> attachments { get; set; }

        public OutgoingMailModel()
        {
            // NOP
        }

        public OutgoingMailModel(string orderId, string recipientName, string recipientEmail)
        {
            this.OrderId = orderId;
            this.recipientName = recipientName;
            this.recipientEmail = recipientEmail;
        }

        public OutgoingMailModel(string orderId, OutgoingMailPackageModel pack)
        {
            OrderId = orderId;
            recipientName = pack.recipientName;
            recipientEmail = pack.recipientEmail;
            message = pack.message;
            attachments = pack.attachments;
        }
    }
}