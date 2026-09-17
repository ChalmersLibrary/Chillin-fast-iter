using System;
using System.Collections.Generic;
using System.Linq;

namespace Chalmers.ILL.Models
{
    public class Template
    {
        public int Id { get; set; }
        public string NodeName { get; set; }
        public DateTime CreateDate { get; set; }
        public DateTime UpdateDate { get; set; }
        public string NodeTypeAlias { get; set; }
        public string Description { get; set; }
        public string Data { get; set; }
        public bool Automatic { get; set; }
        public bool Acquisition { get; set; }
    }
}