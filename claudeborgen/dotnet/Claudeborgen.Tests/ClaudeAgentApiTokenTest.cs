using System;
using System.Security.Cryptography;
using System.Text;
using Claudeborgen;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Claudeborgen.Tests
{
    [TestClass]
    public class ClaudeAgentApiTokenTest
    {
        private static readonly string TokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("hemlig-token")));

        [TestMethod]
        public void CorrectBearerToken_IsAccepted() =>
            Assert.IsTrue(ClaudeAgentApiController.IsValidToken("Bearer hemlig-token", TokenHash));

        [TestMethod]
        public void LowercaseHashInConfiguration_IsAccepted() =>
            Assert.IsTrue(ClaudeAgentApiController.IsValidToken("Bearer hemlig-token", TokenHash.ToLowerInvariant()));

        [TestMethod]
        public void WrongToken_IsRejected() =>
            Assert.IsFalse(ClaudeAgentApiController.IsValidToken("Bearer fel", TokenHash));

        [TestMethod]
        public void MissingOrMalformedHeader_IsRejected()
        {
            Assert.IsFalse(ClaudeAgentApiController.IsValidToken("", TokenHash));
            Assert.IsFalse(ClaudeAgentApiController.IsValidToken(null, TokenHash));
            Assert.IsFalse(ClaudeAgentApiController.IsValidToken("hemlig-token", TokenHash));
            Assert.IsFalse(ClaudeAgentApiController.IsValidToken("Bearer ", TokenHash));
        }

        [TestMethod]
        public void NoHashConfigured_RejectsEverything()
        {
            Assert.IsFalse(ClaudeAgentApiController.IsValidToken("Bearer hemlig-token", null));
            Assert.IsFalse(ClaudeAgentApiController.IsValidToken("Bearer hemlig-token", "inte-hex"));
        }
    }
}
