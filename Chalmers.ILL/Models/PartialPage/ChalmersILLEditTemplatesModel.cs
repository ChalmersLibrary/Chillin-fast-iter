using System;
using System.Collections.Generic;
using System.Linq;

namespace Chalmers.ILL.Models.PartialPage
{
    public class ChalmersILLEditTemplatesModel
    {
        public List<Template> Templates { get; set; }
        public List<string> AvailableOrderItemProperties { get; set; }

        public ChalmersILLEditTemplatesModel()
        {
            Templates = new List<Template>();
            AvailableOrderItemProperties = new List<string>();
        }
    }
}