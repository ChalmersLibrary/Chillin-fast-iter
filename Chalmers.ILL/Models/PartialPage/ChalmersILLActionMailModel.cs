using System;
using System.Collections.Generic;
using System.Linq;

namespace Chalmers.ILL.Models.PartialPage
{
    public class ChalmersILLActionMailModel : OrderItemPageModelBase
    {
        public string SignatureTemplate { get; set; }
        public IList<Template> Templates { get; set; }

        public ChalmersILLActionMailModel(OrderItemModel orderItemModel) : base(orderItemModel)
        {
            Templates = new List<Template>();
        }
    }
}