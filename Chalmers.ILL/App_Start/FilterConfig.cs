using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Authorization;

namespace Chalmers.ILL
{
    public class FilterConfig
    {
        // Umbraco's "Public Access" node protection used to gate every page behind login;
        // that protection lived in the CMS content tree, not in code, so it silently vanished
        // when Umbraco was removed. This filter replaces it: everything requires a logged-in
        // session unless the controller/action opts out with [AllowAnonymous] (the login page,
        // the login POST handler, the QR-code branch-receipt endpoint, and the two
        // machine-to-machine endpoints that must stay public - see fas 0a).
        public static void RegisterGlobalFilters(MvcOptions options)
        {
            options.Filters.Add(new AuthorizeFilter());
        }
    }
}
