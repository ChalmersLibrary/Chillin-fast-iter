using Chalmers.ILL.Configuration;
using Chalmers.ILL.Templates;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Web;
using Chalmers.ILL.Models;

namespace Chalmers.ILL.Patron
{
    // Not wired up in Bootstrapper.cs - IPatronDataProvider currently resolves to
    // FolioPatronDataProvider, and nothing constructs SierraCache. Left in place (fas 6 just
    // migrates its config access), flagged as dead code in TODO-remove-dotnet-framework.md, fas 6.
    public class SierraCache : IPatronDataProvider
    {
        ITemplateService _templateService;
        IAffiliationDataProvider _affiliationDataProvider;
        IChillinConfiguration _config;

        public SierraCache(ITemplateService templateService, IAffiliationDataProvider affiliationDataProvider, IChillinConfiguration config)
        {
            _templateService = templateService;
            _affiliationDataProvider = affiliationDataProvider;
            _config = config;
        }

        public IPatronDataProvider Connect()
        {
            // Not needed
            return this;
        }

        public IPatronDataProvider Disconnect()
        {
            // Not needed
            return this;
        }

        public Models.SierraModel GetPatronInfoFromLibraryCardNumber(string barcode)
        {
            return GetPatronInfoFromLibraryCardNumberOrPersonnummer(barcode, barcode);
        }

        public Models.SierraModel GetPatronInfoFromLibraryCardNumberOrPersonnummer(string barcode, string pnr)
        {
            var ret = new Models.SierraModel();

            var rgx = new Regex("[^a-zA-Z0-9]");

            var pnrWithoutDash = rgx.Replace(pnr, "");
            var pnrWithDash = pnrWithoutDash.Length == 10 ? pnrWithoutDash.Insert(6, "-") : pnrWithoutDash;

            // Only search on first barcode if there are multiple. Only search on exact pnr with or without dash.
            var query = "barcode:" + rgx.Replace(barcode, "") + "* OR pnum:(*" + pnrWithoutDash + "* OR *" + pnrWithDash + "*)";

            try
            {
                HttpWebRequest fileReq = (HttpWebRequest)HttpWebRequest.Create(_config.PatronCacheSolrQueryUrl + query + "&wt=json");

                if (!String.IsNullOrWhiteSpace(_config.PatronCacheSolrBasicAuthUsername) && !String.IsNullOrWhiteSpace(_config.PatronCacheSolrBasicAuthPassword))
                    fileReq.Headers.Add("Authorization", "Basic " + Convert.ToBase64String(System.Text.Encoding.GetEncoding("ISO-8859-1").GetBytes(_config.PatronCacheSolrBasicAuthUsername + ":" + _config.PatronCacheSolrBasicAuthPassword)));

                fileReq.CookieContainer = new CookieContainer();
                fileReq.AllowAutoRedirect = true;

                HttpWebResponse fileResp = (HttpWebResponse)fileReq.GetResponse();
                var outputStream = fileResp.GetResponseStream();

                var sr = new StreamReader(outputStream);
                var json = JsonConvert.DeserializeObject<dynamic>(sr.ReadToEnd());

                if (json.response.numFound == 1)
                {
                    FillInSierraModelFromSolrData(json.response.docs[0], ret);
                }

            }
            catch (Exception)
            {
                // NOP
            }

            return ret;
        }

        public SierraModel GetPatronInfoFromSierraId(string sierraId)
        {
            var ret = new Models.SierraModel();

            try
            {
                var query = "recordnum:" + sierraId;

                HttpWebRequest fileReq = (HttpWebRequest)HttpWebRequest.Create(_config.PatronCacheSolrQueryUrl + query + "&wt=json");

                if (!String.IsNullOrWhiteSpace(_config.PatronCacheSolrBasicAuthUsername) && !String.IsNullOrWhiteSpace(_config.PatronCacheSolrBasicAuthPassword))
                    fileReq.Headers.Add("Authorization", "Basic " + Convert.ToBase64String(System.Text.Encoding.GetEncoding("ISO-8859-1").GetBytes(_config.PatronCacheSolrBasicAuthUsername + ":" + _config.PatronCacheSolrBasicAuthPassword)));

                fileReq.CookieContainer = new CookieContainer();
                fileReq.AllowAutoRedirect = true;

                HttpWebResponse fileResp = (HttpWebResponse)fileReq.GetResponse();
                var outputStream = fileResp.GetResponseStream();

                var sr = new StreamReader(outputStream);
                var json = JsonConvert.DeserializeObject<dynamic>(sr.ReadToEnd());

                if (json.response.numFound == 1)
                {
                    FillInSierraModelFromSolrData(json.response.docs[0], ret);
                }

            }
            catch (Exception)
            {
                // NOP
            }

            return ret;
        }

        private void FillInSierraModelFromSolrData(dynamic recordData, /* out */ SierraModel result)
        {
            var address1 = BuildSierraAddressModel(((string)recordData.address));
            var address2 = BuildSierraAddressModel(((string)recordData.address2));
            var address3 = BuildSierraAddressModel(((string)recordData.address3));
            string patronName = recordData.pname;
            var patronNameParts = patronName.Split(',');
            string recordId = recordData.recordnum;

            if (address1 != null)
            {
                result.adress.Add(address1);
            }

            if (address2 != null)
            {
                result.adress.Add(address2);
            }

            if (address3 != null)
            {
                result.adress.Add(address3);
            }

            result.barcode = recordData.barcode;
            result.home_library = ((string)recordData.homelib).Trim();
            result.home_library_pretty_name = _templateService.GetPrettyLibraryNameFromLibraryAbbreviation(result.home_library);
            result.id = "0";
            result.email = recordData.email;

            if (patronNameParts.Length == 2)
            {
                result.first_name = patronNameParts[1].Trim();
                result.last_name = patronNameParts[0].Trim();
            }
            else
            {
                result.first_name = patronName;
            }

            result.mblock = recordData.mblock;
            result.ptype = recordData.ptype;
            result.record_id = Convert.ToInt32(recordId.Remove(recordId.Length - 1).Remove(0, 1));
            _affiliationDataProvider.GetAffiliationFromPersonNumber(recordData.pnum.ToString(), result);
        }

        private Models.SierraAddressModel BuildSierraAddressModel(string address)
        {
            Models.SierraAddressModel ret = null;

            if (!String.IsNullOrWhiteSpace(address))
            {
                ret = new Models.SierraAddressModel();
                var addressParts = address.Split('$');
                ret.addresscount = addressParts.Length.ToString();
                if (addressParts.Length > 0)
                {
                    ret.addr1 = addressParts[0];
                }

                if (addressParts.Length > 1)
                {
                    ret.addr2 = addressParts[1];
                }

                if (addressParts.Length > 2)
                {
                    ret.addr3 = addressParts[2];
                }
            }

            return ret;
        }

        public IList<SierraModel> GetPatrons(string query)
        {
            throw new NotImplementedException();
        }
    }
}