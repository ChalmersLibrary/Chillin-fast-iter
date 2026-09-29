using System.Collections.Generic;
using System.IO;
using Chalmers.ILL.Configuration;
using Chalmers.ILL.Controllers.SurfaceControllers;
using Chalmers.ILL.Models;
using Chalmers.ILL.OrderItems;
using Chalmers.ILL.SignalR;
using Chalmers.ILL.UmbracoApi;
using Chalmers.ILL.Tests.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chalmers.ILL.Tests.Controllers
{
    // Regression test for a bug found in manual testing (2026-09-29): SetReference passed
    // doReindex=false, doSignal=false to FileOrderItemManager.SetReference with no follow-up
    // flushing call, unlike every other Set* controller. The edit was silently lost (never
    // written to disk) and the per-nodeId lock in FileOrderItemManager was left held forever,
    // so every later edit of the same order hung indefinitely on FileOrderItemManagerTest's
    // AcquireLock. Runs against the real FileOrderItemManager (not a stub), since the no-op
    // StubOrderItemManager fakes used by other controller tests can never catch a persistence bug.
    [TestClass]
    public class OrderItemReferenceSurfaceControllerTest
    {
        private FileOrderItemManager CreateManager(out IChillinConfiguration config)
        {
            var dataPath = Path.Combine(Path.GetTempPath(), "chillin-referencecontroller-test-" + System.Guid.NewGuid());
            config = new StubChillinConfiguration { DataPath = dataPath };

            var manager = new FileOrderItemManager(
                new StubChillinOrderConfiguration(),
                new StubOrderItemSearcher(),
                config,
                new NodeIdGenerator(config),
                new OrderIdIndex(config));
            manager.SetNotifier(new StubNotifier());
            return manager;
        }

        [TestMethod]
        public void SetReference_PersistsImmediately_LikeEveryOtherSetController()
        {
            var manager = CreateManager(out var config);
            var nodeId = manager.CreateOrderItemInDbFromOrderItemModel(new OrderItemModel
            {
                LogItemsList = new List<LogItem>(),
                AttachmentList = new List<OrderAttachment>()
            });
            var controller = new OrderItemReferenceSurfaceController(manager);

            controller.SetReference(nodeId, "New reference");

            // A brand new manager instance (same DataPath) proves the change actually hit disk,
            // rather than only living in the first manager's in-memory pending buffer.
            var freshManager = new FileOrderItemManager(
                new StubChillinOrderConfiguration(),
                new StubOrderItemSearcher(),
                config,
                new NodeIdGenerator(config),
                new OrderIdIndex(config));

            var reloaded = freshManager.GetOrderItem(nodeId);
            Assert.AreEqual("New reference", reloaded.Reference);
        }

        [TestMethod]
        public void SetReference_DoesNotLeaveOrderLockedForSubsequentEdits()
        {
            var manager = CreateManager(out _);
            var nodeId = manager.CreateOrderItemInDbFromOrderItemModel(new OrderItemModel
            {
                LogItemsList = new List<LogItem>(),
                AttachmentList = new List<OrderAttachment>()
            });
            var controller = new OrderItemReferenceSurfaceController(manager);

            controller.SetReference(nodeId, "New reference");

            // If SetReference left the per-nodeId lock held (the original bug), this second,
            // unrelated mutation on the same manager instance would hang forever.
            manager.SetStatus(nodeId, 1, "event-2");

            var saved = manager.GetOrderItem(nodeId);
            Assert.AreEqual("New reference", saved.Reference);
        }

        class StubChillinOrderConfiguration : IChillinOrderConfiguration
        {
            public List<DropdownOption> GetAvailableTypes() => new List<DropdownOption>();
            public List<DropdownOption> GetAvailableStatuses() => new List<DropdownOption>();
            public List<DropdownOption> GetAvailableDeliveryLibraries() => new List<DropdownOption>();
            public List<DropdownOption> GetAvailableCancellationReasons() => new List<DropdownOption>();
            public List<DropdownOption> GetAvailablePurchasedMaterials() => new List<DropdownOption>();
            public string GetValueById(int id) => "";
            public int GetIdByValue(string listKey, string value) => -1;
            public void PopulateModelWithAvailableValues(OrderItemPageModelBase model) { }
        }

        class StubOrderItemSearcher : IOrderItemSearcher
        {
            public IEnumerable<OrderItemModel> Search(string query) => new List<OrderItemModel>();
            public SearchResult Search(string query, int start, int size) => new SearchResult { Items = new List<OrderItemModel>(), Count = 0 };
            public IEnumerable<OrderItemModel> Search(string query, int size, string[] fields) => new List<OrderItemModel>();
            public IEnumerable<string> AggregatedProviders() => new List<string>();
            public void Added(OrderItemModel item) { }
            public void Modified(OrderItemModel item) { }
            public void Deleted(OrderItemModel item) { }
        }

        class StubNotifier : INotifier
        {
            public void ReportNewOrderItemUpdate(OrderItemModel orderItem) { }
            public void UpdateOrderItemUpdate(int nodeId, string editedBy, string editedByMemberName, bool significant = false, bool isPending = false, bool updateFromMail = false) { }
        }
    }
}
