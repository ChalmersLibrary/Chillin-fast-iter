using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Chalmers.ILL.Configuration;
using Chalmers.ILL.Models;
using Newtonsoft.Json;
namespace Chalmers.ILL.Patron
{
    public class PdbPersonDataProvider : IPersonDataProvider
    {
        private static readonly log4net.ILog _log = log4net.LogManager.GetLogger(typeof(PdbPersonDataProvider));
        private HttpClient _httpClient;
        private IChillinConfiguration _config;
        public PdbPersonDataProvider(HttpClient httpClient, IChillinConfiguration config)
        {
            _httpClient = httpClient;
            _config = config;
        }

        public SierraModel GetPatronInfoFromLibraryCidPersonnummerOrEmail(string cidOrPersonnummer, string email)
        {
            SierraModel res = new SierraModel(); // Always return sierra model, empty cid means failure

            try
            {
                string url = "";
                Task<string> task = null;
                dynamic userData = null;

                if (cidOrPersonnummer != null)
                {
                    cidOrPersonnummer = cidOrPersonnummer.Trim();
                    var pnrRx = new Regex("^[0-9]");
                    if (pnrRx.IsMatch(cidOrPersonnummer))
                    {
                        // If personnummer, remove -
                        cidOrPersonnummer = cidOrPersonnummer.Replace("-", "");
                    }
                }

                if (email != null)
                {
                    email = email.Trim();
                }

                try
                {
                    url = _config.LibPSearchUrl + "/api-search?apikey=" + _config.LibPSearchApiKey + "&pnr=" + cidOrPersonnummer;
                    task = _httpClient.GetStringAsync(url);
                    task.Wait();
                    userData = JsonConvert.DeserializeObject<dynamic>(task.Result);
                }
                catch (Exception)
                {
                    // Do nothing, we should be able to submit junk and retry with email
                    // NOOP
                }

                if (userData == null || userData.errorStr != null)
                {
                    // Try again with email instead
                    url = _config.LibPSearchUrl + "/api-search?apikey=" + _config.LibPSearchApiKey + "&pnr=" + email;
                    task = _httpClient.GetStringAsync(url);
                    task.Wait();
                    userData = JsonConvert.DeserializeObject<dynamic>(task.Result);
                }

                if (userData != null && userData.errorStr == null)
                {
                    res = new SierraModel();

                    res.first_name = userData.fname;
                    res.last_name = userData.lname;
                    res.pnum = userData.pnr;
                    res.email = userData.email;
                    res.cid = userData.cid + " (" + userData.fname + " " + userData.lname + ")";
                    res.e_resource_access = userData.eResourceAccess;

                    IEnumerable<dynamic> activeCategories = userData.activeCategories;
                    var isStudent = activeCategories.Any(cs => (cs as IEnumerable<dynamic>).Any(c => c.name == "student"));
                    var isEmployee = activeCategories.Any(cs => (cs as IEnumerable<dynamic>).Any(c => c.name == "employee"));

                    if (isEmployee)
                    {
                        res.aff = "Anställd";
                    }
                    else if (isStudent)
                    {
                        res.aff = "Student";
                    }
                }
            }
            catch (Exception e)
            {
                // TODO: Should inject this
                _log.Error("An error occured when fetching person data from PDB.", e);
            }

            return res;
        }
    }
}