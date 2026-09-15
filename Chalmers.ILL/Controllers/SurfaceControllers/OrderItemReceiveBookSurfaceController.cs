using Chalmers.ILL.Mail;
using Chalmers.ILL.Models;
using Chalmers.ILL.Models.Mail;
using Chalmers.ILL.Models.PartialPage;
using Chalmers.ILL.OrderItems;
using Chalmers.ILL.Repositories;
using Chalmers.ILL.Services;
using Chalmers.ILL.Templates;
using Chalmers.ILL.UmbracoApi;
using Newtonsoft.Json;
using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace Chalmers.ILL.Controllers.SurfaceControllers
{
    [Authorize]
    public class OrderItemReceiveBookSurfaceController : Controller
    {
        public static int EVENT_TYPE { get { return 10; } }
        public static int BOOK_RECEIVED_AT_BRANCH_EVENT_TYPE { get { return 25; } }

        private IChillinOrderConfiguration _orderConfig;
        private  IOrderItemManager _orderItemManager;
        private  ITemplateService _templateService;
        private  IMailService _mailService;
        private  IFolioService _folioService;
        private readonly IChillinTextRepository _chillinTextRepository;

        public OrderItemReceiveBookSurfaceController(
            IChillinOrderConfiguration orderConfig,
            IOrderItemManager orderItemManager,
            ITemplateService templateService,
            IMailService mailService,
            IFolioService folioService,
            IChillinTextRepository chillinTextRepository)
        {
            _orderItemManager = orderItemManager;
            _templateService = templateService;
            _mailService = mailService;
            _orderConfig = orderConfig;
            _folioService = folioService;
            _chillinTextRepository = chillinTextRepository;
        }

        [HttpGet]
        public ActionResult RenderReceiveBookAction(int nodeId)
        {
            var standardTextTitle = _chillinTextRepository.ByTextField("standardTitleText");
            var pageModel = new ChalmersILLActionReceiveBookModel(_orderItemManager.GetOrderItem(nodeId), standardTextTitle.StandardTitleText);
            _orderConfig.PopulateModelWithAvailableValues(pageModel);
            pageModel.BookAvailableMailTemplate = _templateService.GetTemplateData("BookAvailableMailTemplate");
            return PartialView("Chalmers.ILL.Action.ReceiveBook", pageModel);
        }

        /// <summary>
        /// Set that the order item is received and ready for the patron to fetch.
        /// </summary>
        /// <param name="orderNodeId">OrderItem Node Id</param>
        /// <param name="bookId">Delivery Library Book Id</param>
        /// <param name="dueDate">Delivery Library Due Date</param>
        /// <param name="providerInformation">Information about the provider</param>
        /// <returns>MVC ActionResult with JSON</returns>
        [HttpPost]
        public ActionResult SetOrderItemDeliveryReceived(string packJson)
        {
            var json = new ResultResponse();

            try
            {
                DeliveryReceivedPackage pack = JsonConvert.DeserializeObject<DeliveryReceivedPackage>(packJson);

                var orderItem = _orderItemManager.GetOrderItem(pack.orderNodeId);
                    
                var eventId = _orderItemManager.GenerateEventId(EVENT_TYPE);

                _folioService.InitFolio(pack.Title, orderItem.OrderId, pack.bookId, pack.PickUpServicePoint, pack.readOnlyAtLibrary, pack.FolioUserId);

                if (pack.readOnlyAtLibrary)
                {
                    _orderItemManager.AddLogItem(pack.orderNodeId, "LEVERERAD", "Leveranstyp: Ej hemlån.", eventId, false, false);
                }
                else
                {
                    _orderItemManager.AddLogItem(pack.orderNodeId, "LEVERERAD", "Leveranstyp: Avhämtning i infodisk.", eventId, false, false);
                }
                _orderItemManager.SetDueDate(pack.orderNodeId, pack.dueDate, eventId, false, false);
                _orderItemManager.SetProviderDueDate(pack.orderNodeId, pack.dueDate, eventId, false, false);
                _orderItemManager.SetDeliveryDateWithoutLogging(pack.orderNodeId, DateTime.Now, eventId, false, false);
                _orderItemManager.SetBookId(pack.orderNodeId, pack.bookId, eventId, false, false);
                _orderItemManager.SetProviderInformation(pack.orderNodeId, pack.providerInformation, eventId, false, false);
                _orderItemManager.SetStatus(pack.orderNodeId, "17:FOLIO", eventId, false, false);
                _orderItemManager.SetTitleInformation(pack.orderNodeId, pack.Title, eventId, false, false);

                // Overwrite the message with message from template service so that we get the new values injected.
                if (pack.readOnlyAtLibrary)
                {
                    _orderItemManager.SetReadOnlyAtLibrary(pack.orderNodeId, true, eventId, false, false);
                }
                else
                {
                    _orderItemManager.SetReadOnlyAtLibrary(pack.orderNodeId, false, eventId, false, false);
                }
                _orderItemManager.AddLogItem(pack.orderNodeId, "LOG", pack.logMsg, eventId);

                json.Success = true;
                json.Message = "Leverans till infodisk genomförd.";
            }
            catch (Exception e)
            {
                json.Success = false;
                json.Message = "Error: " + e.Message;
            }

            return Json(json);
        }


        /// <summary>
        /// Set that the order item is received at branch and ready for the patron to fetch.
        /// </summary>
        /// <param name="orderNodeId">OrderItem Node Id</param>
        /// <param name="bookId">Delivery Library Book Id</param>
        /// <param name="dueDate">Delivery Library Due Date</param>
        /// <param name="providerInformation">Information about the provider</param>
        /// <returns>MVC ActionResult with JSON</returns>
        [HttpPost]
        public ActionResult SetOrderItemDeliveryReceivedAtBranch(int nodeId)
        {
            var json = new ResultResponse();

            try
            {
                DeliveryReceivedPackage pack = new DeliveryReceivedPackage();
                pack.orderNodeId = nodeId;

                var orderItem = _orderItemManager.GetOrderItem(pack.orderNodeId);

                pack.readOnlyAtLibrary = orderItem.ReadOnlyAtLibrary;

                var eventId = _orderItemManager.GenerateEventId(BOOK_RECEIVED_AT_BRANCH_EVENT_TYPE);

                if (pack.readOnlyAtLibrary)
                {
                    _orderItemManager.AddLogItem(pack.orderNodeId, "LEVERERAD", "Leveranstyp: Ej hemlån.", eventId, false, false);
                }
                else
                {
                    _orderItemManager.AddLogItem(pack.orderNodeId, "LEVERERAD", "Leveranstyp: Avhämtning i infodisk.", eventId, false, false);
                }
                _orderItemManager.SetStatus(pack.orderNodeId, "14:Infodisk", eventId, false, false);

                // We save everything here first so that we get the new values injected into the message by the template service.
                _orderItemManager.SetPatronEmail(pack.orderNodeId, pack.mailData.recipientEmail, eventId);

                // Overwrite the message with message from template service so that we get the new values injected.
                if (pack.readOnlyAtLibrary)
                {
                    pack.mailData.message = _templateService.GetTemplateData("BookAvailableForReadingAtLibraryMailTemplate", _orderItemManager.GetOrderItem(pack.orderNodeId));
                }
                else
                {
                    pack.mailData.message = _templateService.GetTemplateData("BookAvailableMailTemplate", _orderItemManager.GetOrderItem(pack.orderNodeId));
                }

                _mailService.SendMail(new OutgoingMailModel(orderItem.OrderId, pack.mailData));
                _orderItemManager.AddLogItem(pack.orderNodeId, "MAIL_NOTE", "Skickat mail till " + pack.mailData.recipientEmail, eventId, false, false);
                _orderItemManager.AddLogItem(pack.orderNodeId, "MAIL", pack.mailData.message, eventId);

                json.Success = true;
                json.Message = "Leverans till infodisk genomförd.";
            }
            catch (Exception e)
            {
                json.Success = false;
                json.Message = "Error: " + e.Message;
            }

            return Json(json);
        }


        public class DeliveryReceivedPackage
        {
            public int orderNodeId { get; set; }
            public string bookId { get; set; }
            public DateTime dueDate { get; set; }
            public string providerInformation { get; set; }
            public string logMsg { get; set; }
            public bool readOnlyAtLibrary { get; set; }
            public string Title { get; set; }
            public string OrderId { get; set; }
            public string PickUpServicePoint { get; set; }
            public string FolioUserId { get; set; }
            public OutgoingMailPackageModel mailData { get; set; }
        }
    }
}