using System;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Chalmers.ILL.Controllers.SurfaceControllers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chalmers.ILL.Tests.Controllers
{
    [TestClass]
    public class PasswordSurfaceControllerTest
    {
        [TestMethod]
        public void RenderChangePasswordAction_ReturnsChangePasswordPartialView()
        {
            var controller = new PasswordSurfaceController((u, p) => true, (u, o, n) => true);

            var result = controller.RenderChangePasswordAction() as PartialViewResult;

            Assert.IsNotNull(result);
            Assert.AreEqual("Settings/ChangePassword", result.ViewName);
        }

        [TestMethod]
        public void ChangePassword_InvalidModel_RedirectsWithInvalidModelError()
        {
            var controller = new PasswordSurfaceController((u, p) => true, (u, o, n) => true);
            SetHttpContext(controller);
            controller.ModelState.AddModelError("NewPassword", "Required");

            var result = controller.ChangePassword(new Models.PartialPage.Settings.ChangePassword { CurrentPassword = "" }) as RedirectResult;

            Assert.AreEqual("/bestaellningar/instaellningar/?error=invalid-model", result.Url);
        }

        [TestMethod]
        public void ChangePassword_WrongCurrentPassword_RedirectsWithInvalidMemberError()
        {
            var controller = new PasswordSurfaceController((u, p) => false, (u, o, n) => true);
            SetHttpContext(controller);

            var result = controller.ChangePassword(new Models.PartialPage.Settings.ChangePassword { CurrentPassword = "wrong", NewPassword = "newpass" }) as RedirectResult;

            Assert.AreEqual("/bestaellningar/instaellningar/?error=invalid-member", result.Url);
        }

        [TestMethod]
        public void ChangePassword_ChangeSucceeds_RedirectsWithSuccess()
        {
            var controller = new PasswordSurfaceController((u, p) => true, (u, o, n) => true);
            SetHttpContext(controller);

            var result = controller.ChangePassword(new Models.PartialPage.Settings.ChangePassword { CurrentPassword = "current", NewPassword = "newpass" }) as RedirectResult;

            Assert.AreEqual("/bestaellningar/instaellningar/?success=true", result.Url);
        }

        [TestMethod]
        public void ChangePassword_ChangeReturnsFalse_RedirectsWithInvalidMemberErrorInsteadOfSuccess()
        {
            // Regression test: the underlying ChangePassword call can return false (e.g. new password
            // fails a policy check) without throwing. That return value used to be discarded and the
            // user was redirected to the success page regardless.
            var controller = new PasswordSurfaceController((u, p) => true, (u, o, n) => false);
            SetHttpContext(controller);

            var result = controller.ChangePassword(new Models.PartialPage.Settings.ChangePassword { CurrentPassword = "current", NewPassword = "newpass" }) as RedirectResult;

            Assert.AreEqual("/bestaellningar/instaellningar/?error=invalid-member", result.Url);
        }

        [TestMethod]
        public void ChangePassword_ChangeThrows_RedirectsWithInvalidMemberError()
        {
            var controller = new PasswordSurfaceController((u, p) => true, (u, o, n) => throw new InvalidOperationException("boom"));
            SetHttpContext(controller);

            var result = controller.ChangePassword(new Models.PartialPage.Settings.ChangePassword { CurrentPassword = "current", NewPassword = "newpass" }) as RedirectResult;

            Assert.AreEqual("/bestaellningar/instaellningar/?error=invalid-member", result.Url);
        }

        [TestMethod]
        public void ChangePassword_UsesAuthenticatedIdentityNotClientCookie_ForLoginName()
        {
            // Regression test for the fix: the login name whose password gets changed must come from
            // the signed auth identity, not from the client-writable ChalmersILL_memberLoginName
            // cookie. Simulate an attacker-controlled cookie disagreeing with the real identity and
            // assert the identity wins.
            string capturedLoginName = null;
            var controller = new PasswordSurfaceController(
                (u, p) => { capturedLoginName = u; return true; },
                (u, o, n) => true);
            SetHttpContext(controller);
            controller.ControllerContext.HttpContext.Request.Headers["Cookie"] = "ChalmersILL_memberLoginName=attacker";

            controller.ChangePassword(new Models.PartialPage.Settings.ChangePassword { CurrentPassword = "current", NewPassword = "newpass" });

            Assert.AreEqual("testuser", capturedLoginName);
        }

        [TestMethod]
        public void ChangePassword_HasValidateAntiForgeryTokenAttribute()
        {
            // Password change had no CSRF protection at all (see TODO-remove-dotnet-framework.md, fas 3).
            var method = typeof(PasswordSurfaceController).GetMethod(nameof(PasswordSurfaceController.ChangePassword));

            Assert.IsTrue(method.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), true).Any());
        }

        private static void SetHttpContext(Controller controller)
        {
            var httpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "testuser") }, "TestAuth"))
            };
            httpContext.Request.Path = "/settings";
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        }
    }
}
