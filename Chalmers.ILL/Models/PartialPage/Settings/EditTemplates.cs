using System;
using System.Collections.Generic;
using System.Linq;

namespace Chalmers.ILL.Models.PartialPage.Settings
{
    public class EditTemplates
    {
        public List<Template> Templates { get; set; }
        public List<string> AvailableOrderItemProperties { get; set; }

        public EditTemplates()
        {
            Templates = new List<Template>();
            AvailableOrderItemProperties = new List<string>();
        }
    }
}