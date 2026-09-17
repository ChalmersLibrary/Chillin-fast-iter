using System;
using System.Collections.Generic;
using System.Linq;

namespace Chalmers.ILL.Models.PartialPage.DeliveryType
{
    public class ArticleInInfodisk : OrderItemPageModelBase
    {
        public bool DrmWarning { get; set; }
        public string ArticleDeliveryLibrary { get; set; }
        public string ArticleAvailableInInfodiskMailTemplate { get; set; }

        public ArticleInInfodisk(OrderItemModel orderItemModel) : base(orderItemModel) { }
    }
}