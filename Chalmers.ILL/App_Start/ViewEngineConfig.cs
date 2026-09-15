using Microsoft.AspNetCore.Mvc.Razor;

namespace Chalmers.ILL
{
    public class ViewEngineConfig
    {
        // Umbraco's own view engine registration used to add "~/Views/Partials/{0}.cshtml" as a
        // search location (Umbraco itself follows that convention for macro partials). All ~20
        // order-action partials (Chalmers.ILL.Action.*, DeliveryType/*, Settings/*,
        // Chalmers.ILL.OrderItem, Chalmers.ILL.LogItem) live under Views/Partials/ and are
        // referenced by bare name via PartialView("..."), relying on that search location. It
        // silently disappeared when Umbraco's view engine registration was removed, so every one
        // of those PartialView(...) calls fails at runtime with "the partial view '...' was not
        // found" — not caught by unit tests, since they call controller actions directly without
        // going through the view engine.
        public static void RegisterViewEngines(RazorViewEngineOptions options)
        {
            // Unlike MVC5, Core's RazorViewEngine uses ViewLocationFormats for both View() and
            // PartialView() lookups - there is no separate PartialViewLocationFormats.
            options.ViewLocationFormats.Add("/Views/Partials/{0}.cshtml");
        }
    }
}
