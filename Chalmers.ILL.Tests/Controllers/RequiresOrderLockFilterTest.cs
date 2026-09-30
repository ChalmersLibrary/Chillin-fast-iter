using System;
using System.Collections.Generic;
using System.Linq;
using Chalmers.ILL.Members;
using Chalmers.ILL.Models;
using Chalmers.ILL.OrderItems;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chalmers.ILL.Tests.Controllers
{
    // RequiresOrderLockFilter is the fix for a real bug found 2026-09-30 (via the user's own
    // two-user browser test, after LoginSurfaceController's memberId=0 bug was already fixed):
    // no mutation controller besides OrderItemSurfaceController itself ever checked EditedBy, so
    // a locked order could still be edited by a different member - the lock only gated which
    // HTML got rendered. IsolatedModeSmokeTest/browser-check would exercise the real HTTP
    // pipeline; these tests exercise the filter's own decision logic directly, the same way
    // ViewerReadOnlyFilterTest does for the sibling role-gating filter.
    [TestClass]
    public class RequiresOrderLockFilterTest
    {
        [TestMethod]
        public void RegisterGlobalFilters_AddsRequiresOrderLockFilter()
        {
            var options = new MvcOptions();

            FilterConfig.RegisterGlobalFilters(options);

            Assert.IsTrue(options.Filters.OfType<RequiresOrderLockFilter>().Any());
        }

        [TestMethod]
        public void ActionWithoutAttribute_IsNotBlocked()
        {
            var context = MakeContext(hasAttribute: false, currentMemberId: 1, actionArguments: new Dictionary<string, object> { { "nodeId", 42 } });
            SeedOrder(context, 42, editedBy: "999");

            new RequiresOrderLockFilter().OnActionExecuting(context);

            Assert.IsNull(context.Result);
        }

        [TestMethod]
        public void UnlockedOrder_IsNotBlocked()
        {
            var context = MakeContext(hasAttribute: true, currentMemberId: 1, actionArguments: new Dictionary<string, object> { { "nodeId", 42 } });
            SeedOrder(context, 42, editedBy: "");

            new RequiresOrderLockFilter().OnActionExecuting(context);

            Assert.IsNull(context.Result);
        }

        [TestMethod]
        public void OrderLockedByCurrentMember_IsNotBlocked()
        {
            var context = MakeContext(hasAttribute: true, currentMemberId: 1, actionArguments: new Dictionary<string, object> { { "nodeId", 42 } });
            SeedOrder(context, 42, editedBy: "1");

            new RequiresOrderLockFilter().OnActionExecuting(context);

            Assert.IsNull(context.Result);
        }

        [TestMethod]
        public void OrderLockedByAnotherMember_IsBlockedWithFailureJson()
        {
            var context = MakeContext(hasAttribute: true, currentMemberId: 1, actionArguments: new Dictionary<string, object> { { "nodeId", 42 } });
            SeedOrder(context, 42, editedBy: "999", editedByName: "Bob");

            new RequiresOrderLockFilter().OnActionExecuting(context);

            var jsonResult = context.Result as JsonResult;
            Assert.IsNotNull(jsonResult);
            var response = jsonResult.Value as ResultResponse;
            Assert.IsFalse(response.Success);
            StringAssert.Contains(response.Message, "Bob");
        }

        [TestMethod]
        public void PackJsonField_ReadsNodeIdFromJsonBody()
        {
            var context = MakeContext(hasAttribute: true, currentMemberId: 1,
                actionArguments: new Dictionary<string, object> { { "packJson", "{\"nodeId\":42,\"other\":\"x\"}" } },
                packJsonField: "nodeId");
            SeedOrder(context, 42, editedBy: "999", editedByName: "Bob");

            new RequiresOrderLockFilter().OnActionExecuting(context);

            var jsonResult = context.Result as JsonResult;
            Assert.IsNotNull(jsonResult);
            Assert.IsFalse(((ResultResponse)jsonResult.Value).Success);
        }

        [TestMethod]
        public void PackJsonField_DifferentFieldName_StillResolves()
        {
            // OrderItemReceiveBookSurfaceController.DeliveryReceivedPackage uses "orderNodeId", not "nodeId".
            var context = MakeContext(hasAttribute: true, currentMemberId: 1,
                actionArguments: new Dictionary<string, object> { { "packJson", "{\"orderNodeId\":7}" } },
                packJsonField: "orderNodeId");
            SeedOrder(context, 7, editedBy: "999", editedByName: "Bob");

            new RequiresOrderLockFilter().OnActionExecuting(context);

            Assert.IsFalse(((ResultResponse)((JsonResult)context.Result).Value).Success);
        }

        [TestMethod]
        public void ArrayParameter_AnyLockedByAnotherMember_IsBlocked()
        {
            var context = MakeContext(hasAttribute: true, currentMemberId: 1,
                actionArguments: new Dictionary<string, object> { { "nodeIds", new[] { 1, 2, 3 } } });
            SeedOrder(context, 1, editedBy: "");
            SeedOrder(context, 2, editedBy: "999", editedByName: "Bob");
            SeedOrder(context, 3, editedBy: "");

            new RequiresOrderLockFilter().OnActionExecuting(context);

            Assert.IsFalse(((ResultResponse)((JsonResult)context.Result).Value).Success);
        }

        [TestMethod]
        public void ArrayParameter_AllUnlockedOrOwnedByCurrentMember_IsNotBlocked()
        {
            var context = MakeContext(hasAttribute: true, currentMemberId: 1,
                actionArguments: new Dictionary<string, object> { { "nodeIds", new[] { 1, 2 } } });
            SeedOrder(context, 1, editedBy: "");
            SeedOrder(context, 2, editedBy: "1");

            new RequiresOrderLockFilter().OnActionExecuting(context);

            Assert.IsNull(context.Result);
        }

        [TestMethod]
        public void ModelParameter_ReadsNodeIdFieldViaReflection()
        {
            // OrderItemMailSurfaceController.SendMail binds a whole OutgoingMailPackageModel
            // instead of a plain nodeId - stand in with a minimal type carrying the same shape.
            var context = MakeContext(hasAttribute: true, currentMemberId: 1,
                actionArguments: new Dictionary<string, object> { { "m", new ModelWithNodeId { nodeId = 42 } } });
            SeedOrder(context, 42, editedBy: "999", editedByName: "Bob");

            new RequiresOrderLockFilter().OnActionExecuting(context);

            Assert.IsFalse(((ResultResponse)((JsonResult)context.Result).Value).Success);
        }

        [TestMethod]
        public void MissingActionArgument_FailsOpenRatherThanThrowOrBlock()
        {
            var context = MakeContext(hasAttribute: true, currentMemberId: 1, actionArguments: new Dictionary<string, object>());

            new RequiresOrderLockFilter().OnActionExecuting(context);

            Assert.IsNull(context.Result);
        }

        class ModelWithNodeId
        {
            public int nodeId;
        }

        private static void SeedOrder(ActionExecutingContext context, int nodeId, string editedBy, string editedByName = "")
        {
            var services = (StubServiceProvider)context.HttpContext.RequestServices;
            var manager = (StubOrderItemManagerForLock)services.GetService(typeof(IOrderItemManager));
            manager.Orders[nodeId] = new OrderItemModel { NodeId = nodeId, EditedBy = editedBy, EditedByMemberName = editedByName };
        }

        private static ActionExecutingContext MakeContext(bool hasAttribute, int currentMemberId, Dictionary<string, object> actionArguments, string packJsonField = null)
        {
            var httpContext = new DefaultHttpContext();
            httpContext.RequestServices = new StubServiceProvider(new StubOrderItemManagerForLock(), new StubMemberInfoManagerForLock(currentMemberId));

            var metadata = new List<object>();
            if (hasAttribute)
            {
                metadata.Add(packJsonField != null
                    ? new RequiresOrderLockAttribute { PackJsonField = packJsonField }
                    : new RequiresOrderLockAttribute { Parameter = actionArguments.Keys.FirstOrDefault() });
            }

            var descriptor = new ControllerActionDescriptor { EndpointMetadata = metadata };
            var actionContext = new ActionContext(httpContext, new RouteData(), descriptor);

            return new ActionExecutingContext(actionContext, new List<IFilterMetadata>(), actionArguments, controller: null);
        }

        class StubServiceProvider : IServiceProvider
        {
            private readonly IOrderItemManager _orderItemManager;
            private readonly IMemberInfoManager _memberInfoManager;

            public StubServiceProvider(IOrderItemManager orderItemManager, IMemberInfoManager memberInfoManager)
            {
                _orderItemManager = orderItemManager;
                _memberInfoManager = memberInfoManager;
            }

            public object GetService(System.Type serviceType)
            {
                if (serviceType == typeof(IOrderItemManager)) return _orderItemManager;
                if (serviceType == typeof(IMemberInfoManager)) return _memberInfoManager;
                return null;
            }
        }

        class StubMemberInfoManagerForLock : IMemberInfoManager
        {
            private readonly int _memberId;
            public StubMemberInfoManagerForLock(int memberId) { _memberId = memberId; }

            public int GetCurrentMemberId(HttpRequest request, HttpResponse response) => _memberId;
            public string GetCurrentMemberText(HttpRequest request, HttpResponse response) => "Test User";
            public string GetCurrentMemberLoginName(HttpRequest request, HttpResponse response) => "testuser";
            public void PopulateModelWithMemberData(HttpRequest request, HttpResponse response, Chalmers.ILL.Models.Page.ChalmersILLModel model) { }
            public void AddMemberToCache(HttpResponse response, int memberId, string memberText, string memberLoginName) { }
            public void ClearMemberCache(HttpResponse response) { }
        }

        class StubOrderItemManagerForLock : IOrderItemManager
        {
            public Dictionary<int, OrderItemModel> Orders = new Dictionary<int, OrderItemModel>();
            public OrderItemModel GetOrderItem(int nodeId) => Orders.TryGetValue(nodeId, out var item) ? item : null;

            public OrderItemModel GetOrderItem(string orderId) => throw new NotImplementedException();
            public IEnumerable<OrderItemModel> GetLockedOrderItems(string memberId) => throw new NotImplementedException();
            public List<LogItem> GetLogItems(int nodeId) => throw new NotImplementedException();
            public void AddExistingMediaItemAsAnAttachment(int orderNodeId, string mediaNodeId, string title, string link, string eventId, bool doReindex = true, bool doSignal = true) => throw new NotImplementedException();
            public void AddExistingMediaItemAsAnAttachmentWithoutLogging(int orderNodeId, string mediaNodeId, string title, string link, bool doReindex = true, bool doSignal = true) => throw new NotImplementedException();
            public void AddLogItem(int OrderItemNodeId, string Type, string Message, string eventId, bool doReindex = true, bool doSignal = true) => throw new NotImplementedException();
            public void AddSierraDataToLog(int orderItemNodeId, SierraModel sm, string eventId, bool doReindex = true, bool doSignal = true) => throw new NotImplementedException();
            public void RemoveConnectionToMediaItem(int orderNodeId, string mediaNodeId, bool doReindex = true, bool doSignal = true) => throw new NotImplementedException();
            public void SetFollowUpDateWithoutLogging(int nodeId, DateTime date, bool doReindex = true, bool doSignal = true) => throw new NotImplementedException();
            public void SetDrmWarningWithoutLogging(int orderNodeId, bool status, bool doReindex = true, bool doSignal = true) => throw new NotImplementedException();
            public void SetProviderNameWithoutLogging(int nodeId, string providerName, bool doReindex = true, bool doSignal = true) => throw new NotImplementedException();
            public void SetFollowUpDate(int nodeId, DateTime date, string eventId, bool doReindex = true, bool doSignal = true) => throw new NotImplementedException();
            public void SetDueDate(int nodeId, DateTime date, string eventId, bool doReindex = true, bool doSignal = true) => throw new NotImplementedException();
            public void SetProviderDueDate(int nodeId, DateTime date, string eventId, bool doReindex = true, bool doSignal = true) => throw new NotImplementedException();
            public void SetDeliveryDateWithoutLogging(int nodeId, DateTime date, string eventId, bool doReindex = true, bool doSignal = true) => throw new NotImplementedException();
            public void SetCancellationReason(int orderNodeId, int cancellationReasonId, string eventId, bool doReindex = true, bool doSignal = true) => throw new NotImplementedException();
            public void SetDeliveryLibrary(int orderNodeId, int deliveryLibraryId, string eventId, bool doReindex = true, bool doSignal = true) => throw new NotImplementedException();
            public void SetPurchaseLibrary(int orderNodeId, OrderItemModel.PurchaseLibraries library, string eventId, bool doReindex = true, bool doSignal = true) => throw new NotImplementedException();
            public void SetDeliveryLibrary(int orderNodeId, string deliveryLibraryPrevalue, string eventId, bool doReindex = true, bool doSignal = true) => throw new NotImplementedException();
            public void SetDrmWarning(int orderNodeId, bool status, string eventId, bool doReindex = true, bool doSignal = true) => throw new NotImplementedException();
            public void SetPurchasedMaterial(int orderNodeId, int purchasedMaterialId, string eventId, bool doReindex = true, bool doSignal = true) => throw new NotImplementedException();
            public void SetStatus(int orderNodeId, int statusId, string eventId, bool doReindex = true, bool doSignal = true) => throw new NotImplementedException();
            public void SetStatus(int orderNodeId, string statusPrevalue, string eventId, bool doReindex = true, bool doSignal = true) => throw new NotImplementedException();
            public void SetType(int orderNodeId, int typeId, string eventId, bool doReindex = true, bool doSignal = true) => throw new NotImplementedException();
            public void SetBookId(int nodeId, string bookId, string eventId, bool doReindex = true, bool doSignal = true) => throw new NotImplementedException();
            public void SetPatronData(int nodeId, string sierraInfo, int sierraPatronRecordId, int pType, string homeLibrary, string aff, bool doReindex = true, bool doSignal = true) => throw new NotImplementedException();
            public void SetPatronEmail(int nodeId, string email, string eventId, bool doReindex = true, bool doSignal = true) => throw new NotImplementedException();
            public void SetProviderName(int nodeId, string providerName, string eventId, bool doReindex = true, bool doSignal = true) => throw new NotImplementedException();
            public void SetProviderOrderId(int nodeId, string providerOrderId, string eventId, bool doReindex = true, bool doSignal = true) => throw new NotImplementedException();
            public void SetProviderInformation(int nodeId, string providerInformation, string eventId, bool doReindex = true, bool doSignal = true) => throw new NotImplementedException();
            public void SetReference(int nodeId, string reference, string eventId, bool doReindex = true, bool doSignal = true) => throw new NotImplementedException();
            public void SilentAnonymization(int nodeId, string reference, IList<LogItem> logs, string eventId, bool doReindex = true, bool doSignal = true) => throw new NotImplementedException();
            public void SetReadOnlyAtLibrary(int nodeId, bool readOnlyAtLibrary, string eventId, bool doReindex = true, bool doSignal = true) => throw new NotImplementedException();
            public void SetEditedByData(int orderNodeId, string memberId, string memberName, bool doReindex = true, bool doSignal = true) => throw new NotImplementedException();
            public int CreateOrderItemInDbFromMailQueueModel(Chalmers.ILL.Models.Mail.MailQueueModel model, bool doReindex = true, bool doSignal = true) => throw new NotImplementedException();
            public int CreateOrderItemInDbFromOrderItemSeedModel(OrderItemSeedModel model, bool doReindex = true, bool doSignal = true) => throw new NotImplementedException();
            public int CreateOrderItemInDbFromOrderItemModel(OrderItemModel model, bool doReindex = true, bool doSignal = true) => throw new NotImplementedException();
            public void SaveWithoutEventsAndWithSynchronousReindexing(int nodeId, bool doReindex = true, bool doSignal = true) => throw new NotImplementedException();
            public string GenerateEventId(int type) => throw new NotImplementedException();
            public void SetTitleInformation(int nodeId, string titleInformation, string eventId, bool doReindex = true, bool doSignal = true) => throw new NotImplementedException();
            public void AnonymizeOrder(int nodeId, string eventId, bool doReindex = true, bool doSignal = true) => throw new NotImplementedException();
            public void MakeDuplicate(int orderNodeId, string eventId, bool doReindex = true, bool doSignal = true) => throw new NotImplementedException();
            public void SetIsAnonymized(int nodeId, bool isAnonymized, string eventId, bool doReindex = true, bool doSignal = true) => throw new NotImplementedException();
            public void ResetAllAnonymizationFlags(int nodeId, string eventId, bool doReindex = true, bool doSignal = true) => throw new NotImplementedException();
        }
    }
}
