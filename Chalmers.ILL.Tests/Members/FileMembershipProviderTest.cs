using System.Collections.Generic;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Chalmers.ILL.Members;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chalmers.ILL.Tests.Members
{
    [TestClass]
    public class FileMembershipProviderTest
    {
        private static readonly PasswordHasher<MemberAccount> _hasher =
            new PasswordHasher<MemberAccount>(Options.Create(new PasswordHasherOptions
            {
                CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV2
            }));

        [TestMethod]
        public void ValidateUser_CorrectPassword_ReturnsTrue()
        {
            var provider = MakeProvider(new SaveCapture(), Account("alice", "correct-horse"));

            Assert.IsTrue(provider.ValidateUser("alice", "correct-horse"));
        }

        [TestMethod]
        public void ValidateUser_WrongPassword_ReturnsFalse()
        {
            var provider = MakeProvider(new SaveCapture(), Account("alice", "correct-horse"));

            Assert.IsFalse(provider.ValidateUser("alice", "wrong-password"));
        }

        [TestMethod]
        public void ValidateUser_UnknownUser_ReturnsFalse()
        {
            var provider = MakeProvider(new SaveCapture(), Account("alice", "correct-horse"));

            Assert.IsFalse(provider.ValidateUser("bob", "correct-horse"));
        }

        [TestMethod]
        public void ValidateUser_LoginIsCaseInsensitive()
        {
            var provider = MakeProvider(new SaveCapture(), Account("alice", "correct-horse"));

            Assert.IsTrue(provider.ValidateUser("ALICE", "correct-horse"));
        }

        [TestMethod]
        public void GetUser_KnownUser_ReturnsAccount()
        {
            var provider = MakeProvider(new SaveCapture(), Account("alice", "correct-horse"));

            var user = provider.GetUser("alice");

            Assert.IsNotNull(user);
            Assert.AreEqual("alice", user.Login);
        }

        [TestMethod]
        public void GetUser_UnknownUser_ReturnsNull()
        {
            var provider = MakeProvider(new SaveCapture(), Account("alice", "correct-horse"));

            Assert.IsNull(provider.GetUser("bob"));
        }

        [TestMethod]
        public void ChangePassword_CorrectOldPassword_SavesNewHashAndReturnsTrue()
        {
            var capture = new SaveCapture();
            var provider = MakeProvider(capture, Account("alice", "correct-horse"));

            var result = provider.ChangePassword("alice", "correct-horse", "new-password");

            Assert.IsTrue(result);
            Assert.IsNotNull(capture.Saved);
            Assert.AreNotEqual(PasswordVerificationResult.Failed, _hasher.VerifyHashedPassword(capture.Saved[0], capture.Saved[0].PasswordHash, "new-password"));
        }

        [TestMethod]
        public void ChangePassword_WrongOldPassword_ReturnsFalseAndDoesNotSave()
        {
            var capture = new SaveCapture();
            var provider = MakeProvider(capture, Account("alice", "correct-horse"));

            var result = provider.ChangePassword("alice", "wrong-password", "new-password");

            Assert.IsFalse(result);
            Assert.IsNull(capture.Saved);
        }

        private static MemberAccount Account(string login, string password)
        {
            var account = new MemberAccount { Login = login, Roles = new List<string>() };
            account.PasswordHash = _hasher.HashPassword(account, password);
            return account;
        }

        private static FileMembershipProvider MakeProvider(SaveCapture capture, params MemberAccount[] accounts)
        {
            return new FileMembershipProvider(
                () => new List<MemberAccount>(accounts),
                a => capture.Saved = a);
        }

        private class SaveCapture
        {
            public List<MemberAccount> Saved;
        }
    }
}
