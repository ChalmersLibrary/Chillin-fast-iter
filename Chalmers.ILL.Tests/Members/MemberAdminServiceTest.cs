using System.Collections.Generic;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Chalmers.ILL.Members;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chalmers.ILL.Tests.Members
{
    [TestClass]
    public class MemberAdminServiceTest
    {
        private static readonly PasswordHasher<MemberAccount> _hasher =
            new PasswordHasher<MemberAccount>(Options.Create(new PasswordHasherOptions
            {
                CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV2
            }));

        private static bool Verify(MemberAccount account, string password) =>
            _hasher.VerifyHashedPassword(account, account.PasswordHash, password) != PasswordVerificationResult.Failed;

        [TestMethod]
        public void CreateMember_NewLogin_AddsHashedAccount()
        {
            var saved = MakeService(out var service);

            service.CreateMember("alice", "s3cret", new List<string> { "Desk" });

            Assert.AreEqual(1, saved.Value.Count);
            Assert.AreEqual("alice", saved.Value[0].Login);
            Assert.IsTrue(Verify(saved.Value[0], "s3cret"));
            CollectionAssert.AreEqual(new[] { "Desk" }, saved.Value[0].Roles);
        }

        [TestMethod]
        [ExpectedException(typeof(System.InvalidOperationException))]
        public void CreateMember_DuplicateLogin_Throws()
        {
            var saved = MakeService(out var service, Account("alice", "old"));

            service.CreateMember("ALICE", "new", new List<string>());
        }

        [TestMethod]
        [ExpectedException(typeof(System.ArgumentException))]
        public void CreateMember_MissingPassword_Throws()
        {
            MakeService(out var service);

            service.CreateMember("alice", "", new List<string>());
        }

        [TestMethod]
        public void SetPassword_KnownUser_UpdatesHash()
        {
            var saved = MakeService(out var service, Account("alice", "old"));

            service.SetPassword("alice", "new-password");

            Assert.IsTrue(Verify(saved.Value[0], "new-password"));
        }

        [TestMethod]
        [ExpectedException(typeof(System.InvalidOperationException))]
        public void SetPassword_UnknownUser_Throws()
        {
            MakeService(out var service, Account("alice", "old"));

            service.SetPassword("bob", "new-password");
        }

        [TestMethod]
        public void SetRoles_KnownUser_ReplacesRoles()
        {
            var saved = MakeService(out var service, Account("alice", "old"));

            service.SetRoles("alice", new List<string> { "SuperAdmin" });

            CollectionAssert.AreEqual(new[] { "SuperAdmin" }, saved.Value[0].Roles);
        }

        [TestMethod]
        public void DeleteMember_KnownUser_RemovesAccount()
        {
            var saved = MakeService(out var service, Account("alice", "old"), Account("bob", "old"));

            service.DeleteMember("alice");

            Assert.AreEqual(1, saved.Value.Count);
            Assert.AreEqual("bob", saved.Value[0].Login);
        }

        [TestMethod]
        [ExpectedException(typeof(System.InvalidOperationException))]
        public void DeleteMember_UnknownUser_Throws()
        {
            MakeService(out var service, Account("alice", "old"));

            service.DeleteMember("bob");
        }

        private static MemberAccount Account(string login, string password)
        {
            var account = new MemberAccount { Login = login, Roles = new List<string>() };
            account.PasswordHash = _hasher.HashPassword(account, password);
            return account;
        }

        private static Holder<List<MemberAccount>> MakeService(out MemberAdminService service, params MemberAccount[] accounts)
        {
            var holder = new Holder<List<MemberAccount>>();
            service = new MemberAdminService(() => new List<MemberAccount>(accounts), a => holder.Value = a);
            return holder;
        }

        private class Holder<T>
        {
            public T Value;
        }
    }
}
