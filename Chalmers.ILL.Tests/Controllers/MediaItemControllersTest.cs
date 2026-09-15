using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
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
    public class MediaItemControllersTest
    {
        // --- MediaItemSurfaceController ---

        [TestMethod]
        public void GetMediaItem_NotFound_ReturnsJsonError()
        {
            var controller = new MediaItemSurfaceController(new StubMediaItemManager(getOne: null));
            SetHttpContext(controller);

            var result = controller.GetMediaItem("missing") as JsonResult;

            Assert.IsNotNull(result);
            var data = (ResultResponse)result.Value;
            Assert.IsFalse(data.Success);
        }

        [TestMethod]
        public void GetMediaItem_Found_ReturnsFileResult()
        {
            var item = new MediaItemModel
            {
                Name = "test.pdf",
                Data = new MemoryStream(new byte[] { 1, 2, 3 }),
                ContentType = "application/pdf"
            };
            var controller = new MediaItemSurfaceController(new StubMediaItemManager(getOne: item));
            SetHttpContext(controller);

            var result = controller.GetMediaItem("123");

            Assert.IsInstanceOfType(result, typeof(FileResult));
        }

        // --- ImportDocumentSurfaceController ---

        [TestMethod]
        public void ImportFromData_EmptyFilename_ReturnsError()
        {
            var controller = new ImportDocumentSurfaceController(new StubOrderItemManager(), new StubMediaItemManager(create: null));
            SetHttpContext(controller);

            var result = controller.ImportFromData(1, "", "data:application/pdf;base64,AAAA") as JsonResult;

            Assert.IsNotNull(result);
            var data = (ResultResponse)result.Value;
            Assert.IsFalse(data.Success);
        }

        [TestMethod]
        public void ImportFromData_ValidData_ReturnsSuccessWithMediaItemId()
        {
            var saved = new MediaItemModel { Id = "media-1", Url = "http://example.com/doc.pdf" };
            var controller = new ImportDocumentSurfaceController(new StubOrderItemManager(), new StubMediaItemManager(create: saved));
            SetHttpContext(controller);

            // "data:application/pdf;base64,AAAA" splits on "base64[;,]" into ["data:application/pdf;", "AAAA"]
            var result = controller.ImportFromData(42, "doc.pdf", "data:application/pdf;base64,AAAA") as JsonResult;

            Assert.IsNotNull(result);
            var data = (ResultResponse)result.Value;
            Assert.IsTrue(data.Success);
            StringAssert.Contains(data.Message, "media-1");
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

        class StubMediaItemManager : IMediaItemManager
        {
            private readonly MediaItemModel _getOne;
            private readonly MediaItemModel _create;

            public StubMediaItemManager(MediaItemModel getOne = null, MediaItemModel create = null)
            {
                _getOne = getOne;
                _create = create;
            }

            public MediaItemModel GetOne(string id) => _getOne;
            public MediaItemModel CreateMediaItem(string name, int orderItemNodeId, string orderId, Stream data, string contentType) => _create;
            public IList<MediaItemIdAndOrderItemId> DeleteOlderThan(DateTime date) => new List<MediaItemIdAndOrderItemId>();
        }

        class StubOrderItemManager : IOrderItemManager
        {
            public OrderItemModel GetOrderItem(int nodeId) => new OrderItemModel { NodeId = nodeId, OrderId = "ord1", EditedBy = "" };
            public OrderItemModel GetOrderItem(string orderId) => null;
            public IEnumerable<OrderItemModel> GetLockedOrderItems(string memberId) => new List<OrderItemModel>();
            public List<LogItem> GetLogItems(int nodeId) => new List<LogItem>();
            public string GenerateEventId(int type) => "evt-1";
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
            public void SetEditedByData(int orderNodeId, string memberId, string memberName, bool doReindex = true, bool doSignal = true) { }
            public void SetTitleInformation(int nodeId, string titleInformation, string eventId, bool doReindex = true, bool doSignal = true) { }
            public void AnonymizeOrder(int nodeId, string eventId, bool doReindex = true, bool doSignal = true) { }
            public void MakeDuplicate(int orderNodeId, string eventId, bool doReindex = true, bool doSignal = true) { }
            public void SetIsAnonymized(int nodeId, bool isAnonymized, string eventId, bool doReindex = true, bool doSignal = true) { }
            public void ResetAllAnonymizationFlags(int nodeId, string eventId, bool doReindex = true, bool doSignal = true) { }
        }
    }
}
