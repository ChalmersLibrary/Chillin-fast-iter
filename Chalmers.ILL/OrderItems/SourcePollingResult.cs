using System;
using System.Collections.Generic;
using System.Linq;

namespace Chalmers.ILL.OrderItems
{
    public class SourcePollingResult
    {
        public SourcePollingResult(string name)
        {
            SourceName = name;
            Messages = new List<string>();
        }

        public string SourceName { get; set; }
        public int NewOrders { get; set; }
        public int UpdatedOrders { get; set; }
        public int Errors { get; set; }
        public List<string> Messages { get; set; }
    }
}