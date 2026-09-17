using System;
using System.Collections.Generic;
using System.Linq;

namespace Chalmers.ILL.Models.PartialPage.DeliveryType
{
    public class BookInstantLoan : OrderItemPageModelBase
    {
        public string BookAvailableMailTemplate { get; set; }

        public BookInstantLoan(OrderItemModel orderItemModel) : base(orderItemModel) { }
    }
}