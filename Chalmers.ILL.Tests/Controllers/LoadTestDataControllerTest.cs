using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Chalmers.ILL.Isolated;
using Chalmers.ILL.OrderItems;
using Chalmers.ILL.Tests.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace Chalmers.ILL.Tests.Controllers
{
    // The temporary load-test endpoints (claudeborgen only): generated orders land beside the
    // existing ones without touching them, get indexed, and Remove takes away exactly them.
    [TestClass]
    public class LoadTestDataControllerTest
    {
        private string _dataPath;
        private LuceneOrderItemSearcher _searcher;

        [TestInitialize]
        public void Setup()
        {
            _dataPath = Path.Combine(Path.GetTempPath(), "chillin-loadtest-test-" + Guid.NewGuid());
            var existing = Path.Combine(_dataPath, "orders", "000");
            Directory.CreateDirectory(existing);
            File.WriteAllText(Path.Combine(existing, "7.json"),
                Newtonsoft.Json.JsonConvert.SerializeObject(FakeOrders.Make(7, new Random(7))));
            _searcher = new LuceneOrderItemSearcher(_dataPath);
        }

        [TestCleanup]
        public void Cleanup()
        {
            _searcher.Dispose();
            Directory.Delete(_dataPath, recursive: true);
        }

        private LoadTestDataController Controller(bool isolated = true) =>
            new LoadTestDataController(new StubChillinConfiguration { Isolated = isolated, DataPath = _dataPath }, _searcher);

        private JObject WaitUntilDone()
        {
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < TimeSpan.FromSeconds(60))
            {
                var status = JObject.FromObject(((JsonResult)Controller().Status()).Value);
                if (!(bool)status["running"])
                    return status;
                Thread.Sleep(50);
            }
            Assert.Fail("Jobbet blev inte klart.");
            return null;
        }

        [TestMethod]
        public void Generate_WritesOrdersBesideTheExistingOnes_AndIndexesAll()
        {
            Controller().Generate(1500);
            var status = WaitUntilDone();

            Assert.AreEqual("klart", (string)status["phase"], (string)status["error"]);
            Assert.AreEqual(1500, (int)status["written"]);
            Assert.AreEqual(1501, (int)status["indexed"]);
            Assert.IsTrue(File.Exists(Path.Combine(_dataPath, "orders", "000", "7.json")));
            Assert.AreEqual(2, LoadTestDataController.GeneratedBuckets(_dataPath).Count()); // 10000, 10001
            Assert.AreEqual(1501, _searcher.Search("*").Count());
        }

        [TestMethod]
        public void Remove_DeletesOnlyTheGeneratedOrders()
        {
            Controller().Generate(20);
            WaitUntilDone();

            Controller().Remove();
            var status = WaitUntilDone();

            Assert.AreEqual("klart", (string)status["phase"], (string)status["error"]);
            Assert.AreEqual(1, (int)status["indexed"]);
            Assert.IsFalse(LoadTestDataController.GeneratedBuckets(_dataPath).Any());
            Assert.IsTrue(File.Exists(Path.Combine(_dataPath, "orders", "000", "7.json")));
        }

        [TestMethod]
        public void OutsideIsolatedMode_Is404_AndWritesNothing()
        {
            Assert.IsInstanceOfType(Controller(isolated: false).Generate(10), typeof(NotFoundResult));
            Assert.IsInstanceOfType(Controller(isolated: false).Remove(), typeof(NotFoundResult));
            Assert.IsFalse(LoadTestDataController.GeneratedBuckets(_dataPath).Any());
        }
    }
}
