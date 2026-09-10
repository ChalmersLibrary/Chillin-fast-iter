using System;
using System.Security.Principal;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;
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
            var controller = new PasswordSurfaceController(new StubMemberInfoManager());

            var result = controller.RenderChangePasswordAction() as PartialViewResult;

            Assert.IsNotNull(result);
            Assert.AreEqual("Settings/ChangePassword", result.ViewName);
        }

        [TestMethod]
        public void ChangePassword_InvalidModel_RedirectsWithInvalidModelError()
        {
            var controller = new PasswordSurfaceController(new StubMemberInfoManager());
            var fakeResponse = SetHttpContext(controller);
            controller.ModelState.AddModelError("NewPassword", "Required");

            controller.ChangePassword(new Models.PartialPage.Settings.ChangePassword { CurrentPassword = "" });

            Assert.AreEqual("/bestaellningar/instaellningar/?error=invalid-model", fakeResponse.RedirectLocation);
        }

        [TestMethod]
        public void ChangePassword_WrongCurrentPassword_RedirectsWithInvalidMemberError()
        {
            var controller = new PasswordSurfaceController(new StubMemberInfoManager(), (u, p) => false, (u, o, n) => true);
            var fakeResponse = SetHttpContext(controller);

            controller.ChangePassword(new Models.PartialPage.Settings.ChangePassword { CurrentPassword = "wrong", NewPassword = "newpass" });

            Assert.AreEqual("/bestaellningar/instaellningar/?error=invalid-member", fakeResponse.RedirectLocation);
        }

        [TestMethod]
        public void ChangePassword_ChangeSucceeds_RedirectsWithSuccess()
        {
            var controller = new PasswordSurfaceController(new StubMemberInfoManager(), (u, p) => true, (u, o, n) => true);
            var fakeResponse = SetHttpContext(controller);

            controller.ChangePassword(new Models.PartialPage.Settings.ChangePassword { CurrentPassword = "current", NewPassword = "newpass" });

            Assert.AreEqual("/bestaellningar/instaellningar/?success=true", fakeResponse.RedirectLocation);
        }

        [TestMethod]
        public void ChangePassword_ChangeReturnsFalse_RedirectsWithInvalidMemberErrorInsteadOfSuccess()
        {
            // Regression test: the underlying ChangePassword call can return false (e.g. new password
            // fails a policy check) without throwing. That return value used to be discarded and the
            // user was redirected to the success page regardless.
            var controller = new PasswordSurfaceController(new StubMemberInfoManager(), (u, p) => true, (u, o, n) => false);
            var fakeResponse = SetHttpContext(controller);

            controller.ChangePassword(new Models.PartialPage.Settings.ChangePassword { CurrentPassword = "current", NewPassword = "newpass" });

            Assert.AreEqual("/bestaellningar/instaellningar/?error=invalid-member", fakeResponse.RedirectLocation);
        }

        [TestMethod]
        public void ChangePassword_ChangeThrows_RedirectsWithInvalidMemberError()
        {
            var controller = new PasswordSurfaceController(new StubMemberInfoManager(), (u, p) => true, (u, o, n) => throw new InvalidOperationException("boom"));
            var fakeResponse = SetHttpContext(controller);

            controller.ChangePassword(new Models.PartialPage.Settings.ChangePassword { CurrentPassword = "current", NewPassword = "newpass" });

            Assert.AreEqual("/bestaellningar/instaellningar/?error=invalid-member", fakeResponse.RedirectLocation);
        }

        private static FakeHttpResponse SetHttpContext(Controller controller)
        {
            var response = new FakeHttpResponse();
            var context = new FakeHttpContext(response)
            {
                User = new GenericPrincipal(new GenericIdentity("testuser"), new string[0])
            };
            controller.ControllerContext = new ControllerContext(context, new RouteData(), controller);
            return response;
        }

        class FakeHttpContext : HttpContextBase
        {
            private readonly HttpResponseBase _response;
            private readonly HttpRequestBase _request = new FakeHttpRequest();

            public FakeHttpContext(HttpResponseBase response)
            {
                _response = response;
            }

            public override HttpRequestBase Request => _request;
            public override HttpResponseBase Response => _response;
            public override IPrincipal User { get; set; }
        }

        class FakeHttpRequest : HttpRequestBase
        {
            public override Uri Url => new Uri("http://localhost/settings");
        }

        class FakeHttpResponse : HttpResponseBase
        {
            public override string RedirectLocation { get; set; }

            public override void Redirect(string url)
            {
                RedirectLocation = url;
            }
        }

        class StubMemberInfoManager : IMemberInfoManager
        {
            public int GetCurrentMemberId(HttpRequestBase request, HttpResponseBase response) => 1;
            public string GetCurrentMemberText(HttpRequestBase request, HttpResponseBase response) => "Test User";
            public string GetCurrentMemberLoginName(HttpRequestBase request, HttpResponseBase response) => "testuser";
            public void PopulateModelWithMemberData(HttpRequestBase request, HttpResponseBase response, ChalmersILLModel model) { }
            public void AddMemberToCache(HttpResponseBase response, int memberId, string memberText, string memberLoginName) { }
            public void ClearMemberCache(HttpResponseBase response) { }
        }
    }
}
