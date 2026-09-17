using System;
using System.Collections.Generic;
using System.Linq;

namespace Chalmers.ILL.Models.PartialPage
{
    public class ChalmersILLActionLogEntryModel : OrderItemPageModelBase
    {
        public ChalmersILLActionLogEntryModel(OrderItemModel orderItemModel) : base(orderItemModel) { }
    }
}