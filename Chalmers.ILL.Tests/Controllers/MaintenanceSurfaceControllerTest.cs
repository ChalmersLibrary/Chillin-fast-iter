using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Chalmers.ILL.Controllers.SurfaceControllers;
using Chalmers.ILL.MediaItems;
using Chalmers.ILL.Models;
using Chalmers.ILL.Models.Mail;
using Chalmers.ILL.OrderItems;
using Chalmers.ILL.Tests.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using static Chalmers.ILL.Models.OrderItemModel;

namespace Chalmers.ILL.Tests.Controllers
{
    [TestClass]
    public class MaintenanceSurfaceControllerTest
    {
        [TestMethod]
        public void RunMaintenanceJobs_DeletesOldMediaItemsAndRemovesTheirConnections()
        {
            var deleted = new List<MediaItemIdAndOrderItemId>
            {
                new MediaItemIdAndOrderItemId("media-1", 10),
                new MediaItemIdAndOrderItemId("media-2", 20)
            };
            var orderItemManager = new StubOrderItemManager();
            var controller = NewController(orderItemManager, new StubMediaItemManager(deleted));

            var result = controller.RunMaintenanceJobs() as JsonResult;
            var response = result?.Value as ResultResponse;

            Assert.IsNotNull(response);
            Assert.IsTrue(response.Success);
            Assert.AreEqual("All maintenance jobs ran successfully.", response.Message);
            CollectionAssert.AreEquivalent(
                new List<string> { "10:media-1", "20:media-2" },
                orderItemManager.RemovedConnections);
        }

        [TestMethod]
        public void RunMaintenanceJobs_MediaItemDeletionFails_ReturnsFailureWithoutThrowing()
        {
            var controller = NewController(new StubOrderItemManager(), new ThrowingMediaItemManager());

            var result = controller.RunMaintenanceJobs() as JsonResult;
            var response = result?.Value as ResultResponse;

            Assert.IsNotNull(response);
            Assert.IsFalse(response.Success);
            StringAssert.Contains(response.Message, "Failed to remove old media items.");
        }

        [TestMethod]
        public void RebuildSearchIndex_NoOrdersDirectory_ReturnsSuccessWithZeroCount()
        {
            var dataPath = Path.Combine(Path.GetTempPath(), "chillin-rebuild-" + Guid.NewGuid());
            var searcher = new RecordingOrderItemSearcher();
            var controller = NewController(new StubOrderItemManager(), new StubMediaItemManager(new List<MediaItemIdAndOrderItemId>()), dataPath, searcher);

            var result = controller.RebuildSearchIndex() as JsonResult;
            var response = result?.Value as ResultResponse;

            Assert.IsNotNull(response);
            Assert.IsTrue(response.Success);
            Assert.AreEqual("Reindexed 0 orders.", response.Message);
            Assert.AreEqual(0, searcher.AddedItems.Count);
        }

        [TestMethod]
        public void RebuildSearchIndex_OrderFilesOnDisk_CallsAddedForEachAndSkipsCorruptOnes()
        {
            var dataPath = Path.Combine(Path.GetTempPath(), "chillin-rebuild-" + Guid.NewGuid());
            var ordersDirectory = Path.Combine(dataPath, "orders");
            Directory.CreateDirectory(ordersDirectory);
            File.WriteAllText(Path.Combine(ordersDirectory, "1.json"), JsonConvert.SerializeObject(new OrderItemModel { NodeId = 1 }));
            File.WriteAllText(Path.Combine(ordersDirectory, "2.json"), JsonConvert.SerializeObject(new OrderItemModel { NodeId = 2 }));
            File.WriteAllText(Path.Combine(ordersDirectory, "corrupt.json"), "{ not valid json");
            var searcher = new RecordingOrderItemSearcher();
            var controller = NewController(new StubOrderItemManager(), new StubMediaItemManager(new List<MediaItemIdAndOrderItemId>()), dataPath, searcher);

            var result = controller.RebuildSearchIndex() as JsonResult;
            var response = result?.Value as ResultResponse;

            Assert.IsNotNull(response);
            Assert.IsTrue(response.Success);
            Assert.AreEqual("Reindexed 2 orders.", response.Message);
            CollectionAssert.AreEquivalent(new List<int> { 1, 2 }, searcher.AddedItems.Select(o => o.NodeId).ToList());
        }

        [TestMethod]
        public void RebuildSearchIndex_SearcherThrows_ReturnsFailureWithoutThrowing()
        {
            var dataPath = Path.Combine(Path.GetTempPath(), "chillin-rebuild-" + Guid.NewGuid());
            var ordersDirectory = Path.Combine(dataPath, "orders");
            Directory.CreateDirectory(ordersDirectory);
            File.WriteAllText(Path.Combine(ordersDirectory, "1.json"), JsonConvert.SerializeObject(new OrderItemModel { NodeId = 1 }));
            var controller = NewController(new StubOrderItemManager(), new StubMediaItemManager(new List<MediaItemIdAndOrderItemId>()), dataPath, new ThrowingOrderItemSearcher());

            var result = controller.RebuildSearchIndex() as JsonResult;
            var response = result?.Value as ResultResponse;

            Assert.IsNotNull(response);
            Assert.IsFalse(response.Success);
            StringAssert.Contains(response.Message, "Failed to rebuild the search index.");
        }

        private static MaintenanceSurfaceController NewController(
            IOrderItemManager orderItemManager,
            IMediaItemManager mediaItemManager,
            string dataPath = null,
            IOrderItemSearcher orderItemSearcher = null)
        {
            return new MaintenanceSurfaceController(
                orderItemManager,
                mediaItemManager,
                new StubChillinConfiguration { DataPath = dataPath },
                orderItemSearcher ?? new RecordingOrderItemSearcher());
        }

        class StubMediaItemManager : IMediaItemManager
        {
            private readonly IList<MediaItemIdAndOrderItemId> _deleted;

            public StubMediaItemManager(IList<MediaItemIdAndOrderItemId> deleted)
            {
                _deleted = deleted;
            }

            public MediaItemModel CreateMediaItem(string name, int orderItemNodeId, string orderId, System.IO.Stream data, string contentType) => null;
            public IList<MediaItemIdAndOrderItemId> DeleteOlderThan(DateTime date) => _deleted;
            public MediaItemModel GetOne(string id) => null;
        }

        class ThrowingMediaItemManager : IMediaItemManager
        {
            public MediaItemModel CreateMediaItem(string name, int orderItemNodeId, string orderId, System.IO.Stream data, string contentType) => null;
            public IList<MediaItemIdAndOrderItemId> DeleteOlderThan(DateTime date) => throw new InvalidOperationException("Storage unavailable.");
            public MediaItemModel GetOne(string id) => null;
        }

        class StubOrderItemManager : IOrderItemManager
        {
            public List<string> RemovedConnections { get; } = new List<string>();

            public OrderItemModel GetOrderItem(int nodeId) => null;
            public OrderItemModel GetOrderItem(string orderId) => null;
            public IEnumerable<OrderItemModel> GetLockedOrderItems(string memberId) => new List<OrderItemModel>();
            public List<LogItem> GetLogItems(int nodeId) => new List<LogItem>();
            public string GenerateEventId(int type) => "evt";
            public int CreateOrderItemInDbFromMailQueueModel(MailQueueModel model, bool doReindex = true, bool doSignal = true) => -1;
            public int CreateOrderItemInDbFromOrderItemSeedModel(OrderItemSeedModel model, bool doReindex = true, bool doSignal = true) => -1;
            public int CreateOrderItemInDbFromOrderItemModel(OrderItemModel model, bool doReindex = true, bool doSignal = true) => -1;
            public void SaveWithoutEventsAndWithSynchronousReindexing(int nodeId, bool doReindex = true, bool doSignal = true) { }
            public void AddExistingMediaItemAsAnAttachment(int orderNodeId, string mediaNodeId, string title, string link, string eventId, bool doReindex = true, bool doSignal = true) { }
            public void AddExistingMediaItemAsAnAttachmentWithoutLogging(int orderNodeId, string mediaNodeId, string title, string link, bool doReindex = true, bool doSignal = true) { }
            public void AddLogItem(int OrderItemNodeId, string Type, string Message, string eventId, bool doReindex = true, bool doSignal = true) { }
            public void AddSierraDataToLog(int orderItemNodeId, SierraModel sm, string eventId, bool doReindex = true, bool doSignal = true) { }
            public void RemoveConnectionToMediaItem(int orderNodeId, string mediaNodeId, bool doReindex = true, bool doSignal = true)
            {
                RemovedConnections.Add(orderNodeId + ":" + mediaNodeId);
            }
            public void SetFollowUpDateWithoutLogging(int nodeId, DateTime date, bool doReindex = true, bool doSignal = true) { }
            public void SetDrmWarningWithoutLogging(int orderNodeId, bool status, bool doReindex = true, bool doSignal = true) { }
            public void SetProviderNameWithoutLogging(int nodeId, string providerName, bool doReindex = true, bool doSignal = true) { }
            public void SetFollowUpDate(int nodeId, DateTime date, string eventId, bool doReindex = true, bool doSignal = true) { }
            public void SetDueDate(int nodeId, DateTime date, string eventId, bool doReindex = true, bool doSignal = true) { }
            public void SetProviderDueDate(int nodeId, DateTime date, string eventId, bool doReindex = true, bool doSignal = true) { }
            public void SetDeliveryDateWithoutLogging(int nodeId, DateTime date, string eventId, bool doReindex = true, bool doSignal = true) { }
            public void SetCancellationReason(int orderNodeId, int cancellationReasonId, string eventId, bool doReindex = true, bool doSignal = true) { }
            public void SetDeliveryLibrary(int orderNodeId, int deliveryLibraryId, string eventId, bool doReindex = true, bool doSignal = true) { }
            public void SetPurchaseLibrary(int orderNodeId, PurchaseLibraries library, string eventId, bool doReindex = true, bool doSignal = true) { }
            public void SetDeliveryLibrary(int orderNodeId, string deliveryLibraryPrevalue, string eventId, bool doReindex = true, bool doSignal = true) { }
            public void SetDrmWarning(int orderNodeId, bool status, string eventId, bool doReindex = true, bool doSignal = true) { }
            public void SetPurchasedMaterial(int orderNodeId, int purchasedMaterialId, string eventId, bool doReindex = true, bool doSignal = true) { }
            public void SetStatus(int orderNodeId, int statusId, string eventId, bool doReindex = true, bool doSignal = true) { }
            public void SetStatus(int orderNodeId, string statusPrevalue, string eventId, bool doReindex = true, bool doSignal = true) { }
            public void SetType(int orderNodeId, int typeId, string eventId, bool doReindex = true, bool doSignal = true) { }
            public void SetBookId(int nodeId, string bookId, string eventId, bool doReindex = true, bool doSignal = true) { }
            public void SetPatronData(int nodeId, string sierraInfo, int sierraPatronRecordId, int pType, string homeLibrary, string aff, bool doReindex = true, bool doSignal = true) { }
            public void SetPatronEmail(int nodeId, string email, string eventId, bool doReindex = true, bool doSignal = true) { }
            public void SetProviderName(int nodeId, string providerName, string eventId, bool doReindex = true, bool doSignal = true) { }
            public void SetProviderOrderId(int nodeId, string providerOrderId, string eventId, bool doReindex = true, bool doSignal = true) { }
            public void SetProviderInformation(int nodeId, string providerInformation, string eventId, bool doReindex = true, bool doSignal = true) { }
            public void SetReference(int nodeId, string reference, string eventId, bool doReindex = true, bool doSignal = true) { }
            public void SilentAnonymization(int nodeId, string reference, IList<LogItem> logs, string eventId, bool doReindex = true, bool doSignal = true) { }
            public void SetReadOnlyAtLibrary(int nodeId, bool readOnlyAtLibrary, string eventId, bool doReindex = true, bool doSignal = true) { }
            public void SetEditedByData(int orderNodeId, string memberId, string memberName, bool doReindex = true, bool doSignal = true) { }
            public void SetTitleInformation(int nodeId, string titleInformation, string eventId, bool doReindex = true, bool doSignal = true) { }
            public void AnonymizeOrder(int nodeId, string eventId, bool doReindex = true, bool doSignal = true) { }
            public void MakeDuplicate(int orderNodeId, string eventId, bool doReindex = true, bool doSignal = true) { }
            public void SetIsAnonymized(int nodeId, bool isAnonymized, string eventId, bool doReindex = true, bool doSignal = true) { }
            public void ResetAllAnonymizationFlags(int nodeId, string eventId, bool doReindex = true, bool doSignal = true) { }
        }

        class RecordingOrderItemSearcher : IOrderItemSearcher
        {
            public List<OrderItemModel> AddedItems { get; } = new List<OrderItemModel>();

            public IEnumerable<OrderItemModel> Search(string query) => new List<OrderItemModel>();
            public SearchResult Search(string query, int start, int size) => new SearchResult { Count = 0, Items = new List<OrderItemModel>() };
            public IEnumerable<OrderItemModel> Search(string query, int size, string[] fields) => new List<OrderItemModel>();
            public IEnumerable<string> AggregatedProviders() => new List<string>();
            public void Added(OrderItemModel item) => AddedItems.Add(item);
            public void Modified(OrderItemModel item) { }
            public void Deleted(OrderItemModel item) { }
        }

        class ThrowingOrderItemSearcher : IOrderItemSearcher
        {
            public IEnumerable<OrderItemModel> Search(string query) => new List<OrderItemModel>();
            public SearchResult Search(string query, int start, int size) => new SearchResult { Count = 0, Items = new List<OrderItemModel>() };
            public IEnumerable<OrderItemModel> Search(string query, int size, string[] fields) => new List<OrderItemModel>();
            public IEnumerable<string> AggregatedProviders() => new List<string>();
            public void Added(OrderItemModel item) => throw new InvalidOperationException("Search index unavailable.");
            public void Modified(OrderItemModel item) { }
            public void Deleted(OrderItemModel item) { }
        }
    }
}
