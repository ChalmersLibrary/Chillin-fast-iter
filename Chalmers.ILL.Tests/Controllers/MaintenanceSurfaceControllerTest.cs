using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using Chalmers.ILL.Controllers.SurfaceControllers;
using Chalmers.ILL.MediaItems;
using Chalmers.ILL.Models;
using Chalmers.ILL.Models.Mail;
using Chalmers.ILL.OrderItems;
using Microsoft.VisualStudio.TestTools.UnitTesting;
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
            var controller = new MaintenanceSurfaceController(orderItemManager, new StubMediaItemManager(deleted));

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
            var controller = new MaintenanceSurfaceController(new StubOrderItemManager(), new ThrowingMediaItemManager());

            var result = controller.RunMaintenanceJobs() as JsonResult;
            var response = result?.Value as ResultResponse;

            Assert.IsNotNull(response);
            Assert.IsFalse(response.Success);
            StringAssert.Contains(response.Message, "Failed to remove old media items.");
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
    }
}
