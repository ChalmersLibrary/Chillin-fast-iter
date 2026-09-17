using System;
using System.Collections.Generic;
using System.Linq;

namespace Chalmers.ILL.Models.Mail
{
    public class OutgoingMailPackageModel
    {
        public int nodeId { get; set; } 
        public string recipientEmail { get; set; } 
        public string recipientName { get; set; } 
        public string message { get; set; }
        public int newStatusId { get; set; }
        public int newCancellationReasonId { get; set; }
        public int newPurchasedMaterialId { get; set; }
        public string newFollowUpDate { get; set; }
        public List<string> attachments { get; set; }
    }
}