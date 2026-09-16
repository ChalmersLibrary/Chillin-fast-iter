using System;

namespace Chalmers.ILL.Models
{
    public class OrderAttachment
    {
        public Guid DbId { get; set; }

        public string MediaItemNodeId { get; set; }
        public string Title { get; set; }
        public string Link { get; set; }
    }
}