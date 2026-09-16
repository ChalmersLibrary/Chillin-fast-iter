using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Chalmers.ILL.Controllers.SurfaceControllers;
using Chalmers.ILL.Controllers.SurfaceControllers.Page;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chalmers.ILL.Tests.Controllers
{
    // Umbraco's "Public Access" node protection used to gate every page behind login; that
    // protection lived in the CMS content tree, not in code, so it silently vanished when
    // Umbraco was removed (only noticed once ChalmersILLController stopped crashing and the
    // unauthenticated homepage became visible). FilterConfig replaces it with a global
    // AuthorizeAttribute. These tests guard the wiring: the filter is registered, and exactly
    // the controllers that must stay public are marked [AllowAnonymous].
    [TestClass]
    public class AuthorizationTest
    {
        [TestMethod]
        public void RegisterGlobalFilters_AddsAuthorizeFilter()
        {
            var options = new MvcOptions();

            FilterConfig.RegisterGlobalFilters(options);

            Assert.IsTrue(options.Filters.OfType<AuthorizeFilter>().Any());
        }

        [TestMethod]
        public void LoginPageController_IsAllowAnonymous()
        {
            Assert.IsTrue(IsAllowAnonymous(typeof(ChalmersILLLoginPageController)));
        }

        [TestMethod]
        public void LoginSurfaceController_IsAllowAnonymous()
        {
            Assert.IsTrue(IsAllowAnonymous(typeof(LoginSurfaceController)));
        }

        [TestMethod]
        public void OrderItemReceivedAtBranchSurfaceController_IsAllowAnonymous()
        {
            // Physical delivery slips already printed with QR codes point at this URL and
            // can't be reprinted, so it must stay reachable without a login.
            Assert.IsTrue(IsAllowAnonymous(typeof(OrderItemReceivedAtBranchSurfaceController)));
        }

        [TestMethod]
        public void ChalmersILLController_RequiresLogin()
        {
            Assert.IsFalse(IsAllowAnonymous(typeof(ChalmersILLController)));
        }

        [TestMethod]
        public void ChalmersILLOrderListPageController_RequiresLogin()
        {
            Assert.IsFalse(IsAllowAnonymous(typeof(ChalmersILLOrderListPageController)));
        }

        [TestMethod]
        public void ChalmersILLSettingsPageController_RequiresLogin()
        {
            Assert.IsFalse(IsAllowAnonymous(typeof(ChalmersILLSettingsPageController)));
        }

        [TestMethod]
        public void ChalmersILLDiskPageController_RequiresLogin()
        {
            Assert.IsFalse(IsAllowAnonymous(typeof(ChalmersILLDiskPageController)));
        }

        [TestMethod]
        public void SystemSurfaceController_IsAllowAnonymous()
        {
            // Called by an external cron server with no user login; its own IP-based
            // IsRequestAuthorized() check runs after the global AuthorizeAttribute would
            // otherwise redirect the cron server to the login page.
            Assert.IsTrue(IsAllowAnonymous(typeof(SystemSurfaceController)));
        }

        [TestMethod]
        public void PublicDataSurfaceController_IsAllowAnonymous()
        {
            // Documented public API (see ILL-status-api.md) called cross-origin by the
            // library system with no user login.
            Assert.IsTrue(IsAllowAnonymous(typeof(PublicDataSurfaceController)));
        }

        [TestMethod]
        public void ChalmersILLLogoutPageController_IsAllowAnonymous()
        {
            // A user whose auth cookie already expired must still be able to reach this page
            // to clear the separate, unsigned ChalmersILL cookie (see MemberInfoManager).
            Assert.IsTrue(IsAllowAnonymous(typeof(ChalmersILLLogoutPageController)));
        }

        [TestMethod]
        public void MemberAdminSurfaceController_RequiresSuperAdminRole()
        {
            // The only role-based server-side authorization in the whole app - creating and
            // deleting member accounts must stay restricted to SuperAdmin, not just any logged-in user.
            var attribute = typeof(MemberAdminSurfaceController)
                .GetCustomAttributes(typeof(AuthorizeAttribute), true)
                .OfType<AuthorizeAttribute>()
                .SingleOrDefault();

            Assert.IsNotNull(attribute);
            Assert.AreEqual("SuperAdmin", attribute.Roles);
        }

        [TestMethod]
        public async Task MemberAdminSurfaceController_SuperAdminAttribute_ActuallyDeniesNonSuperAdminUsers()
        {
            // The reflection test above only checks that the attribute is present with the right
            // Roles string; it never runs ASP.NET Core's real authorization pipeline (see
            // TODO-remove-dotnet-framework.md, fas 3: "[Authorize(Roles = "SuperAdmin")] är otestat").
            // This drives the actual RolesAuthorizationRequirement handler.
            var provider = new ServiceCollection().AddAuthorization().AddLogging().BuildServiceProvider();
            var policyProvider = provider.GetRequiredService<IAuthorizationPolicyProvider>();
            var authService = provider.GetRequiredService<IAuthorizationService>();

            var attribute = typeof(MemberAdminSurfaceController)
                .GetCustomAttributes(typeof(AuthorizeAttribute), true)
                .OfType<AuthorizeAttribute>()
                .Single();
            var policy = await AuthorizationPolicy.CombineAsync(policyProvider, new[] { attribute });

            var deskUser = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "Desk") }, "TestAuth"));
            var superAdminUser = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "SuperAdmin") }, "TestAuth"));

            var deskResult = await authService.AuthorizeAsync(deskUser, policy);
            var superAdminResult = await authService.AuthorizeAsync(superAdminUser, policy);

            Assert.IsFalse(deskResult.Succeeded);
            Assert.IsTrue(superAdminResult.Succeeded);
        }

        private static bool IsAllowAnonymous(System.Type controllerType)
        {
            return controllerType.GetCustomAttributes(typeof(AllowAnonymousAttribute), true).Any();
        }
    }
}
