using System;

namespace Chalmers.ILL.Models
{
    public class HoldingBasic
    {
        public string CallNumber { get; set; } = "Interlibrary-in-loan";
        public bool DiscoverySuppress { get; set; } = true;
        public string InstanceId { get; set; }
        public string SourceId { get; set; }
        public string PermanentLocationId { get; set; }
        public string[] StatisticalCodeIds { get; set; }

        public HoldingBasic(string instanceId, string sourceId, string permanentLocationId, string statisticalCodeId)
        {
            if (string.IsNullOrEmpty(instanceId))
            {
                throw new ArgumentNullException(nameof(instanceId));
            }
            InstanceId = instanceId;
            SourceId = sourceId;
            PermanentLocationId = permanentLocationId;
            StatisticalCodeIds = new string[] { statisticalCodeId };
        }
    }
}