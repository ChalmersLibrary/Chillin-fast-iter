using System.Threading.Tasks;
using Chalmers.ILL;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chalmers.ILL.Tests.Controllers
{
    [TestClass]
    public class RoutingTest
    {
        [TestMethod]
        public async Task DefaultRoute_ControllerAction_MapsCorrectly()
        {
            var (controller, action) = await Resolve("/ChalmersILL/Index");
            Assert.AreEqual("ChalmersILL", controller);
            Assert.AreEqual("Index", action);
        }

        [TestMethod]
        public async Task DefaultRoute_RootUrl_MapsToChalmersILLController()
        {
            var (controller, action) = await Resolve("/");
            Assert.AreEqual("ChalmersILL", controller);
            Assert.AreEqual("Index", action);
        }

        [TestMethod]
        public async Task DefaultRoute_OrderListPage_MapsToOrderListController()
        {
            var (controller, _) = await Resolve("/ChalmersILLOrderListPage/Index");
            Assert.AreEqual("ChalmersILLOrderListPage", controller);
        }

        [TestMethod]
        public async Task LegacyUmbracoSurfaceAliasRoute_MapsToSameControllerAndAction()
        {
            var (controller, action) = await Resolve("/umbraco/surface/OrderItemReceivedAtBranchSurface/RenderResponse");
            Assert.AreEqual("OrderItemReceivedAtBranchSurface", controller);
            Assert.AreEqual("RenderResponse", action);
        }

        [TestMethod]
        public async Task LegacyBestaellningarSlugAlias_MapsToOrderListController()
        {
            var (controller, action) = await Resolve("/bestaellningar");
            Assert.AreEqual("ChalmersILLOrderListPage", controller);
            Assert.AreEqual("Index", action);
        }

        [TestMethod]
        public async Task LegacyBestaellningarSlugAlias_WithTrailingSlash_MapsToOrderListController()
        {
            var (controller, action) = await Resolve("/bestaellningar/");
            Assert.AreEqual("ChalmersILLOrderListPage", controller);
            Assert.AreEqual("Index", action);
        }

        [TestMethod]
        public async Task LegacyDiskSlugAlias_MapsToDiskPageController()
        {
            var (controller, action) = await Resolve("/disk");
            Assert.AreEqual("ChalmersILLDiskPage", controller);
            Assert.AreEqual("Index", action);
        }

        [TestMethod]
        public async Task LegacyDiskSlugAlias_WithTrailingSlash_MapsToDiskPageController()
        {
            var (controller, action) = await Resolve("/disk/");
            Assert.AreEqual("ChalmersILLDiskPage", controller);
            Assert.AreEqual("Index", action);
        }

        [TestMethod]
        public async Task LegacyBestaellningarInstaellningarSlugAlias_MapsToSettingsController()
        {
            var (controller, action) = await Resolve("/bestaellningar/instaellningar");
            Assert.AreEqual("ChalmersILLSettingsPage", controller);
            Assert.AreEqual("Index", action);
        }

        [TestMethod]
        public async Task LegacyBestaellningarInstaellningarSlugAlias_WithTrailingSlash_MapsToSettingsController()
        {
            var (controller, action) = await Resolve("/bestaellningar/instaellningar/");
            Assert.AreEqual("ChalmersILLSettingsPage", controller);
            Assert.AreEqual("Index", action);
        }

        [TestMethod]
        public async Task BestaellningarInstaellningar_IsNotSwallowedByOrderListWildcard()
        {
            // Regression test for the ordering dependency documented in RouteConfig: the
            // settings alias must be registered before the order-list alias, or the order
            // list's wildcard swallows "/bestaellningar/instaellningar" too.
            var (controller, _) = await Resolve("/bestaellningar/instaellningar");
            Assert.AreEqual("ChalmersILLSettingsPage", controller);
        }

        // Resolves a URL through the real RouteConfig.RegisterRoutes registration (endpoint
        // routing) and returns the matched controller/action - verifies routing behavior end to
        // end (an HTTP request in, route values out) rather than internal RouteData shape, per
        // CLAUDE.md's Testtäckningen. No controller is ever constructed or executed: a terminal
        // middleware reads the match right after the routing middleware selects it and
        // short-circuits, so this doesn't need any of the controllers' real dependencies wired up.
        private static async Task<(string controller, string action)> Resolve(string path)
        {
            var builder = WebApplication.CreateBuilder();
            // The test host's entry assembly isn't Chalmers.ILL.dll, so the default
            // ApplicationPartManager assembly discovery doesn't find its controllers - add it
            // explicitly, or every route matches zero endpoints ("No action descriptors found").
            builder.Services.AddControllersWithViews()
                .AddApplicationPart(typeof(Chalmers.ILL.Controllers.SurfaceControllers.LoginSurfaceController).Assembly);
            var app = builder.Build();

            app.UseRouting();
            RouteConfig.RegisterRoutes(app);
            app.Run(async context =>
            {
                var routeValues = context.Request.RouteValues;
                context.Items["controller"] = routeValues["controller"]?.ToString();
                context.Items["action"] = routeValues["action"]?.ToString();
                await Task.CompletedTask;
            });

            var requestDelegate = ((IApplicationBuilder)app).Build();

            var httpContext = new DefaultHttpContext
            {
                RequestServices = app.Services
            };
            // DefaultHttpContext leaves Method unset, which the HttpMethodMatcherPolicy treats as
            // not matching any [HttpGet]-constrained action (e.g. OrderItemReceivedAtBranchSurfaceController)
            // even though every route in this app is a plain GET in practice.
            httpContext.Request.Method = "GET";
            httpContext.Request.Path = path;

            await requestDelegate(httpContext);

            return ((string)httpContext.Items["controller"], (string)httpContext.Items["action"]);
        }
    }
}
