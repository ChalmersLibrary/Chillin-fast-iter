using System;

namespace Chalmers.ILL.Models
{
    public class InstanceBasic
    {
        public string Title { get; set; }
        public string Source { get; set; } = "FOLIO";
        public string InstanceTypeId { get; set; }
        public bool DiscoverySuppress { get; set; } = true;
        public string StatusId { get; set; }
        public string ModeOfIssuanceId { get; set; }
        public Identifier[] Identifiers { get; set; }
        public string[] StatisticalCodeIds { get; set; }

        public InstanceBasic(string title, string orderId, string instanceTypeId, string statusId, string modeOfIssuanceId, string identifierTypeId, string statisticalCodeId)
        {
            if (string.IsNullOrEmpty(title))
            {
                throw new ArgumentNullException(nameof(title));
            }
            if (string.IsNullOrEmpty(orderId))
            {
                throw new ArgumentNullException(nameof(orderId));
            }
            Title = title;
            InstanceTypeId = instanceTypeId;
            StatusId = statusId;
            ModeOfIssuanceId = modeOfIssuanceId;
            StatisticalCodeIds = new string[] { statisticalCodeId };
            Identifiers = new Identifier[]
            {
                new Identifier
                {
                    Value = orderId,
                    IdentifierTypeId = identifierTypeId
                }
            };
        }
    }
}