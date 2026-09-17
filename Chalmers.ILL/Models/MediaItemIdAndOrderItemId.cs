using System;
using System.Collections.Generic;
using System.Linq;

namespace Chalmers.ILL.Models
{
    public class MediaItemIdAndOrderItemId
    {
        public MediaItemIdAndOrderItemId(string mediaItemId, int orderItemId)
        {
            MediaItemId = mediaItemId;
            OrderItemId = orderItemId;
        }

        public string MediaItemId { get; }
        public int OrderItemId { get; }
    }
}