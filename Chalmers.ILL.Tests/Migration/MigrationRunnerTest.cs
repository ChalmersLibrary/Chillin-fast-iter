using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chalmers.ILL.Migration;
using Chalmers.ILL.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

namespace Chalmers.ILL.Tests.Migration
{
    // Characterization tests for the fas 7 migration tool (TODO-remove-dotnet-framework.md,
    // "Bygg engångsmigreringen från SQL till filer"). SqlOrderItemSource itself (the actual SQL
    // reads) can't be exercised here - there is no SQL Server in this environment - so these
    // tests cover MigrationRunner/OrderFileWriter against a FakeOrderItemSource instead: the file
    // layout, the counters/index it seeds, and - most importantly - that its self-verification
    // (count check + spot check) actually fails when source and file disagree, not just when
    // everything already matches.
    [TestClass]
    public class MigrationRunnerTest
    {
        private string _dataPath;
        private string _ordersDirectory;

        [TestInitialize]
        public void Setup()
        {
            _dataPath = Path.Combine(Path.GetTempPath(), "chillin-migration-test-" + Guid.NewGuid());
            _ordersDirectory = Path.Combine(_dataPath, "orders");
        }

        private static OrderItemModel MakeOrder(int nodeId, string orderId)
        {
            return new OrderItemModel
            {
                NodeId = nodeId,
                OrderId = orderId,
                PatronName = "Patron " + nodeId,
                LogItemsList = new List<LogItem>
                {
                    new LogItem { Id = Guid.NewGuid(), NodeId = nodeId, OrderItemNodeId = nodeId, Type = "INFO", Message = "m1", CreateDate = DateTime.UtcNow },
                    new LogItem { Id = Guid.NewGuid(), NodeId = nodeId, OrderItemNodeId = nodeId, Type = "INFO", Message = "m2", CreateDate = DateTime.UtcNow },
                },
                AttachmentList = new List<OrderAttachment>
                {
                    new OrderAttachment { DbId = Guid.NewGuid(), Title = "bilaga", MediaItemNodeId = "media-" + nodeId },
                },
                SierraInfo = new SierraModel { id = "sierra-" + nodeId },
            };
        }

        [TestMethod]
        public void Run_WritesOneFilePerOrder_AtCorrectBucketPath()
        {
            var orders = new List<OrderItemModel> { MakeOrder(5, "o-5"), MakeOrder(1500, "o-1500"), MakeOrder(2999, "o-2999") };
            var writer = new OrderFileWriter(_ordersDirectory);
            var runner = new MigrationRunner(new FakeOrderItemSource(orders), writer);

            runner.Run();

            Assert.IsTrue(File.Exists(Path.Combine(_ordersDirectory, "000", "5.json")));
            Assert.IsTrue(File.Exists(Path.Combine(_ordersDirectory, "001", "1500.json")));
            Assert.IsTrue(File.Exists(Path.Combine(_ordersDirectory, "002", "2999.json")));

            var written = JsonConvert.DeserializeObject<OrderItemModel>(File.ReadAllText(Path.Combine(_ordersDirectory, "001", "1500.json")));
            Assert.AreEqual("o-1500", written.OrderId);
            Assert.AreEqual(2, written.LogItemsList.Count);
            Assert.AreEqual(1, written.AttachmentList.Count);
            Assert.AreEqual("sierra-1500", written.SierraInfo.id);
        }

        [TestMethod]
        public void Run_SeedsNodeIdCounterToMaxPlusOne()
        {
            var orders = new List<OrderItemModel> { MakeOrder(5, "o-5"), MakeOrder(42, "o-42"), MakeOrder(17, "o-17") };
            var runner = new MigrationRunner(new FakeOrderItemSource(orders), new OrderFileWriter(_ordersDirectory));

            runner.Run();

            var counter = File.ReadAllText(Path.Combine(_ordersDirectory, "next-node-id.txt"));
            Assert.AreEqual(43, int.Parse(counter.Trim()));
        }

        [TestMethod]
        public void Run_BuildsOrderIdIndex()
        {
            var orders = new List<OrderItemModel> { MakeOrder(5, "o-5"), MakeOrder(6, "o-6") };
            var runner = new MigrationRunner(new FakeOrderItemSource(orders), new OrderFileWriter(_ordersDirectory));

            runner.Run();

            var index = JsonConvert.DeserializeObject<Dictionary<string, int>>(
                File.ReadAllText(Path.Combine(_ordersDirectory, "orderid-index.json")));

            Assert.AreEqual(5, index["o-5"]);
            Assert.AreEqual(6, index["o-6"]);
        }

        [TestMethod]
        public void Run_ReportsSourceAndWrittenCounts()
        {
            var orders = new List<OrderItemModel> { MakeOrder(1, "o-1"), MakeOrder(2, "o-2"), MakeOrder(3, "o-3") };
            var runner = new MigrationRunner(new FakeOrderItemSource(orders), new OrderFileWriter(_ordersDirectory));

            var result = runner.Run();

            Assert.AreEqual(3, result.SourceOrderCount);
            Assert.AreEqual(3, result.WrittenFileCount);
            Assert.AreEqual(6, result.SourceLogItemCount);
            Assert.AreEqual(3, result.SourceAttachmentCount);
            Assert.AreEqual(3, result.MaxNodeId);
        }

        [TestMethod]
        public void Run_SpotCheckPasses_WhenSourceAndFileAgree()
        {
            var orders = Enumerable.Range(1, 10).Select(i => MakeOrder(i, "o-" + i)).ToList();
            var runner = new MigrationRunner(new FakeOrderItemSource(orders), new OrderFileWriter(_ordersDirectory), sampleSize: 10);

            var result = runner.Run();

            Assert.IsTrue(result.Success, string.Join("; ", result.Errors));
            Assert.AreEqual(10, result.SpotCheckedNodeIds.Count);
        }

        [TestMethod]
        public void Run_SpotCheckFails_WhenIndependentReadDisagreesWithWrittenFile()
        {
            var orders = Enumerable.Range(1, 5).Select(i => MakeOrder(i, "o-" + i)).ToList();
            var source = new FakeOrderItemSource(orders);

            // Simulate the migration having silently corrupted node 3's data: the file on disk
            // (built from `orders`) no longer matches what an independent re-read of "the
            // database" (ReadOneOverride) reports for that same order.
            source.ReadOneOverride = nodeId =>
            {
                var order = orders.First(o => o.NodeId == nodeId);
                if (nodeId == 3)
                {
                    var corrupted = JsonConvert.DeserializeObject<OrderItemModel>(JsonConvert.SerializeObject(order));
                    corrupted.PatronName = "NÅGON ANNAN";
                    return corrupted;
                }
                return order;
            };

            var runner = new MigrationRunner(source, new OrderFileWriter(_ordersDirectory), sampleSize: 5);

            var result = runner.Run();

            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.Errors.Any(e => e.Contains("3")), string.Join("; ", result.Errors));
        }

        [TestMethod]
        public void Run_NormalizesNullListsToEmptyLists()
        {
            var order = new OrderItemModel { NodeId = 1, OrderId = "o-1", LogItemsList = null, AttachmentList = null, SierraInfo = null };
            var runner = new MigrationRunner(new FakeOrderItemSource(new List<OrderItemModel> { order }), new OrderFileWriter(_ordersDirectory));

            runner.Run();

            var written = JsonConvert.DeserializeObject<OrderItemModel>(File.ReadAllText(Path.Combine(_ordersDirectory, "000", "1.json")));
            Assert.IsNotNull(written.LogItemsList);
            Assert.IsNotNull(written.AttachmentList);
            Assert.IsNotNull(written.SierraInfo);
        }
    }
}
