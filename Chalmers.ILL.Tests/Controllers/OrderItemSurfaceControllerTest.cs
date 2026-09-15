using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Chalmers.ILL.Controllers.SurfaceControllers;
using Chalmers.ILL.Members;
using Chalmers.ILL.Models;
using Chalmers.ILL.Models.Mail;
using Chalmers.ILL.Models.Page;
using Chalmers.ILL.Models.PartialPage;
using Chalmers.ILL.OrderItems;
using Chalmers.ILL.SignalR;
using Chalmers.ILL.UmbracoApi;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Chalmers.ILL.Models.OrderItemModel;

namespace Chalmers.ILL.Tests.Controllers
{
    [TestClass]
    public class OrderItemSurfaceControllerTest
    {
        [TestMethod]
        public void RenderOrderItem_PopulatesAvailableStatusesAndTypes()
        {
            var statuses = new List<DropdownOption> { new DropdownOption { Id = 1, Value = "01:Ny" } };
            var types = new List<DropdownOption> { new DropdownOption { Id = 10, Value = "Artikel" } };

            var controller = MakeController(statuses, types, out _, out _);
            SetHttpContext(controller);

            var result = controller.RenderOrderItem(42) as PartialViewResult;
            var model = result?.Model as ChalmersILLOrderItemModel;

            Assert.IsNotNull(model);
            Assert.AreEqual(1, model.AvailableStatuses.Count);
            Assert.AreEqual("01:Ny", model.AvailableStatuses[0].Value);
            Assert.AreEqual(1, model.AvailableTypes.Count);
            Assert.AreEqual("Artikel", model.AvailableTypes[0].Value);
        }

        [TestMethod]
        public void TakeOverLockedOrderItem_SetsEditedByToCurrentMemberIdAndText()
        {
            var controller = MakeController(out var orderItemManager, out var notifier);
            SetHttpContext(controller);

            var result = controller.TakeOverLockedOrderItem(42) as JsonResult;
            var json = result?.Value as ResultResponse;

            Assert.IsTrue(json.Success);
            Assert.AreEqual("1", orderItemManager.LastEditedByMemberId);
            Assert.AreEqual("Test User", orderItemManager.LastEditedByMemberName);
            Assert.AreEqual("1", notifier.LastEditedBy);
            Assert.AreEqual("Test User", notifier.LastEditedByMemberName);
        }

        [TestMethod]
        public void LockOrderItem_WhenUnlocked_LocksWithCurrentMemberIdAndText()
        {
            var controller = MakeController(out var orderItemManager, out var notifier);
            SetHttpContext(controller);

            var result = controller.LockOrderItem(42) as JsonResult;
            var json = result?.Value as ResultResponse;

            Assert.IsTrue(json.Success);
            Assert.AreEqual("1", orderItemManager.LastEditedByMemberId);
            Assert.AreEqual("Test User", orderItemManager.LastEditedByMemberName);
        }

        [TestMethod]
        public void LockOrderItem_WhenAlreadyLocked_ReturnsFailureWithoutLocking()
        {
            var controller = MakeController(out var orderItemManager, out var notifier);
            orderItemManager.EditedBy = "5";
            SetHttpContext(controller);

            var result = controller.LockOrderItem(42) as JsonResult;
            var json = result?.Value as ResultResponse;

            Assert.IsFalse(json.Success);
            Assert.IsNull(orderItemManager.LastEditedByMemberId);
        }

        [TestMethod]
        public void UnlockOrderItem_WhenLockedByCurrentMember_Unlocks()
        {
            var controller = MakeController(out var orderItemManager, out var notifier);
            orderItemManager.EditedBy = "1";
            SetHttpContext(controller);

            var result = controller.UnlockOrderItem(42) as JsonResult;
            var json = result?.Value as ResultResponse;

            Assert.IsTrue(json.Success);
            Assert.AreEqual("", orderItemManager.LastEditedByMemberId);
            Assert.AreEqual("", orderItemManager.LastEditedByMemberName);
        }

        private static OrderItemSurfaceController MakeController(
            List<DropdownOption> statuses,
            List<DropdownOption> types,
            out StubOrderItemManager orderItemManager,
            out StubNotifier notifier)
        {
            orderItemManager = new StubOrderItemManager();
            notifier = new StubNotifier();
            return new OrderItemSurfaceController(
                new StubMemberInfoManager(),
                orderItemManager,
                notifier,
                new StubOrderConfig(statuses, types));
        }

        private static OrderItemSurfaceController MakeController(
            out StubOrderItemManager orderItemManager,
            out StubNotifier notifier)
        {
            return MakeController(new List<DropdownOption>(), new List<DropdownOption>(), out orderItemManager, out notifier);
        }

        private static void SetHttpContext(Controller controller)
        {
            var httpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "testuser") }, "TestAuth"))
            };
            httpContext.Request.Path = "/";
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        }

        class StubMemberInfoManager : IMemberInfoManager
        {
            public int GetCurrentMemberId(HttpRequest request, HttpResponse response) => 1;
            public string GetCurrentMemberText(HttpRequest request, HttpResponse response) => "Test User";
            public string GetCurrentMemberLoginName(HttpRequest request, HttpResponse response) => "testuser";
            public void PopulateModelWithMemberData(HttpRequest request, HttpResponse response, ChalmersILLModel model) { }
            public void AddMemberToCache(HttpResponse response, int memberId, string memberText, string memberLoginName) { }
            public void ClearMemberCache(HttpResponse response) { }
        }

        class StubOrderItemManager : IOrderItemManager
        {
            public string EditedBy = "";
            public string LastEditedByMemberId;
            public string LastEditedByMemberName;

            public OrderItemModel GetOrderItem(int nodeId) => new OrderItemModel { NodeId = nodeId, EditedBy = EditedBy };
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
            public void SetEditedByData(int orderNodeId, string memberId, string memberName, bool doReindex = true, bool doSignal = true)
            {
                LastEditedByMemberId = memberId;
                LastEditedByMemberName = memberName;
            }
            public void SetTitleInformation(int nodeId, string titleInformation, string eventId, bool doReindex = true, bool doSignal = true) { }
            public void AnonymizeOrder(int nodeId, string eventId, bool doReindex = true, bool doSignal = true) { }
            public void MakeDuplicate(int orderNodeId, string eventId, bool doReindex = true, bool doSignal = true) { }
            public void SetIsAnonymized(int nodeId, bool isAnonymized, string eventId, bool doReindex = true, bool doSignal = true) { }
            public void ResetAllAnonymizationFlags(int nodeId, string eventId, bool doReindex = true, bool doSignal = true) { }
        }

        class StubNotifier : INotifier
        {
            public string LastEditedBy;
            public string LastEditedByMemberName;

            public void ReportNewOrderItemUpdate(OrderItemModel orderItem) { }
            public void UpdateOrderItemUpdate(int nodeId, string editedBy, string editedByMemberName, bool significant = false, bool isPending = false, bool updateFromMail = false)
            {
                LastEditedBy = editedBy;
                LastEditedByMemberName = editedByMemberName;
            }
        }

        class StubOrderConfig : IChillinOrderConfiguration
        {
            private readonly List<DropdownOption> _statuses;
            private readonly List<DropdownOption> _types;

            public StubOrderConfig(List<DropdownOption> statuses, List<DropdownOption> types)
            {
                _statuses = statuses;
                _types = types;
            }

            public List<DropdownOption> GetAvailableStatuses() => _statuses;
            public List<DropdownOption> GetAvailableTypes() => _types;
            public List<DropdownOption> GetAvailableDeliveryLibraries() => new List<DropdownOption>();
            public List<DropdownOption> GetAvailableCancellationReasons() => new List<DropdownOption>();
            public List<DropdownOption> GetAvailablePurchasedMaterials() => new List<DropdownOption>();
            public string GetValueById(int id) => "";
            public int GetIdByValue(string listKey, string value) => -1;
            public void PopulateModelWithAvailableValues(OrderItemPageModelBase model)
            {
                model.AvailableStatuses = _statuses;
                model.AvailableTypes = _types;
                model.AvailableDeliveryLibraries = new List<DropdownOption>();
                model.AvailableCancellationReasons = new List<DropdownOption>();
                model.AvailablePurchasedMaterials = new List<DropdownOption>();
            }
        }
    }
}
