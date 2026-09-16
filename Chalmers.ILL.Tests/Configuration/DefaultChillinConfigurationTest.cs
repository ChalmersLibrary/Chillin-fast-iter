using System.Collections.Generic;
using Chalmers.ILL.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chalmers.ILL.Tests.Configuration
{
    // DefaultChillinConfiguration used to read System.Configuration.ConfigurationManager.AppSettings
    // directly - a mechanism Kestrel never reads and which also resolved against the wrong assembly
    // under `dotnet test` (see the old comment in ChalmersILLOrderListPageController.cs). Fas 6
    // replaced it with Microsoft.Extensions.Configuration under a "Chillin" section. This pins that
    // every property reads its own "Chillin:<PropertyName>" key, string and bool alike, and that a
    // missing key comes back null/false rather than throwing - the same "AppSettings[missing] ==
    // null" behavior the old ConfigurationManager-backed implementation had.
    [TestClass]
    public class DefaultChillinConfigurationTest
    {
        [TestMethod]
        public void Properties_ReadFromChillinSection_ByPropertyName()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    ["Chillin:BaseUrl"] = "http://example.test/",
                    ["Chillin:UseMicrosoftGraphMailService"] = "true",
                    ["Chillin:FolioApiBaseAddress"] = "https://folio.example.test",
                    ["Chillin:ChalmersIllArchiveProcessedMails"] = "false",
                })
                .Build();

            var sut = new DefaultChillinConfiguration(configuration);

            Assert.AreEqual("http://example.test/", sut.BaseUrl);
            Assert.IsTrue(sut.UseMicrosoftGraphMailService);
            Assert.AreEqual("https://folio.example.test", sut.FolioApiBaseAddress);
            Assert.IsFalse(sut.ChalmersIllArchiveProcessedMails);
        }

        [TestMethod]
        public void Properties_MissingKey_ReturnsNullOrFalse()
        {
            var configuration = new ConfigurationBuilder().Build();

            var sut = new DefaultChillinConfiguration(configuration);

            Assert.IsNull(sut.BaseUrl);
            Assert.IsNull(sut.FolioApiBaseAddress);
            Assert.IsFalse(sut.UseMicrosoftGraphMailService);
            Assert.IsFalse(sut.CheckForPendingDatabaseMigrations);
        }
    }
}
