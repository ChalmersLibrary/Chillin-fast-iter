using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chalmers.ILL.Configuration;
using Chalmers.ILL.Models;
using Chalmers.ILL.Models.Mail;
using Chalmers.ILL.OrderItems;
using Chalmers.ILL.SignalR;
using Chalmers.ILL.UmbracoApi;
using Chalmers.ILL.Tests.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chalmers.ILL.Tests.OrderItems
{
    // Characterization tests for FileOrderItemManager (fas 7, "Implementera filbaserad
    // IOrderItemManager"). The EF-based EntityFrameworkOrderItemManagerTest only had 2 tests, both
    // against the private FillOutStuff via reflection - the save path (and the doReindex=false
    // batching every controller in Controllers/SurfaceControllers relies on) was completely
    // untested. Each test here gets its own scratch DataPath so tests never share state.
    [TestClass]
    public class FileOrderItemManagerTest
    {
        private FileOrderItemManager CreateManager(out RecordingOrderItemSearcher searcher, out RecordingNotifier notifier, out IChillinConfiguration config)
        {
            var dataPath = Path.Combine(Path.GetTempPath(), "chillin-fileorderitemmanager-test-" + Guid.NewGuid());
            config = new StubChillinConfiguration { DataPath = dataPath };
            searcher = new RecordingOrderItemSearcher();
            notifier = new RecordingNotifier();

            var manager = new FileOrderItemManager(
                new StubChillinOrderConfiguration(),
                searcher,
                config,
                new NodeIdGenerator(config),
                new OrderIdIndex(config));
            manager.SetNotifier(notifier);
            return manager;
        }

        private FileOrderItemManager CreateManager(out RecordingOrderItemSearcher searcher, out RecordingNotifier notifier) =>
            CreateManager(out searcher, out notifier, out _);

        private FileOrderItemManager CreateManager() => CreateManager(out _, out _, out _);

        private static OrderItemModel NewOrderItem() => new OrderItemModel
        {
            LogItemsList = new List<LogItem>(),
            AttachmentList = new List<OrderAttachment>()
        };

        [TestMethod]
        public void FillOutStuff_UnclassifiedType_DefaultsToEmptyStringNotNull()
        {
            var manager = CreateManager();
            var nodeId = manager.CreateOrderItemInDbFromOrderItemModel(NewOrderItem()); // TypeId == -1, i.e. unclassified

            var orderItem = manager.GetOrderItem(nodeId);

            Assert.AreEqual("", orderItem.Type);
            Assert.AreEqual("", orderItem.DeliveryLibrary);
            Assert.AreEqual("", orderItem.CancellationReason);
            Assert.AreEqual("", orderItem.PurchasedMaterial);
        }

        [TestMethod]
        public void FillOutStuff_ClassifiedType_LooksUpValueFromConfig()
        {
            var manager = CreateManager();
            var item = NewOrderItem();
            item.TypeId = 42;
            var nodeId = manager.CreateOrderItemInDbFromOrderItemModel(item);

            var orderItem = manager.GetOrderItem(nodeId);

            Assert.AreEqual("Inköpsförslag", orderItem.Type);
        }

        [TestMethod]
        public void CreateThenGetOrderItem_ByNodeIdAndByOrderId_RoundTrips()
        {
            var manager = CreateManager();

            var nodeId = manager.CreateOrderItemInDbFromMailQueueModel(new MailQueueModel
            {
                OriginalOrder = "Please order this",
                PatronName = "Test Testsson",
                PatronEmail = "test@example.com",
                SierraPatronInfo = new SierraModel()
            });

            var byNodeId = manager.GetOrderItem(nodeId);
            Assert.IsNotNull(byNodeId);
            Assert.AreEqual("Please order this", byNodeId.OriginalOrder);

            var byOrderId = manager.GetOrderItem(byNodeId.OrderId);
            Assert.IsNotNull(byOrderId);
            Assert.AreEqual(nodeId, byOrderId.NodeId);
        }

        [TestMethod]
        public void GetOrderItem_UnknownNodeId_ReturnsNull()
        {
            var manager = CreateManager();

            Assert.IsNull(manager.GetOrderItem(999999));
            Assert.IsNull(manager.GetOrderItem("no-such-order-id"));
        }

        [TestMethod]
        public void SetReference_UnknownNodeId_ThrowsOrderItemNotFoundException()
        {
            var manager = CreateManager();

            Assert.ThrowsException<OrderItemNotFoundException>(() =>
                manager.SetReference(999999, "new reference", "event-1"));
        }

        [TestMethod]
        public void ChainedFalseFalseCalls_OnlyFlushOnceAtTheFinalDefaultCall()
        {
            // Mirrors the real pattern in Controllers/SurfaceControllers/OrderItemDeliverySurfaceController
            // etc: several AddLogItem/Set* calls with doReindex=false, doSignal=false, then one final
            // call with the defaults (true, true) that must persist and reindex/notify everything
            // together - not once per call.
            var manager = CreateManager(out var searcher, out var notifier);
            var nodeId = manager.CreateOrderItemInDbFromOrderItemModel(NewOrderItem());
            searcher.Reset();
            notifier.Reset();

            manager.AddLogItem(nodeId, "LOG", "Steg 1", "event-1", false, false);
            manager.AddLogItem(nodeId, "LOG", "Steg 2", "event-1", false, false);
            Assert.AreEqual(0, searcher.ModifiedCalls.Count, "Should not have reindexed yet - still batching.");
            Assert.AreEqual(0, notifier.Notifications.Count, "Should not have notified yet - still batching.");

            manager.SetReference(nodeId, "Final reference", "event-1"); // default true, true - flushes

            Assert.AreEqual(1, searcher.ModifiedCalls.Count, "Exactly one reindex for the whole batch.");
            Assert.AreEqual(1, notifier.Notifications.Count, "Exactly one notification for the whole batch.");

            var saved = manager.GetOrderItem(nodeId);
            Assert.AreEqual("Final reference", saved.Reference);
            Assert.IsTrue(saved.LogItemsList.Any(l => l.Message == "Steg 1"));
            Assert.IsTrue(saved.LogItemsList.Any(l => l.Message == "Steg 2"));
        }

        [TestMethod]
        public void DoReindexFalse_NeverPersistedIfNoLaterFlushHappens()
        {
            var manager = CreateManager(out _, out _, out var config);
            var nodeId = manager.CreateOrderItemInDbFromOrderItemModel(NewOrderItem());

            manager.SetReference(nodeId, "Should not be saved", "event-1", doReindex: false, doSignal: false);

            // A brand new manager instance (same DataPath) proves nothing hit disk - the change only
            // ever existed in the first manager's in-memory pending buffer.
            var freshManager = new FileOrderItemManager(
                new StubChillinOrderConfiguration(),
                new RecordingOrderItemSearcher(),
                config,
                new NodeIdGenerator(config),
                new OrderIdIndex(config));

            var reloaded = freshManager.GetOrderItem(nodeId);
            Assert.AreNotEqual("Should not be saved", reloaded.Reference);
        }

        [TestMethod]
        public void MutatingADifferentOrderWhileOneIsPendingOnTheSameThread_Throws()
        {
            var manager = CreateManager();
            var nodeIdA = manager.CreateOrderItemInDbFromOrderItemModel(NewOrderItem());
            var nodeIdB = manager.CreateOrderItemInDbFromOrderItemModel(NewOrderItem());

            manager.AddLogItem(nodeIdA, "LOG", "still pending", "event-1", false, false);

            Assert.ThrowsException<InvalidOperationException>(() =>
                manager.AddLogItem(nodeIdB, "LOG", "different order", "event-1", false, false));
        }

        [TestMethod]
        public void ResetAllAnonymizationFlags_AlreadyClean_DoesNotReindexOrNotify()
        {
            var manager = CreateManager(out var searcher, out var notifier);
            var nodeId = manager.CreateOrderItemInDbFromOrderItemModel(NewOrderItem()); // IsAnonymized == false by default
            searcher.Reset();
            notifier.Reset();

            manager.ResetAllAnonymizationFlags(nodeId, "event-1");

            Assert.AreEqual(0, searcher.ModifiedCalls.Count);
            Assert.AreEqual(0, notifier.Notifications.Count);
        }

        [TestMethod]
        public void ResetAllAnonymizationFlags_WasAnonymized_ResetsAndReindexes()
        {
            var manager = CreateManager(out var searcher, out _);
            var item = NewOrderItem();
            item.IsAnonymized = true;
            var nodeId = manager.CreateOrderItemInDbFromOrderItemModel(item);
            searcher.Reset();

            manager.ResetAllAnonymizationFlags(nodeId, "event-1");

            var saved = manager.GetOrderItem(nodeId);
            Assert.IsFalse(saved.IsAnonymized);
            Assert.AreEqual(1, searcher.ModifiedCalls.Count);
        }

        [TestMethod]
        public void MakeDuplicate_CreatesNewOrderAndLogsOnBoth()
        {
            var manager = CreateManager();
            var source = NewOrderItem();
            source.Reference = "Original reference";
            var sourceNodeId = manager.CreateOrderItemInDbFromOrderItemModel(source);

            manager.MakeDuplicate(sourceNodeId, "event-1");

            var updatedSource = manager.GetOrderItem(sourceNodeId);
            Assert.IsTrue(updatedSource.LogItemsList.Any(l => l.Type == "DUPLICERING" && l.Message.Contains("skapad med denna order som källa")));

            var duplicateOrderId = updatedSource.LogItemsList
                .First(l => l.Type == "DUPLICERING").Message;
            StringAssert.Contains(duplicateOrderId, "Kopia");
        }

        [TestMethod]
        public void GetLockedOrderItems_DelegatesToSearcherWithEditedByQuery()
        {
            var manager = CreateManager(out var searcher, out _);

            manager.GetLockedOrderItems("42");

            Assert.AreEqual("editedBy:\"42\"", searcher.LastQuery);
        }

        [TestMethod]
        public void NodeIdGenerator_NeverReusesAnId_AcrossManyOrders()
        {
            var manager = CreateManager();
            var ids = new HashSet<int>();

            for (var i = 0; i < 25; i++)
            {
                ids.Add(manager.CreateOrderItemInDbFromOrderItemModel(NewOrderItem()));
            }

            Assert.AreEqual(25, ids.Count, "Every allocated NodeId must be unique.");
        }

        class StubChillinOrderConfiguration : IChillinOrderConfiguration
        {
            public List<DropdownOption> GetAvailableTypes() => new List<DropdownOption>();
            public List<DropdownOption> GetAvailableStatuses() => new List<DropdownOption>();
            public List<DropdownOption> GetAvailableDeliveryLibraries() => new List<DropdownOption>();
            public List<DropdownOption> GetAvailableCancellationReasons() => new List<DropdownOption>();
            public List<DropdownOption> GetAvailablePurchasedMaterials() => new List<DropdownOption>();
            public string GetValueById(int id) => id == 42 ? "Inköpsförslag" : (id == -1 ? "" : "01:Ny");
            public int GetIdByValue(string listKey, string value) => value == "01:Ny" ? 1 : -1;
            public void PopulateModelWithAvailableValues(OrderItemPageModelBase model) { }
        }

        public class RecordingOrderItemSearcher : IOrderItemSearcher
        {
            public List<OrderItemModel> AddedCalls { get; } = new List<OrderItemModel>();
            public List<OrderItemModel> ModifiedCalls { get; } = new List<OrderItemModel>();
            public List<OrderItemModel> DeletedCalls { get; } = new List<OrderItemModel>();
            public string LastQuery { get; private set; }

            public IEnumerable<OrderItemModel> Search(string query) { LastQuery = query; return new List<OrderItemModel>(); }
            public SearchResult Search(string query, int start, int size) { LastQuery = query; return new SearchResult { Items = new List<OrderItemModel>(), Count = 0 }; }
            public IEnumerable<OrderItemModel> Search(string query, int size, string[] fields) { LastQuery = query; return new List<OrderItemModel>(); }
            public IEnumerable<string> AggregatedProviders() => new List<string>();
            public void Added(OrderItemModel item) => AddedCalls.Add(item);
            public void Modified(OrderItemModel item) => ModifiedCalls.Add(item);
            public void Deleted(OrderItemModel item) => DeletedCalls.Add(item);

            public void Reset()
            {
                AddedCalls.Clear();
                ModifiedCalls.Clear();
                DeletedCalls.Clear();
            }
        }

        public class RecordingNotifier : INotifier
        {
            public List<OrderItemModel> Notifications { get; } = new List<OrderItemModel>();
            public void ReportNewOrderItemUpdate(OrderItemModel orderItem) => Notifications.Add(orderItem);
            public void UpdateOrderItemUpdate(int nodeId, string editedBy, string editedByMemberName, bool significant = false, bool isPending = false, bool updateFromMail = false) { }

            public void Reset() => Notifications.Clear();
        }
    }
}
