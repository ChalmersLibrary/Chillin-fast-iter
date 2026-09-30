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
        //
        // ViewerReadOnlyFilter adds a second, narrower restriction on top: a logged-in account
        // with no real role (FileRoleProvider's implicit "Viewer" default) may only reach actions
        // explicitly marked [AllowViewer] - see that file for the reasoning.
        //
        // RequiresOrderLockFilter adds a third, orthogonal restriction: an action explicitly
        // marked [RequiresOrderLock] is blocked if the order it targets is locked by a different
        // member - see that file for why this was needed.
        public static void RegisterGlobalFilters(MvcOptions options)
        {
            options.Filters.Add(new AuthorizeFilter());
            options.Filters.Add(new ViewerReadOnlyFilter());
            options.Filters.Add(new RequiresOrderLockFilter());
        }
    }
}
