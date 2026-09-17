using Chalmers.ILL.Models.Mail;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Chalmers.ILL.Mail
{
    public class DelayedMailOperation
    {
        public int InternalOrderId { get; set; }
        public OutgoingMailModel Mail { get; set; }
        public List<LogMessage> LogMessages { get; set; }
        public string NewStatus { get; set; }
        public bool ShouldBeProcessed { get; set; }

        public DelayedMailOperation(int internalOrderId, OutgoingMailModel mail)
        {
            this.InternalOrderId = internalOrderId;
            this.Mail = mail;
            LogMessages = new List<LogMessage>();
            ShouldBeProcessed = false;
        }

        public DelayedMailOperation(int orderId) : this(orderId, null) { }
        public DelayedMailOperation(int internalOrderId, string orderId, string patronName, string patronEmail) :
            this(internalOrderId, new OutgoingMailModel(orderId, patronName, patronEmail))
        { }
    }
}