using System;
using System.Collections.Generic;
using System.Linq;

namespace Chalmers.ILL.Models.PartialPage
{
    public class ChalmersILLActionPatronReturnDateModel : OrderItemPageModelBase
    {
        public ChalmersILLActionPatronReturnDateModel(OrderItemModel orderItemModel) : base(orderItemModel) { }

        public string ReturnDateChangedMailTemplate { get; set; }
    }
}