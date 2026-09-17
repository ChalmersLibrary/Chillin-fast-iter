using System;
using System.Collections.Generic;
using System.Linq;

namespace Chalmers.ILL.Models
{
    public class OrderItemSeedModel
    {
        public string Id { get; set; }
        public string PatronName { get; set; }
        public string PatronEmail { get; set; }
        public string PatronCardNumber { get; set; }
        public string DeliveryLibrarySigel { get; set; }
        public string Message { get; set; }
        public string MessagePrefix { get; set; }
        public SierraModel SierraPatronInfo { get; set; }
    }
}