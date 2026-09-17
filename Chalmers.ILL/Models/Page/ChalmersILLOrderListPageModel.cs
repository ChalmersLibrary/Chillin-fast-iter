using System;
using System.Collections.Generic;
using System.Linq;

namespace Chalmers.ILL.Models.Page
{
    public class ChalmersILLOrderListPageModel : ChalmersILLModel
    {
        public SearchResult PendingOrderItems { get; set; }
        public SearchResult ManualAnonymizationItems { get; set; }
    }
}