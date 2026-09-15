using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using Chalmers.ILL.Controllers.SurfaceControllers;
using Chalmers.ILL.Models;
using Chalmers.ILL.Models.Mail;
using Chalmers.ILL.Models.PartialPage;
using Chalmers.ILL.OrderItems;
using Chalmers.ILL.UmbracoApi;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Chalmers.ILL.Models.OrderItemModel;

namespace Chalmers.ILL.Tests.Controllers
{
    [TestClass]
    public class LogItemSurfaceControllerTest
    {
        [TestMethod]
        public void RenderLogEntryAction_PopulatesOrderItemAndAvailableStatuses()
        {
            var statuses = new List<DropdownOption> { new DropdownOption { Id = 1, Value = "01:Ny" } };
            var orderItemManager = new StubOrderItemManager();
            var controller = new LogItemSurfaceController(orderItemManager, new StubOrderConfig(statuses));

            var result = controller.RenderLogEntryAction(42) as PartialViewResult;
            var model = result?.Model as ChalmersILLActionLogEntryModel;

            Assert.IsNotNull(model);
            Assert.AreEqual("Chalmers.ILL.Action.LogEntry", result.ViewName);
            Assert.AreEqual(42, model.OrderItem.NodeId);
            Assert.AreEqual(1, model.AvailableStatuses.Count);
            Assert.AreEqual("01:Ny", model.AvailableStatuses[0].Value);
        }

        [TestMethod]
        public void GetLogItemsAsPartial_ReturnsPartialViewWithLogItems()
        {
            var orderItemManager = new StubOrderItemManager();
            orderItemManager.LogItems.Add(new LogItem { Type = "LOG", Message = "Hej" });
            var controller = new LogItemSurfaceController(orderItemManager, new StubOrderConfig(new List<DropdownOption>()));

            var result = controller.GetLogItemsAsPartial(42) as PartialViewResult;
            var model = result?.Model as List<LogItem>;

            Assert.IsNotNull(model);
            Assert.AreEqual(1, model.Count);
            Assert.AreEqual("Hej", model[0].Message);
        }

        [TestMethod]
        public void WriteLogItem_ValidInput_AddsLogItemAndReturnsSuccess()
        {
            var orderItemManager = new StubOrderItemManager();
            var controller = new LogItemSurfaceController(orderItemManager, new StubOrderConfig(new List<DropdownOption>()));

            var result = controller.WriteLogItem(42, "LOG", "Ett meddelande", "", -1, -1, -1) as JsonResult;
            var json = result?.Value as ResultResponse;

            Assert.IsTrue(json.Success);
            Assert.AreEqual(42, orderItemManager.LastLogItemNodeId);
            Assert.AreEqual("LOG", orderItemManager.LastLogItemType);
            Assert.AreEqual("Ett meddelande", orderItemManager.LastLogItemMessage);
        }

        class StubOrderItemManager : IOrderItemManager
        {
            public List<LogItem> LogItems = new List<LogItem>();
            public int LastLogItemNodeId;
            public string LastLogItemType;
            public string LastLogItemMessage;

            public OrderItemModel GetOrderItem(int nodeId) => new OrderItemModel { NodeId = nodeId };
            public OrderItemModel GetOrderItem(string orderId) => null;
            public IEnumerable<OrderItemModel> GetLockedOrderItems(string memberId) => new List<OrderItemModel>();
            public List<LogItem> GetLogItems(int nodeId) => LogItems;
            public string GenerateEventId(int type) => "evt";
            public int CreateOrderItemInDbFromMailQueueModel(MailQueueModel model, bool doReindex = true, bool doSignal = true) => -1;
            public int CreateOrderItemInDbFromOrderItemSeedModel(OrderItemSeedModel model, bool doReindex = true, bool doSignal = true) => -1;
            public int CreateOrderItemInDbFromOrderItemModel(OrderItemModel model, bool doReindex = true, bool doSignal = true) => -1;
            public void SaveWithoutEventsAndWithSynchronousReindexing(int nodeId, bool doReindex = true, bool doSignal = true) { }
            public void AddExistingMediaItemAsAnAttachment(int orderNodeId, string mediaNodeId, string title, string link, string eventId, bool doReindex = true, bool doSignal = true) { }
            public void AddExistingMediaItemAsAnAttachmentWithoutLogging(int orderNodeId, string mediaNodeId, string title, string link, bool doReindex = true, bool doSignal = true) { }
            public void AddLogItem(int OrderItemNodeId, string Type, string Message, string eventId, bool doReindex = true, bool doSignal = true)
            {
                LastLogItemNodeId = OrderItemNodeId;
                LastLogItemType = Type;
                LastLogItemMessage = Message;
            }
            public void AddSierraDataToLog(int orderItemNodeId, SierraModel sm, string eventId, bool doReindex = true, bool doSignal = true) { }
            public void RemoveConnectionToMediaItem(int orderNodeId, string mediaNodeId, bool doReindex = true, bool doSignal = true) { }
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

        class StubOrderConfig : IChillinOrderConfiguration
        {
            private readonly List<DropdownOption> _statuses;

            public StubOrderConfig(List<DropdownOption> statuses)
            {
                _statuses = statuses;
            }

            public List<DropdownOption> GetAvailableStatuses() => _statuses;
            public List<DropdownOption> GetAvailableTypes() => new List<DropdownOption>();
            public List<DropdownOption> GetAvailableDeliveryLibraries() => new List<DropdownOption>();
            public List<DropdownOption> GetAvailableCancellationReasons() => new List<DropdownOption>();
            public List<DropdownOption> GetAvailablePurchasedMaterials() => new List<DropdownOption>();
            public string GetValueById(int id) => "";
            public int GetIdByValue(string listKey, string value) => -1;
            public void PopulateModelWithAvailableValues(OrderItemPageModelBase model)
            {
                model.AvailableStatuses = _statuses;
                model.AvailableTypes = new List<DropdownOption>();
                model.AvailableDeliveryLibraries = new List<DropdownOption>();
                model.AvailableCancellationReasons = new List<DropdownOption>();
                model.AvailablePurchasedMaterials = new List<DropdownOption>();
            }
        }
    }
}
