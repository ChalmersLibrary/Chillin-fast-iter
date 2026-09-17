using System;
using System.Collections.Generic;
using System.Linq;

namespace Chalmers.ILL.Models.Page
{
    public class ChalmersILLDiskPageModel : ChalmersILLModel
    {
        public ChalmersILLDiskPageModel()
        {
            OrderItems = new List<OrderItemModel>();
        }

        public IEnumerable<OrderItemModel> OrderItems { get; set; }
    }
}