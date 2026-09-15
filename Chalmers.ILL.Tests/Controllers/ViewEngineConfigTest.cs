using System.Linq;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chalmers.ILL.Tests.Controllers
{
    // Umbraco's own view engine registration used to add "~/Views/Partials/{0}.cshtml" as a
    // search location. All order-action partials (Chalmers.ILL.Action.*, DeliveryType/*,
    // Settings/*, Chalmers.ILL.OrderItem, Chalmers.ILL.LogItem — ~20 files) live under
    // Views/Partials/ and are referenced by bare name via PartialView("..."), relying on that
    // location. It silently disappeared when Umbraco's view engine registration was removed,
    // so every PartialView(...) call targeting one of those files failed at runtime with "the
    // partial view '...' was not found" — invisible to controller-level unit tests, since they
    // call the action method directly without going through the view engine at all.
    [TestClass]
    public class ViewEngineConfigTest
    {
        [TestMethod]
        public void RegisterViewEngines_AddsViewsPartialsToViewLocationFormats()
        {
            var options = new RazorViewEngineOptions();

            ViewEngineConfig.RegisterViewEngines(options);

            Assert.IsTrue(options.ViewLocationFormats.Contains("/Views/Partials/{0}.cshtml"));
        }

        [TestMethod]
        public void RegisterViewEngines_PreservesExistingLocationFormats()
        {
            // A bare `new RazorViewEngineOptions()` has an empty ViewLocationFormats - the
            // built-in defaults (e.g. "/Views/Shared/{0}.cshtml") are applied by MVC's own
            // IConfigureOptions<RazorViewEngineOptions>, wired up by AddControllersWithViews().
            var services = new ServiceCollection();
            services.AddControllersWithViews();
            var options = services.BuildServiceProvider().GetRequiredService<IOptions<RazorViewEngineOptions>>().Value;
            var originalCount = options.ViewLocationFormats.Count;

            ViewEngineConfig.RegisterViewEngines(options);

            Assert.AreEqual(originalCount + 1, options.ViewLocationFormats.Count);
            Assert.IsTrue(options.ViewLocationFormats.Contains("/Views/Shared/{0}.cshtml"));
        }
    }
}
