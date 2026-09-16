using System;
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

            var sut = new DefaultChillinConfiguration(configuration, new StubWebHostEnvironment());

            Assert.AreEqual("http://example.test/", sut.BaseUrl);
            Assert.IsTrue(sut.UseMicrosoftGraphMailService);
            Assert.AreEqual("https://folio.example.test", sut.FolioApiBaseAddress);
            Assert.IsFalse(sut.ChalmersIllArchiveProcessedMails);
        }

        [TestMethod]
        public void Properties_MissingKey_ReturnsNullOrFalse()
        {
            var configuration = new ConfigurationBuilder().Build();

            var sut = new DefaultChillinConfiguration(configuration, new StubWebHostEnvironment());

            Assert.IsNull(sut.BaseUrl);
            Assert.IsNull(sut.FolioApiBaseAddress);
            Assert.IsFalse(sut.UseMicrosoftGraphMailService);
            Assert.IsFalse(sut.Isolated);
        }

        [TestMethod]
        public void DataPath_ExplicitKey_Wins()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    ["Chillin:DataPath"] = "/explicit/data/path",
                })
                .Build();

            var sut = new DefaultChillinConfiguration(configuration, new StubWebHostEnvironment());

            Assert.AreEqual("/explicit/data/path", sut.DataPath);
        }

        [TestMethod]
        public void DataPath_NoExplicitKeyButHomeSet_FallsBackToHomeSlashData()
        {
            var configuration = new ConfigurationBuilder().Build();
            var originalHome = Environment.GetEnvironmentVariable("HOME");
            try
            {
                Environment.SetEnvironmentVariable("HOME", "/home/testuser");

                var sut = new DefaultChillinConfiguration(configuration, new StubWebHostEnvironment());

                Assert.AreEqual(System.IO.Path.Combine("/home/testuser", "data"), sut.DataPath);
            }
            finally
            {
                Environment.SetEnvironmentVariable("HOME", originalHome);
            }
        }

        [TestMethod]
        public void DataPath_NoExplicitKeyAndNoHome_FallsBackToContentRootSibling()
        {
            var configuration = new ConfigurationBuilder().Build();
            var originalHome = Environment.GetEnvironmentVariable("HOME");
            try
            {
                Environment.SetEnvironmentVariable("HOME", null);

                var sut = new DefaultChillinConfiguration(configuration, new StubWebHostEnvironment
                {
                    ContentRootPath = "/app/Chalmers.ILL"
                });

                Assert.AreEqual("/app/chillin-data", sut.DataPath);
            }
            finally
            {
                Environment.SetEnvironmentVariable("HOME", originalHome);
            }
        }
    }
}
