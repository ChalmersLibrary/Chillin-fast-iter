using System;
using System.Collections.Generic;
using System.Linq;

namespace Chalmers.ILL.Models
{
    public class Lock
    {
        public int NodeId { get; set; }
        public string OrderId { get; set; }
        public DateTime CreateDate { get; set; }
        public DateTime UpdateDate { get; set; }
        public DateTime FollowUpDate { get; set; }
    }
    public class LockResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public int MemberId { get; set; }
        public string MemberName { get; set; }
        public List<Lock> List { get; set; }
    }
}