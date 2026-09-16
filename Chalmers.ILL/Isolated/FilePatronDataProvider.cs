using System.Collections.Generic;
using System.Linq;
using Chalmers.ILL.Models;
using Chalmers.ILL.Patron;

namespace Chalmers.ILL.Isolated
{
    // Fake IPatronDataProvider/IAffiliationDataProvider/IPersonDataProvider for isolated mode (fas 6,
    // isolerat läge steg A) - one class implementing all three so they share the same small,
    // deliberately awkward table (unclassified/blocked/inactive patrons) instead of three
    // independent fakes that could drift. Only constructed personnummer (obviously fake dates/
    // suffixes) - never real patron data. Unknown keys are a genuine miss, not a fallback record, so
    // the "not found" path is exercised too.
    public class FilePatronDataProvider : IPatronDataProvider, IAffiliationDataProvider, IPersonDataProvider
    {
        private static readonly List<SierraModel> Patrons = new List<SierraModel>
        {
            new SierraModel
            {
                id = "isolated-1", barcode = "1111111111", pnum = "19000101-0001",
                first_name = "Test", last_name = "Testsson", email = "test.testsson@isolated.invalid",
                ptype = 20, aff = "Student", active = true, mblock = "", home_library = "hbib"
            },
            new SierraModel
            {
                id = "isolated-2", barcode = "2222222222", pnum = "19000101-0002",
                first_name = "Anställd", last_name = "Andersson", email = "anstalld.andersson@isolated.invalid",
                ptype = 10, aff = "Anställd", active = true, mblock = "", home_library = "lbib"
            },
            new SierraModel
            {
                id = "isolated-3", barcode = "3333333333", pnum = "19000101-0003",
                first_name = "Spärrad", last_name = "Svensson", email = "sparrad.svensson@isolated.invalid",
                ptype = 20, aff = "Student", active = true, mblock = "requests (manual)", home_library = "hbib"
            },
            new SierraModel
            {
                id = "isolated-4", barcode = "4444444444", pnum = "19000101-0004",
                first_name = "Inaktiv", last_name = "Inaktivsson", email = "inaktiv@isolated.invalid",
                ptype = 50, aff = "Ingen tillhörighet", active = false, mblock = "", home_library = "abib"
            }
        };

        public IList<SierraModel> GetPatrons(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return new List<SierraModel>();

            return Patrons.Where(p =>
                Matches(p.email, query) || Matches(p.barcode, query) ||
                Matches(p.pnum, query) || Matches(p.first_name, query) || Matches(p.last_name, query))
                .ToList();
        }

        public SierraModel GetPatronInfoFromLibraryCardNumber(string barcode) =>
            Patrons.FirstOrDefault(p => p.barcode == barcode) ?? new SierraModel();

        public SierraModel GetPatronInfoFromLibraryCardNumberOrPersonnummer(string barcode, string pnr)
        {
            var testPnr = pnr != null && pnr.Length == 12 ? pnr.Remove(0, 2) : pnr;
            return Patrons.FirstOrDefault(p => p.barcode == barcode || p.pnum == testPnr) ?? new SierraModel();
        }

        public SierraModel GetPatronInfoFromSierraId(string sierraId) =>
            Patrons.FirstOrDefault(p => p.id == sierraId) ?? new SierraModel();

        public void GetAffiliationFromPersonNumber(string pnum, SierraModel sm)
        {
            var match = Patrons.FirstOrDefault(p => p.pnum == pnum);
            sm.aff = match?.aff ?? "Hittade ej i libpsearch";
            if (match != null)
                sm.cid = match.pnum + " (" + match.first_name + " " + match.last_name + ")";
        }

        public SierraModel GetPatronInfoFromLibraryCidPersonnummerOrEmail(string cidOrPersonnummer, string email)
        {
            var match = Patrons.FirstOrDefault(p => p.pnum == cidOrPersonnummer || p.email == email);
            if (match == null)
                return new SierraModel();

            return new SierraModel
            {
                first_name = match.first_name,
                last_name = match.last_name,
                pnum = match.pnum,
                email = match.email,
                cid = match.pnum + " (" + match.first_name + " " + match.last_name + ")",
                aff = match.aff,
                e_resource_access = true
            };
        }

        private static bool Matches(string field, string query) =>
            field != null && field.Contains(query, System.StringComparison.OrdinalIgnoreCase);
    }
}
