using System;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Chalmers.ILL.Controllers.SurfaceControllers;
using Chalmers.ILL.Members;
using Chalmers.ILL.Models.Page;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chalmers.ILL.Tests.Controllers
{
    [TestClass]
    public class PasswordSurfaceControllerTest
    {
        [TestMethod]
        public void RenderChangePasswordAction_ReturnsChangePasswordPartialView()
        {
            var controller = new PasswordSurfaceController(new StubMemberInfoManager(), (u, p) => true, (u, o, n) => true);

            var result = controller.RenderChangePasswordAction() as PartialViewResult;

            Assert.IsNotNull(result);
            Assert.AreEqual("Settings/ChangePassword", result.ViewName);
        }

        [TestMethod]
        public void ChangePassword_InvalidModel_RedirectsWithInvalidModelError()
        {
            var controller = new PasswordSurfaceController(new StubMemberInfoManager(), (u, p) => true, (u, o, n) => true);
            SetHttpContext(controller);
            controller.ModelState.AddModelError("NewPassword", "Required");

            var result = controller.ChangePassword(new Models.PartialPage.Settings.ChangePassword { CurrentPassword = "" }) as RedirectResult;

            Assert.AreEqual("/bestaellningar/instaellningar/?error=invalid-model", result.Url);
        }

        [TestMethod]
        public void ChangePassword_WrongCurrentPassword_RedirectsWithInvalidMemberError()
        {
            var controller = new PasswordSurfaceController(new StubMemberInfoManager(), (u, p) => false, (u, o, n) => true);
            SetHttpContext(controller);

            var result = controller.ChangePassword(new Models.PartialPage.Settings.ChangePassword { CurrentPassword = "wrong", NewPassword = "newpass" }) as RedirectResult;

            Assert.AreEqual("/bestaellningar/instaellningar/?error=invalid-member", result.Url);
        }

        [TestMethod]
        public void ChangePassword_ChangeSucceeds_RedirectsWithSuccess()
        {
            var controller = new PasswordSurfaceController(new StubMemberInfoManager(), (u, p) => true, (u, o, n) => true);
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
            var controller = new PasswordSurfaceController(new StubMemberInfoManager(), (u, p) => true, (u, o, n) => false);
            SetHttpContext(controller);

            var result = controller.ChangePassword(new Models.PartialPage.Settings.ChangePassword { CurrentPassword = "current", NewPassword = "newpass" }) as RedirectResult;

            Assert.AreEqual("/bestaellningar/instaellningar/?error=invalid-member", result.Url);
        }

        [TestMethod]
        public void ChangePassword_ChangeThrows_RedirectsWithInvalidMemberError()
        {
            var controller = new PasswordSurfaceController(new StubMemberInfoManager(), (u, p) => true, (u, o, n) => throw new InvalidOperationException("boom"));
            SetHttpContext(controller);

            var result = controller.ChangePassword(new Models.PartialPage.Settings.ChangePassword { CurrentPassword = "current", NewPassword = "newpass" }) as RedirectResult;

            Assert.AreEqual("/bestaellningar/instaellningar/?error=invalid-member", result.Url);
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

        class StubMemberInfoManager : IMemberInfoManager
        {
            public int GetCurrentMemberId(HttpRequest request, HttpResponse response) => 1;
            public string GetCurrentMemberText(HttpRequest request, HttpResponse response) => "Test User";
            public string GetCurrentMemberLoginName(HttpRequest request, HttpResponse response) => "testuser";
            public void PopulateModelWithMemberData(HttpRequest request, HttpResponse response, ChalmersILLModel model) { }
            public void AddMemberToCache(HttpResponse response, int memberId, string memberText, string memberLoginName) { }
            public void ClearMemberCache(HttpResponse response) { }
        }
    }
}
