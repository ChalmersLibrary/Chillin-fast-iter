using System;
using System.Collections.Generic;
using System.Linq;

namespace Chalmers.ILL.Models.PartialPage.DeliveryType
{
    public class ArticleByEmail : OrderItemPageModelBase
    {
        public string ArticleDeliveryByMailTemplate { get; set; }
        public bool DrmWarning { get; set; }

        public ArticleByEmail(OrderItemModel orderItemModel) : base(orderItemModel) { }
    }
}