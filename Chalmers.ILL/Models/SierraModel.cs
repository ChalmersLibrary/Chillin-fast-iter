using System;
using System.Collections.Generic;

namespace Chalmers.ILL.Models
{
    public class SierraModel
    {
        public SierraModel()
        {
            adress = new List<SierraAddressModel>();
        }

        public Guid DbId { get; set; }

        public string id { get; set; }
        public string barcode { get; set; }
        public string pnum { get; set; }
        public int ptype { get; set; }
        public string email { get; set; }
        public string first_name { get; set; }
        public string last_name { get; set; }
        public string mblock { get; set; }
        public string home_library { get; set; }
        public string expdate { get; set; }
        public string home_library_pretty_name { get; set; }
        public int record_id { get; set; }
        public string aff { get; set; }
        public List<SierraAddressModel> adress { get; set; }
        public bool? active { get; set; }
        public bool? e_resource_access { get; set; }
        public string cid { get; set; }
    }

    public class SierraAddressModel
    {
        public Guid DbId { get; set; }

        public string addresscount { get; set; }
        public string addr1 { get; set; }
        public string addr2 { get; set; }
        public string addr3 { get; set; }
        public string village { get; set; }
        public string city { get; set; }
        public string region { get; set; }
        public string postal_code { get; set; }
        public string country { get; set; }
    }
}