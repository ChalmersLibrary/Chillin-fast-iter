using System;
using System.Collections.Generic;
using System.Linq;

namespace Chalmers.ILL.Models
{
    public class OrderItemPageModelBase
    {
        private OrderItemModel _orderItemModel;
        public OrderItemModel OrderItem { get { return _orderItemModel; } }

        public List<DropdownOption> AvailableTypes { get; set; }
        public List<DropdownOption> AvailableStatuses { get; set; }
        public List<DropdownOption> AvailableDeliveryLibraries { get; set; }
        public List<DropdownOption> AvailableCancellationReasons { get; set; }
        public List<DropdownOption> AvailablePurchasedMaterials { get; set; }

        public OrderItemPageModelBase(OrderItemModel orderItemModel)
        {
            _orderItemModel = orderItemModel;
        }
    }
}
