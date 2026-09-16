using System;
using System.Collections.Generic;

namespace Chalmers.ILL.Models
{
    public class LogItem
    {
        // Was [DatabaseGenerated(DatabaseGeneratedOption.Identity)] - EF assigned this on save.
        // Nothing does that anymore, so FileOrderItemManager.AppendLogItem sets it explicitly
        // (SilentAnonymization matches manually-edited log entries back to originals by this Id).
        public Guid Id { get; set; }

        public int OrderItemNodeId { get; set; }
        public int NodeId { get; set; }

        public string EventId { get; set; }
        public string Type { get; set; }
        public string Message { get; set; }
        public string MemberName { get; set; }
        public DateTime CreateDate { get; set; }
    }

    public class LogItems
    {
        public List<LogItem> LogItemsList { get; set; }
    }
}
