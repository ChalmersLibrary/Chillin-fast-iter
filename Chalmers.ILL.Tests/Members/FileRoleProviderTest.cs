using System.Collections.Generic;
using Chalmers.ILL.Members;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chalmers.ILL.Tests.Members
{
    [TestClass]
    public class FileRoleProviderTest
    {
        [TestMethod]
        public void IsUserInRole_UserHasRole_ReturnsTrue()
        {
            var provider = MakeProvider(Account("alice", "Desk", "Administrator"));

            Assert.IsTrue(provider.IsUserInRole("alice", "Desk"));
        }

        [TestMethod]
        public void IsUserInRole_UserLacksRole_ReturnsFalse()
        {
            var provider = MakeProvider(Account("alice", "Desk"));

            Assert.IsFalse(provider.IsUserInRole("alice", "Administrator"));
        }

        [TestMethod]
        public void IsUserInRole_UnknownUser_ReturnsFalse()
        {
            var provider = MakeProvider(Account("alice", "Desk"));

            Assert.IsFalse(provider.IsUserInRole("bob", "Desk"));
        }

        [TestMethod]
        public void IsUserInRole_RoleNameIsCaseInsensitive()
        {
            var provider = MakeProvider(Account("alice", "Desk"));

            Assert.IsTrue(provider.IsUserInRole("alice", "desk"));
        }

        [TestMethod]
        public void GetRolesForUser_ReturnsAssignedRoles()
        {
            var provider = MakeProvider(Account("alice", "Desk", "Administrator"));

            CollectionAssert.AreEquivalent(new[] { "Desk", "Administrator" }, provider.GetRolesForUser("alice"));
        }

        [TestMethod]
        public void GetRolesForUser_UnknownUser_ReturnsEmpty()
        {
            var provider = MakeProvider(Account("alice", "Desk"));

            Assert.AreEqual(0, provider.GetRolesForUser("bob").Length);
        }

        [TestMethod]
        public void IsUserInRole_AccountHasNullRoles_ReturnsFalseWithoutThrowing()
        {
            var provider = MakeProvider(new MemberAccount { Login = "alice", PasswordHash = "irrelevant", Roles = null });

            Assert.IsFalse(provider.IsUserInRole("alice", "Desk"));
        }

        [TestMethod]
        public void GetRolesForUser_AccountHasNullRoles_ReturnsEmptyWithoutThrowing()
        {
            var provider = MakeProvider(new MemberAccount { Login = "alice", PasswordHash = "irrelevant", Roles = null });

            Assert.AreEqual(0, provider.GetRolesForUser("alice").Length);
        }

        [TestMethod]
        public void GetAllRoles_OneAccountHasNullRoles_IgnoresItWithoutThrowing()
        {
            var provider = MakeProvider(
                new MemberAccount { Login = "alice", PasswordHash = "irrelevant", Roles = null },
                Account("bob", "Desk"));

            CollectionAssert.AreEquivalent(new[] { "Desk" }, provider.GetAllRoles());
        }

        private static MemberAccount Account(string login, params string[] roles) =>
            new MemberAccount { Login = login, PasswordHash = "irrelevant", Roles = new List<string>(roles) };

        private static FileRoleProvider MakeProvider(params MemberAccount[] accounts) =>
            new FileRoleProvider(() => new List<MemberAccount>(accounts));
    }
}
