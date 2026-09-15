using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Chalmers.ILL
{
    public class RouteConfig
    {
        public static void RegisterRoutes(IEndpointRouteBuilder endpoints)
        {
            // Backwards-compatibility alias for the old Umbraco surface-controller URL scheme
            // (/umbraco/surface/{Controller}/{Action}). Controller/action names are unchanged
            // by the Umbraco removal, so this maps straight onto the same routes as "Default".
            // Kept indefinitely: physical delivery slips already printed with QR codes pointing
            // at this URL (see OrderItemReceivedAtBranchSurfaceController) can't be reprinted.
            endpoints.MapControllerRoute(
                name: "LegacyUmbracoSurfaceAlias",
                pattern: "umbraco/surface/{controller}/{action}/{id?}"
            );

            // Backwards-compatibility aliases for the old Umbraco content-tree slugs that pointed
            // at the order list and settings pages before the Umbraco removal. The app's own links
            // use these slugs again (see ChalmersILL.cshtml et al.), so these routes are what make
            // them resolve; the wildcard segment absorbs a trailing slash or stray path/query noise.
            // Settings alias must be registered before the order-list alias, since the order-list
            // alias's wildcard would otherwise swallow "/bestaellningar/instaellningar" too.
            endpoints.MapControllerRoute(
                name: "LegacyBestaellningarInstaellningarSlugAlias",
                pattern: "bestaellningar/instaellningar/{*pathInfo}",
                defaults: new { controller = "ChalmersILLSettingsPage", action = "Index" }
            );

            endpoints.MapControllerRoute(
                name: "LegacyBestaellningarSlugAlias",
                pattern: "bestaellningar/{*pathInfo}",
                defaults: new { controller = "ChalmersILLOrderListPage", action = "Index" }
            );

            // Backwards-compatibility alias for the old Umbraco content-tree slug for the Desk
            // landing page. LoginSurfaceController redirects members with the "Desk" role here
            // after login; this route is what makes that URL resolve.
            endpoints.MapControllerRoute(
                name: "LegacyDiskSlugAlias",
                pattern: "disk/{*pathInfo}",
                defaults: new { controller = "ChalmersILLDiskPage", action = "Index" }
            );

            endpoints.MapControllerRoute(
                name: "Default",
                pattern: "{controller=ChalmersILL}/{action=Index}/{id?}"
            );
        }
    }
}
