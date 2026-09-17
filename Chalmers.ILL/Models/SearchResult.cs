using System;
using System.Collections.Generic;
using System.Linq;

namespace Chalmers.ILL.Models
{
    public class SearchResult
    {
        public long Count { get; set; }
        public IEnumerable<OrderItemModel> Items { get; set; }
    }
}