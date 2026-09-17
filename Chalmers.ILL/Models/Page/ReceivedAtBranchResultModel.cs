using System;
using System.Collections.Generic;
using System.Linq;

namespace Chalmers.ILL.Models.Page
{
    public class ReceivedAtBranchResultModel : OrderItemPageModelBase
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; }

        public ReceivedAtBranchResultModel(OrderItemModel orderItemModel) : base(orderItemModel) { }
    }
}