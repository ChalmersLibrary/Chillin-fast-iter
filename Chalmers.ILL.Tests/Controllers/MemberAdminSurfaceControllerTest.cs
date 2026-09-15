using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using Chalmers.ILL.Controllers.SurfaceControllers;
using Chalmers.ILL.Members;
using Chalmers.ILL.Models;
using Chalmers.ILL.Models.PartialPage.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chalmers.ILL.Tests.Controllers
{
    [TestClass]
    public class MemberAdminSurfaceControllerTest
    {
        [TestMethod]
        public void RenderMemberAdminAction_ListsMembersSortedByLogin()
        {
            var controller = new MemberAdminSurfaceController(new StubMemberAdminService(
                new MemberAccount { Login = "bob", Roles = new List<string> { "Desk" } },
                new MemberAccount { Login = "alice", Roles = new List<string> { "SuperAdmin" } }));

            var result = controller.RenderMemberAdminAction() as PartialViewResult;
            var model = result?.Model as MemberAdmin;

            Assert.IsNotNull(model);
            Assert.AreEqual(2, model.Members.Count);
            Assert.AreEqual("alice", model.Members[0].Login);
            Assert.AreEqual("bob", model.Members[1].Login);
        }

        [TestMethod]
        public void CreateMember_Success_ReturnsSuccessJson()
        {
            var service = new StubMemberAdminService();
            var controller = new MemberAdminSurfaceController(service);

            var result = controller.CreateMember("alice", "password", "Desk, SuperAdmin") as JsonResult;
            var json = result?.Value as ResultResponse;

            Assert.IsTrue(json.Success);
            Assert.AreEqual("alice", service.LastCreatedLogin);
            CollectionAssert.AreEqual(new[] { "Desk", "SuperAdmin" }, service.LastCreatedRoles);
        }

        [TestMethod]
        public void CreateMember_ServiceThrows_ReturnsFailureJsonWithoutThrowing()
        {
            var controller = new MemberAdminSurfaceController(new StubMemberAdminService { ThrowOnCreate = true });

            var result = controller.CreateMember("alice", "password", "") as JsonResult;
            var json = result?.Value as ResultResponse;

            Assert.IsFalse(json.Success);
        }

        [TestMethod]
        public void SetMemberPassword_Success_ReturnsSuccessJson()
        {
            var service = new StubMemberAdminService();
            var controller = new MemberAdminSurfaceController(service);

            var result = controller.SetMemberPassword("alice", "new-password") as JsonResult;
            var json = result?.Value as ResultResponse;

            Assert.IsTrue(json.Success);
            Assert.AreEqual("alice", service.LastPasswordLogin);
        }

        [TestMethod]
        public void DeleteMember_Success_ReturnsSuccessJson()
        {
            var service = new StubMemberAdminService();
            var controller = new MemberAdminSurfaceController(service);

            var result = controller.DeleteMember("alice") as JsonResult;
            var json = result?.Value as ResultResponse;

            Assert.IsTrue(json.Success);
            Assert.AreEqual("alice", service.LastDeletedLogin);
        }

        class StubMemberAdminService : IMemberAdminService
        {
            private readonly List<MemberAccount> _members;

            public bool ThrowOnCreate;
            public string LastCreatedLogin;
            public List<string> LastCreatedRoles;
            public string LastPasswordLogin;
            public string LastDeletedLogin;

            public StubMemberAdminService(params MemberAccount[] members)
            {
                _members = new List<MemberAccount>(members);
            }

            public List<MemberAccount> GetAllMembers() => _members;

            public void CreateMember(string login, string password, List<string> roles)
            {
                if (ThrowOnCreate) throw new System.InvalidOperationException("boom");
                LastCreatedLogin = login;
                LastCreatedRoles = roles;
            }

            public void SetPassword(string login, string newPassword) => LastPasswordLogin = login;

            public void SetRoles(string login, List<string> roles) { }

            public void DeleteMember(string login) => LastDeletedLogin = login;
        }
    }
}
