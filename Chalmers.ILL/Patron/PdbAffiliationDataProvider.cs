using Chalmers.ILL.Configuration;
using Chalmers.ILL.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net.Http;

namespace Chalmers.ILL.Patron
{
    public class PdbAffiliationDataProvider : IAffiliationDataProvider
    {
        private HttpClient _httpClient;
        private IChillinConfiguration _config;
        public PdbAffiliationDataProvider(HttpClient httpClient, IChillinConfiguration config)
        {
            _httpClient = httpClient;
            _config = config;
        }

        public void GetAffiliationFromPersonNumber(string pnum, /*out*/ SierraModel sm)
        {
            sm.aff = "N/A";

            try
            {
                var url = _config.LibPSearchUrl + "/api-search?apikey=" + _config.LibPSearchApiKey + "&pnr=" + pnum;
                var task = _httpClient.GetStringAsync(url);
                task.Wait();
                var userData = JsonConvert.DeserializeObject<dynamic>(task.Result);

                if (userData.errorStr == null)
                {
                    IEnumerable<dynamic> activeCategories = userData.activeCategories;
                    bool isStudent = false;
                    bool isEmployee = false;
                    if (activeCategories != null)
                    {
                        isStudent = activeCategories.Any(cs => (cs as IEnumerable<dynamic>).Any(c => c.name == "student"));
                        isEmployee = activeCategories.Any(cs => (cs as IEnumerable<dynamic>).Any(c => c.name == "employee"));
                    }

                    if (isEmployee)
                    {
                        sm.aff = "Anställd";
                    }
                    else if (isStudent)
                    {
                        sm.aff = "Student";
                    }
                    else
                    {
                        sm.aff = "Ingen tillhörighet";
                    }

                    sm.cid = userData.cid + " (" + userData.fname + " " + userData.lname + ")";
                    sm.e_resource_access = (bool)(userData.eResourceAccess as JValue);
                }
                else if (userData.errorStr == "no_persons_found")
                {
                    sm.aff = "Hittade ej i libpsearch";
                }
                else
                {
                    sm.aff = "Error i libpsearch";
                }
            } 
            catch (Exception)
            {
                sm.aff = "Error";
            }
        }
    }
}