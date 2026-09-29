using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Chalmers.ILL.Controllers.SurfaceControllers;
using Chalmers.ILL.Controllers.SurfaceControllers.Page;
using Chalmers.ILL.Members;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chalmers.ILL.Tests.Controllers
{
    // Two tiers below "has a real role" (see TODO-remove-dotnet-framework.md, the "Viewer" entry):
    // an account explicitly assigned only the "Viewer" role may reach actions marked
    // [AllowViewer]; an account with NO roles at all may reach nothing, [AllowViewer] included.
    // IsolatedModeSmokeTest has the real end-to-end HTTP version of this; these tests exercise
    // ViewerReadOnlyFilter's own decision logic directly, the same way AuthorizationTest drives
    // the SuperAdmin policy directly.
    [TestClass]
    public class ViewerReadOnlyFilterTest
    {
        [TestMethod]
        public void RegisterGlobalFilters_AddsViewerReadOnlyFilter()
        {
            var options = new MvcOptions();

            FilterConfig.RegisterGlobalFilters(options);

            Assert.IsTrue(options.Filters.OfType<ViewerReadOnlyFilter>().Any());
        }

        [TestMethod]
        public void ViewerOnlyUser_ActionWithoutAllowViewer_IsForbidden()
        {
            var context = MakeContext(RolesOf("Viewer"), hasAllowViewer: false, isAllowAnonymous: false);

            new ViewerReadOnlyFilter().OnAuthorization(context);

            Assert.IsInstanceOfType(context.Result, typeof(ForbidResult));
        }

        [TestMethod]
        public void ViewerOnlyUser_ActionWithAllowViewer_IsNotBlocked()
        {
            var context = MakeContext(RolesOf("Viewer"), hasAllowViewer: true, isAllowAnonymous: false);

            new ViewerReadOnlyFilter().OnAuthorization(context);

            Assert.IsNull(context.Result);
        }

        // An account with no roles assigned at all bakes zero role claims into the login cookie
        // (see LoginSurfaceController) - stricter than "Viewer": blocked even from actions
        // [AllowViewer] opens up for an explicit Viewer.
        [TestMethod]
        public void NoRoleClaimsAtAll_ActionWithoutAllowViewer_IsForbidden()
        {
            var context = MakeContext(RolesOf(), hasAllowViewer: false, isAllowAnonymous: false);

            new ViewerReadOnlyFilter().OnAuthorization(context);

            Assert.IsInstanceOfType(context.Result, typeof(ForbidResult));
        }

        [TestMethod]
        public void NoRoleClaimsAtAll_ActionWithAllowViewer_IsStillForbidden()
        {
            var context = MakeContext(RolesOf(), hasAllowViewer: true, isAllowAnonymous: false);

            new ViewerReadOnlyFilter().OnAuthorization(context);

            Assert.IsInstanceOfType(context.Result, typeof(ForbidResult));
        }

        [TestMethod]
        public void UserWithARealRole_ActionWithoutAllowViewer_IsNotBlocked()
        {
            var context = MakeContext(RolesOf("Desk"), hasAllowViewer: false, isAllowAnonymous: false);

            new ViewerReadOnlyFilter().OnAuthorization(context);

            Assert.IsNull(context.Result);
        }

        // A user who happens to carry "Viewer" alongside a real role is not restricted - only
        // "Viewer and nothing else" triggers the block.
        [TestMethod]
        public void UserWithViewerAndARealRole_IsNotBlocked()
        {
            var context = MakeContext(RolesOf("Viewer", "Desk"), hasAllowViewer: false, isAllowAnonymous: false);

            new ViewerReadOnlyFilter().OnAuthorization(context);

            Assert.IsNull(context.Result);
        }

        [TestMethod]
        public void UnauthenticatedUser_IsLeftToTheLoginRequiredFilter()
        {
            var context = MakeContext(principal: new ClaimsPrincipal(new ClaimsIdentity()), hasAllowViewer: false, isAllowAnonymous: false);

            new ViewerReadOnlyFilter().OnAuthorization(context);

            Assert.IsNull(context.Result);
        }

        [TestMethod]
        public void AllowAnonymousAction_ViewerOnlyUser_IsNotBlocked()
        {
            var context = MakeContext(RolesOf("Viewer"), hasAllowViewer: false, isAllowAnonymous: true);

            new ViewerReadOnlyFilter().OnAuthorization(context);

            Assert.IsNull(context.Result);
        }

        // Reflection guard for the sneakiest category found while designing this fix: a handful of
        // pre-existing actions mutate order data despite being exposed over [HttpGet]. A filter -
        // or a future edit - that infers "read" from the HTTP verb would wrongly open these up to
        // Viewer. None of them may ever carry [AllowViewer].
        [TestMethod]
        public void GetVerbMutations_NeverCarryAllowViewer()
        {
            AssertNoAllowViewer(typeof(OrderItemProviderSurfaceController), nameof(OrderItemProviderSurfaceController.SetProvider));
            AssertNoAllowViewer(typeof(OrderItemPatronDataSurfaceController), nameof(OrderItemPatronDataSurfaceController.FetchPatronDataUsingSierraId));
            AssertNoAllowViewer(typeof(OrderItemStatusSurfaceController), nameof(OrderItemStatusSurfaceController.SetOrderItemStatus));
            AssertNoAllowViewer(typeof(OrderItemTypeSurfaceController), nameof(OrderItemTypeSurfaceController.SetOrderItemType));
            AssertNoAllowViewer(typeof(OrderItemPurchaseLibrarySurfaceController), nameof(OrderItemPurchaseLibrarySurfaceController.SetOrderItemPurchaseLibrary));
            AssertNoAllowViewer(typeof(OrderItemDeliveryLibrarySurfaceController), nameof(OrderItemDeliveryLibrarySurfaceController.SetOrderItemDeliveryLibrary));
        }

        // A Viewer can view an order item, but must not be able to acquire or release its edit
        // lock - a read-only account has no business locking anything for editing.
        [TestMethod]
        public void OrderItemSurfaceController_LockActions_NeverCarryAllowViewer()
        {
            AssertNoAllowViewer(typeof(OrderItemSurfaceController), nameof(OrderItemSurfaceController.LockOrderItem));
            AssertNoAllowViewer(typeof(OrderItemSurfaceController), nameof(OrderItemSurfaceController.UnlockOrderItem));
            AssertNoAllowViewer(typeof(OrderItemSurfaceController), nameof(OrderItemSurfaceController.TakeOverLockedOrderItem));
        }

        // The controller behind the exact page the user reported ("jag kan se ordrar även om jag
        // helt saknar roles") must stay reachable for an explicit Viewer - read access is fine,
        // writes are not (a truly roleless account is blocked from it entirely, see
        // NoRoleClaimsAtAll_ActionWithAllowViewer_IsStillForbidden above).
        [TestMethod]
        public void ChalmersILLOrderListPageController_CarriesAllowViewer()
        {
            Assert.IsTrue(typeof(ChalmersILLOrderListPageController).GetCustomAttributes(typeof(AllowViewerAttribute), true).Any());
        }

        // MemberAdminSurfaceController is already fully blocked for Viewer by its own
        // [Authorize(Roles = "SuperAdmin")] (see AuthorizationTest) - it must not additionally
        // carry [AllowViewer] anywhere, which would be a contradictory/confusing thing to write.
        [TestMethod]
        public void MemberAdminSurfaceController_NeverCarriesAllowViewer()
        {
            Assert.IsFalse(typeof(MemberAdminSurfaceController).GetCustomAttributes(typeof(AllowViewerAttribute), true).Any());
            foreach (var method in typeof(MemberAdminSurfaceController).GetMethods().Where(m => typeof(ActionResult).IsAssignableFrom(m.ReturnType)))
            {
                Assert.IsFalse(method.GetCustomAttributes(typeof(AllowViewerAttribute), true).Any(), $"{method.Name} should not carry [AllowViewer].");
            }
        }

        private static void AssertNoAllowViewer(System.Type controllerType, string methodName)
        {
            var method = controllerType.GetMethod(methodName);
            Assert.IsNotNull(method, $"{controllerType.Name}.{methodName} not found - has it been renamed?");
            Assert.IsFalse(method.GetCustomAttributes(typeof(AllowViewerAttribute), true).Any(), $"{controllerType.Name}.{methodName} must not carry [AllowViewer].");
        }

        private static ClaimsPrincipal RolesOf(params string[] roles)
        {
            var claims = roles.Select(r => new Claim(ClaimTypes.Role, r)).Append(new Claim(ClaimTypes.Name, "test-user"));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        }

        private static AuthorizationFilterContext MakeContext(ClaimsPrincipal principal = null, bool hasAllowViewer = false, bool isAllowAnonymous = false)
        {
            var httpContext = new DefaultHttpContext { User = principal };

            // [AllowAnonymous] shows up as an AllowAnonymousAttribute in EndpointMetadata on this
            // ASP.NET Core version (confirmed by tracing a real request), NOT as an
            // IAllowAnonymousFilter in AuthorizationFilterContext.Filters - see the comment in
            // ViewerReadOnlyFilter itself. A version of this test that only populated `filters`
            // passed while the real HTTP pipeline still 403'd an [AllowAnonymous] page for a
            // roleless user - this must mirror EndpointMetadata to actually catch that.
            var metadata = new List<object>();
            if (hasAllowViewer) metadata.Add(new AllowViewerAttribute());
            if (isAllowAnonymous) metadata.Add(new AllowAnonymousAttribute());

            var descriptor = new ControllerActionDescriptor { EndpointMetadata = metadata };

            var actionContext = new ActionContext(httpContext, new RouteData(), descriptor);

            return new AuthorizationFilterContext(actionContext, new List<IFilterMetadata>());
        }
    }
}
