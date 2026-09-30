using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Chalmers.ILL.Controllers.SurfaceControllers;
using Chalmers.ILL.Members;
using Chalmers.ILL.Models.Page;
using Chalmers.ILL.Tests.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chalmers.ILL.Tests.Controllers
{
    // LoginSurfaceController.HandleLogin is the most security-critical method in the application
    // and had zero tests before this file (see TODO-remove-dotnet-framework.md, fas 3). Written
    // before the ASP.NET Core rewrite so the redirect behavior it protects survives unchanged;
    // updated again for the fas 1b-5 sweep since HandleLogin became async and now returns
    // IActionResult (a RedirectResult) instead of calling Response.Redirect directly.
    [TestClass]
    public class LoginSurfaceControllerTest
    {
        [TestMethod]
        public async Task HandleLogin_ValidCredentials_DeskRole_SetsAuthCookieAndRedirectsToDisk()
        {
            string signedInAs = null;
            var controller = NewController((u, p) => true, u => new[] { "Desk" }, (ctx, login, roles) => { signedInAs = login; return Task.CompletedTask; });

            var result = await controller.HandleLogin(new Models.LoginModel { Login = "deskuser", Password = "correct" }) as RedirectResult;

            Assert.AreEqual("deskuser", signedInAs);
            Assert.IsNotNull(result);
            Assert.AreEqual("/disk/?login=ok", result.Url);
        }

        [TestMethod]
        public async Task HandleLogin_ValidCredentials_NonDeskRole_RedirectsToOrderListPage()
        {
            var controller = NewController((u, p) => true, u => Array.Empty<string>(), (ctx, login, roles) => Task.CompletedTask);

            var result = await controller.HandleLogin(new Models.LoginModel { Login = "otheruser", Password = "correct" }) as RedirectResult;

            // OrderListPageUrl isn't set on the stub config, so it resolves to empty - this pins
            // today's behavior (no crash, no fallback URL), not a desired value.
            Assert.IsNotNull(result);
            Assert.AreEqual("?login=ok", result.Url);
        }

        [TestMethod]
        public async Task HandleLogin_InvalidCredentials_RedirectsWithInvalidMemberError()
        {
            bool signedIn = false;
            var controller = NewController((u, p) => false, u => Array.Empty<string>(), (ctx, login, roles) => { signedIn = true; return Task.CompletedTask; });

            var result = await controller.HandleLogin(new Models.LoginModel { Login = "baduser", Password = "wrong" }) as RedirectResult;

            Assert.IsFalse(signedIn);
            Assert.IsNotNull(result);
            Assert.AreEqual("/ChalmersILLLoginPage?error=invalid-member", result.Url);
        }

        [TestMethod]
        public async Task HandleLogin_InvalidModel_RedirectsWithInvalidModelError()
        {
            var controller = NewController((u, p) => true, u => Array.Empty<string>(), (ctx, login, roles) => Task.CompletedTask);
            controller.ModelState.AddModelError("Login", "Required");

            var result = await controller.HandleLogin(new Models.LoginModel { Login = "", Password = "" }) as RedirectResult;

            Assert.IsNotNull(result);
            Assert.AreEqual("/ChalmersILLLoginPage?error=invalid-model", result.Url);
        }

        [TestMethod]
        public async Task HandleLogin_ValidCredentials_AssignsNonZeroMemberId()
        {
            // Regression coverage for a real bug found 2026-09-30 via manual two-user testing:
            // AddMemberToCache was always called with a literal 0, which every lock comparison
            // in OrderItemSurfaceController/ChalmersILLOrderListPage.cshtml treats as "no member",
            // so a locked order never showed as locked to anyone. The old StubMemberInfoManager
            // below discarded the memberId argument entirely, so this went uncaught here even
            // though the controller was fully unit tested.
            int? capturedMemberId = null;
            var memberInfoManager = new CapturingMemberInfoManager(id => capturedMemberId = id);
            var controller = new LoginSurfaceController(memberInfoManager, (u, p) => true, u => new[] { "Desk" }, (ctx, login, roles) => Task.CompletedTask, new StubChillinConfiguration());
            controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

            await controller.HandleLogin(new Models.LoginModel { Login = "someuser", Password = "correct" });

            Assert.IsNotNull(capturedMemberId);
            Assert.AreNotEqual(0, capturedMemberId.Value);
        }

        [TestMethod]
        public async Task HandleLogin_DifferentLogins_GetDifferentMemberIds()
        {
            int? idForAlice = null, idForBob = null;

            var aliceController = new LoginSurfaceController(new CapturingMemberInfoManager(id => idForAlice = id), (u, p) => true, u => Array.Empty<string>(), (ctx, login, roles) => Task.CompletedTask, new StubChillinConfiguration());
            aliceController.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
            await aliceController.HandleLogin(new Models.LoginModel { Login = "alice", Password = "correct" });

            var bobController = new LoginSurfaceController(new CapturingMemberInfoManager(id => idForBob = id), (u, p) => true, u => Array.Empty<string>(), (ctx, login, roles) => Task.CompletedTask, new StubChillinConfiguration());
            bobController.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
            await bobController.HandleLogin(new Models.LoginModel { Login = "bob", Password = "correct" });

            Assert.IsNotNull(idForAlice);
            Assert.IsNotNull(idForBob);
            Assert.AreNotEqual(idForAlice.Value, idForBob.Value);
        }

        [TestMethod]
        public async Task HandleLogin_SameLoginTwice_GetsSameMemberIdBothTimes()
        {
            int? firstId = null, secondId = null;

            var controller1 = new LoginSurfaceController(new CapturingMemberInfoManager(id => firstId = id), (u, p) => true, u => Array.Empty<string>(), (ctx, login, roles) => Task.CompletedTask, new StubChillinConfiguration());
            controller1.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
            await controller1.HandleLogin(new Models.LoginModel { Login = "sameuser", Password = "correct" });

            var controller2 = new LoginSurfaceController(new CapturingMemberInfoManager(id => secondId = id), (u, p) => true, u => Array.Empty<string>(), (ctx, login, roles) => Task.CompletedTask, new StubChillinConfiguration());
            controller2.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
            await controller2.HandleLogin(new Models.LoginModel { Login = "sameuser", Password = "correct" });

            Assert.IsNotNull(firstId);
            Assert.AreEqual(firstId.Value, secondId.Value);
        }

        [TestMethod]
        public void HandleLogin_HasValidateAntiForgeryTokenAttribute()
        {
            // Login had no CSRF protection at all (see TODO-remove-dotnet-framework.md, fas 3).
            var method = typeof(LoginSurfaceController).GetMethod(nameof(LoginSurfaceController.HandleLogin));

            Assert.IsTrue(method.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), true).Any());
        }

        private static LoginSurfaceController NewController(Func<string, string, bool> validateUser, Func<string, IEnumerable<string>> getRolesForUser, Func<HttpContext, string, IEnumerable<string>, Task> signIn)
        {
            var controller = new LoginSurfaceController(new StubMemberInfoManager(), validateUser, getRolesForUser, signIn, new StubChillinConfiguration());
            var httpContext = new DefaultHttpContext();
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
            return controller;
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

        // Unlike StubMemberInfoManager above, records the memberId HandleLogin actually passes
        // to AddMemberToCache, since that's exactly the value the real bug got wrong.
        class CapturingMemberInfoManager : IMemberInfoManager
        {
            private readonly Action<int> _onAddMemberToCache;

            public CapturingMemberInfoManager(Action<int> onAddMemberToCache)
            {
                _onAddMemberToCache = onAddMemberToCache;
            }

            public int GetCurrentMemberId(HttpRequest request, HttpResponse response) => 1;
            public string GetCurrentMemberText(HttpRequest request, HttpResponse response) => "Test User";
            public string GetCurrentMemberLoginName(HttpRequest request, HttpResponse response) => "testuser";
            public void PopulateModelWithMemberData(HttpRequest request, HttpResponse response, ChalmersILLModel model) { }
            public void AddMemberToCache(HttpResponse response, int memberId, string memberText, string memberLoginName) => _onAddMemberToCache(memberId);
            public void ClearMemberCache(HttpResponse response) { }
        }
    }
}
