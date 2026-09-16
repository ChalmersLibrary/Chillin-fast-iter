using System;
using System.Collections.Generic;

namespace Chalmers.ILL.Models
{
    public class ItemBasic
    {
        public string Barcode { get; set; }
        public bool DiscoverySuppress { get; set; } = true;
        public string MaterialTypeId { get; set; }
        public string PermanentLoanTypeId { get; set; }
        public string HoldingsRecordId { get; set; }
        public Status Status { get; set; } = new Status();
        public List<CirculationNote> CirculationNotes { get; set; } = new List<CirculationNote>();
        public string[] StatisticalCodeIds { get; set; }

        public ItemBasic(string barcode, string holdingsRecordId, bool readOnlyAtLibrary, string materialTypeId, string statisticalCodeId, string permanentLoanTypeId, string permanentLoanTypeIdInHouse)
        {
            if (string.IsNullOrEmpty(barcode))
            {
                throw new ArgumentNullException(nameof(barcode));
            }
            if (string.IsNullOrEmpty(holdingsRecordId))
            {
                throw new ArgumentNullException(nameof(holdingsRecordId));
            }
            Barcode = barcode;
            HoldingsRecordId = holdingsRecordId;
            MaterialTypeId = materialTypeId;
            StatisticalCodeIds = new string[] { statisticalCodeId };
            PermanentLoanTypeId = readOnlyAtLibrary ?
                permanentLoanTypeIdInHouse :
                permanentLoanTypeId;
        }
    }

    public class Status
    {
        public string Name { get; set; } = "Available";
    }

    public class CirculationNote
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string NoteType { get; set; }
        public string Note { get; set; }
        public Source Source { get; set; }
        public string Date { get; set; } = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ssZ");
        public bool StaffOnly { get; set; } = true;
    }

    public class Source
    {
        public string Id { get; set; }
        public Personal Personal { get; set; }
    }
}