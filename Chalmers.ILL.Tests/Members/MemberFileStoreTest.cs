using System.Collections.Generic;
using System.IO;
using Chalmers.ILL.Members;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chalmers.ILL.Tests.Members
{
    [TestClass]
    public class MemberFileStoreTest
    {
        string _path;

        [TestInitialize]
        public void Setup()
        {
            _path = Path.Combine(Path.GetTempPath(), "members-" + System.Guid.NewGuid().ToString("N") + ".json");
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (File.Exists(_path)) File.Delete(_path);
        }

        [TestMethod]
        public void Load_MissingFile_ReturnsEmptyList()
        {
            var accounts = MemberFileStore.Load(_path);

            Assert.AreEqual(0, accounts.Count);
        }

        [TestMethod]
        public void Load_MalformedFile_ReturnsEmptyList()
        {
            File.WriteAllText(_path, "not json");

            var accounts = MemberFileStore.Load(_path);

            Assert.AreEqual(0, accounts.Count);
        }

        [TestMethod]
        public void SaveThenLoad_RoundTripsAccountData()
        {
            var accounts = new List<MemberAccount>
            {
                new MemberAccount { Login = "alice", PasswordHash = "hash", Roles = new List<string> { "Desk", "Administrator" } }
            };

            MemberFileStore.Save(accounts, _path);
            var loaded = MemberFileStore.Load(_path);

            Assert.AreEqual(1, loaded.Count);
            Assert.AreEqual("alice", loaded[0].Login);
            Assert.AreEqual("hash", loaded[0].PasswordHash);
            CollectionAssert.AreEqual(new[] { "Desk", "Administrator" }, loaded[0].Roles);
        }

        [TestMethod]
        public void Save_CalledTwice_OverwritesExistingFileContent()
        {
            MemberFileStore.Save(new List<MemberAccount> { new MemberAccount { Login = "alice" } }, _path);
            MemberFileStore.Save(new List<MemberAccount> { new MemberAccount { Login = "bob" } }, _path);

            var loaded = MemberFileStore.Load(_path);

            Assert.AreEqual(1, loaded.Count);
            Assert.AreEqual("bob", loaded[0].Login);
        }

        [TestMethod]
        public void Save_DoesNotLeaveTempFilesBehindInTargetDirectory()
        {
            MemberFileStore.Save(new List<MemberAccount> { new MemberAccount { Login = "alice" } }, _path);
            MemberFileStore.Save(new List<MemberAccount> { new MemberAccount { Login = "bob" } }, _path);

            var directory = Path.GetDirectoryName(_path);
            var leftoverTempFiles = Directory.GetFiles(directory, Path.GetFileName(_path) + ".*.tmp");

            Assert.AreEqual(0, leftoverTempFiles.Length);
        }
    }
}
