using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Chalmers.ILL.Models
{
    public class MediaItemModel
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public int OrderItemNodeId { get; set; }
        public DateTime CreateDate { get; set; }
        public string Url { get; set; }
        public Stream Data { get; set; }
        public string ContentType { get; set; }
    }
}