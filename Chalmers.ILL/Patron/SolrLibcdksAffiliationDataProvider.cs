using Chalmers.ILL.Configuration;
using Chalmers.ILL.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Web;

namespace Chalmers.ILL.Patron
{
    // Not wired up in Bootstrapper.cs - IAffiliationDataProvider currently resolves to
    // PdbAffiliationDataProvider, and nothing constructs this class. Left in place (fas 6 just
    // migrates its config access), flagged as dead code in TODO-remove-dotnet-framework.md, fas 6.
    public class SolrLibcdksAffiliationDataProvider : IAffiliationDataProvider
    {
        private readonly IChillinConfiguration _config;

        public SolrLibcdksAffiliationDataProvider(IChillinConfiguration config)
        {
            _config = config;
        }

        public void GetAffiliationFromPersonNumber(string pnum, /*out*/ SierraModel sm)
        {
            sm.aff = "N/A";

            var fixedPnum = FixPersonNumber(pnum);

            if (!String.IsNullOrEmpty(fixedPnum))
            {
                // Only search on first barcode if there are multiple. Only search on exact pnr with or without dash.
                var query = "pernum:" + fixedPnum;

                try
                {
                    HttpWebRequest fileReq = (HttpWebRequest)HttpWebRequest.Create(_config.PatronAffiliationSolrQueryUrl + query + "&wt=json");

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
                        sm.aff = json.response.docs[0].aff.ToString();
                    }

                }
                catch (Exception)
                {
                    sm.aff = "Misslyckad inläsning";
                }
            }
        }

        #region Private methods

        private string FixPersonNumber(string pnum)
        {
            var res = pnum.Replace("-", "");
            if (res.Length == 12)
            {
                res = res.Substring(2);
            }
            else if (res.Length < 10)
            {
                res = ""; // We don't want to search with less than 10.
            }

            return res;
        }

        #endregion
    }
}