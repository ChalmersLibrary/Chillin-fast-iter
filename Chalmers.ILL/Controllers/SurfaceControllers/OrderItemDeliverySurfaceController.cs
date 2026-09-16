using Chalmers.ILL.Mail;
using Chalmers.ILL.Models;
using Chalmers.ILL.Models.Mail;
using Chalmers.ILL.Models.PartialPage;
using Chalmers.ILL.OrderItems;
using Chalmers.ILL.Templates;
using Chalmers.ILL.UmbracoApi;
using Newtonsoft.Json;
using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using QRCoder;
using Chalmers.ILL.Configuration;

namespace Chalmers.ILL.Controllers.SurfaceControllers
{

    [Authorize]
    public class OrderItemDeliverySurfaceController : Controller
    {
        public static int EVENT_TYPE { get { return 9; } }
        public static int ARTICLE_SENT_TO_BRANCH_EVENT_TYPE { get { return 26; } }

        IOrderItemManager _orderItemManager;
        IChillinOrderConfiguration _orderConfig;
        ITemplateService _templateService;
        IMailService _mailService;
        IChillinConfiguration _config;

        public OrderItemDeliverySurfaceController(IOrderItemManager orderItemManager, IChillinOrderConfiguration orderConfig,
            ITemplateService templateService, IMailService mailService, IChillinConfiguration config)
        {
            _orderItemManager = orderItemManager;
            _orderConfig = orderConfig;
            _templateService = templateService;
            _mailService = mailService;
            _config = config;
        }

        /// <summary>
        /// Render the Partial View for the action of delivering an item.
        /// </summary>
        /// <param name="nodeId">OrderItem Node Id</param>
        /// <returns>Partial View</returns>
        [HttpGet]
        public ActionResult RenderDeliveryAction(int nodeId)
        {
            var pageModel = new ChalmersILLActionDeliveryModel(_orderItemManager.GetOrderItem(nodeId));
            _orderConfig.PopulateModelWithAvailableValues(pageModel);
            return PartialView("Chalmers.ILL.Action.Delivery", pageModel);
        }

        /// <summary>
        /// Render the Partial View for the article by e-mail delivery type.
        /// </summary>
        /// <param name="nodeId">OrderItem Node Id</param>
        /// <returns>Partial View</returns>
        [HttpGet]
        public ActionResult RenderArticleByEmailDeliveryType(int nodeId)
        {
            var pageModel = new Models.PartialPage.DeliveryType.ArticleByEmail(_orderItemManager.GetOrderItem(nodeId));
            _orderConfig.PopulateModelWithAvailableValues(pageModel);
            pageModel.ArticleDeliveryByMailTemplate = _templateService.GetTemplateData("ArticleDeliveryByMailTemplate");
            pageModel.DrmWarning = pageModel.OrderItem.DrmWarning == "1" ? true : false;
            return PartialView("DeliveryType/ArticleByEmail", pageModel);
        }

        /// <summary>
        /// Render the Partial View for the article by mail or internal mail delivery type.
        /// </summary>
        /// <param name="nodeId">OrderItem Node Id</param>
        /// <returns>Partial View</returns>
        [HttpGet]
        public ActionResult RenderArticleByMailOrInternalMailDeliveryType(int nodeId)
        {
            var pageModel = new Models.PartialPage.DeliveryType.ArticleByMailOrInternalMail(_orderItemManager.GetOrderItem(nodeId));
            _orderConfig.PopulateModelWithAvailableValues(pageModel);
            pageModel.ArticleDeliveryByPostTemplate = _templateService.GetTemplateData("ArticleDeliveryByPostTemplate");
            pageModel.ArticleDeliveryByInternpostTemplate = _templateService.GetTemplateData("ArticleDeliveryByInternpostTemplate");
            pageModel.DrmWarning = pageModel.OrderItem.DrmWarning == "1" ? true : false;
            return PartialView("DeliveryType/ArticleByMailOrInternalMail", pageModel);
        }

        /// <summary>
        /// Render the Partial View for the article in infodisk delivery type.
        /// </summary>
        /// <param name="nodeId">OrderItem Node Id</param>
        /// <returns>Partial View</returns>
        [HttpGet]
        public ActionResult RenderArticleInInfodiskDeliveryType(int nodeId)
        {
            var pageModel = new Models.PartialPage.DeliveryType.ArticleInInfodisk(_orderItemManager.GetOrderItem(nodeId));
            _orderConfig.PopulateModelWithAvailableValues(pageModel);
            pageModel.DrmWarning = pageModel.OrderItem.DrmWarning == "1" ? true : false;
            pageModel.ArticleDeliveryLibrary = _templateService.GetPrettyLibraryNameFromLibraryAbbreviation(pageModel.OrderItem.SierraInfo.home_library);
            pageModel.ArticleAvailableInInfodiskMailTemplate = _templateService.GetTemplateData("ArticleAvailableInInfodiskMailTemplate", pageModel.OrderItem);
            return PartialView("DeliveryType/ArticleInInfodisk", pageModel);
        }

        /// <summary>
        /// Render the Partial View for the article in transit delivery type.
        /// </summary>
        /// <param name="nodeId">OrderItem Node Id</param>
        /// <returns>Partial View</returns>
        [HttpGet]
        public ActionResult RenderArticleInTransitDeliveryType(int nodeId)
        {
            var pageModel = new Models.PartialPage.DeliveryType.ArticleInTransit(_orderItemManager.GetOrderItem(nodeId));
            _orderConfig.PopulateModelWithAvailableValues(pageModel);
            pageModel.DrmWarning = pageModel.OrderItem.DrmWarning == "1" ? true : false;
            pageModel.ArticleDeliveryLibrary = _templateService.GetPrettyLibraryNameFromLibraryAbbreviation(pageModel.OrderItem.SierraInfo.home_library);

            // Generate QR code which should be printed on the slip and scanned to register that the article has been received at branch.
            // Was System.Drawing/GDI+ (QRCode + Bitmap) - Windows-only, throws
            // PlatformNotSupportedException on Linux from .NET 6 on. PngByteQRCode returns the
            // PNG bytes directly, no GDI+ involved (fas 8, pulled forward - required to even
            // compile on net10.0/Linux).
            QRCodeGenerator qrGenerator = new QRCodeGenerator();
            QRCodeData qrCodeData = qrGenerator.CreateQrCode(_config.BaseUrl + "/OrderItemReceivedAtBranchSurface/RenderResponse?nodeId=" + pageModel.OrderItem.NodeId, QRCodeGenerator.ECCLevel.Q);
            PngByteQRCode qrCode = new PngByteQRCode(qrCodeData);
            byte[] qrCodeBytes = qrCode.GetGraphic(4);
            var base64 = Convert.ToBase64String(qrCodeBytes);
            pageModel.RegisterReceivedQrCode = "data:image/png;base64," + base64;

            return PartialView("DeliveryType/ArticleInTransit", pageModel);
        }

        [HttpGet]
        public ActionResult RenderArticleFromProviderDeliveryType(int nodeId)
        {
            var pageModel = new Models.PartialPage.DeliveryType.ArticleFromProvider(_orderItemManager.GetOrderItem(nodeId));
            _orderConfig.PopulateModelWithAvailableValues(pageModel);
            return PartialView("DeliveryType/ArticleFromProvider", pageModel);
        }

        /// <summary>
        /// Render the Partial View for the book instant loan delivery type.
        /// </summary>
        /// <param name="nodeId">OrderItem Node Id</param>
        /// <returns>Partial View</returns>
        [HttpGet]
        public ActionResult RenderBookInstantLoanDeliveryType(int nodeId)
        {
            var pageModel = new Models.PartialPage.DeliveryType.BookInstantLoan(_orderItemManager.GetOrderItem(nodeId));
            _orderConfig.PopulateModelWithAvailableValues(pageModel);
            pageModel.BookAvailableMailTemplate = _templateService.GetTemplateData("BookAvailableMailTemplate", pageModel.OrderItem);
            return PartialView("DeliveryType/BookInstantLoan", pageModel);
        }

        /// <summary>
        /// Render the Partial View for the book read at library delivery type.
        /// </summary>
        /// <param name="nodeId">OrderItem Node Id</param>
        /// <returns>Partial View</returns>
        [HttpGet]
        public ActionResult RenderBookReadAtLibraryDeliveryType(int nodeId)
        {
            var pageModel = new Models.PartialPage.DeliveryType.BookReadAtLibrary(_orderItemManager.GetOrderItem(nodeId));
            _orderConfig.PopulateModelWithAvailableValues(pageModel);
            pageModel.BookAvailableForReadingAtLibraryMailTemplate = _templateService.GetTemplateData("BookAvailableForReadingAtLibraryMailTemplate", pageModel.OrderItem);
            return PartialView("DeliveryType/BookReadAtLibrary", pageModel);
        }

        /// <summary>
        /// Log message, set delivered status and log delivery type.
        /// </summary>
        /// <param name="nodeId">OrderItem Node Id</param>
        /// <param name="logEntry">Log message</param>
        /// <param name="delivery">Type of delivery</param>
        /// <returns>JSON result</returns>
        [HttpPost]
        public ActionResult SetDelivery(int nodeId, string logEntry, string delivery)
        {
            var json = new ResultResponse();

            try
            {
                var eventId = _orderItemManager.GenerateEventId(EVENT_TYPE);
                _orderItemManager.AddLogItem(nodeId, "LEVERERAD", "Leveranstyp: " + delivery, eventId, false, false);
                _orderItemManager.AddLogItem(nodeId, "LOG", logEntry, eventId, false, false);
                _orderItemManager.SetStatus(nodeId, "05:Levererad", eventId);

                json.Success = true;
                json.Message = "Saved provider data.";
            }
            catch (Exception e)
            {
                json.Success = false;
                json.Message = "Error: " + e.Message;
            }

            return Json(json);
        }

        /// <summary>
        /// Log message, set transport status and log delivery type.
        /// </summary>
        /// <param name="nodeId">OrderItem Node Id</param>
        /// <param name="logEntry">Log message</param>
        /// <param name="delivery">Type of delivery</param>
        /// <returns>JSON result</returns>
        [HttpPost]
        public ActionResult SetTransport(int nodeId, string logEntry, string delivery)
        {
            var json = new ResultResponse();

            try
            {
                var eventId = _orderItemManager.GenerateEventId(ARTICLE_SENT_TO_BRANCH_EVENT_TYPE);
                _orderItemManager.AddLogItem(nodeId, "LEVERERAD", "Leveranstyp: " + delivery, eventId, false, false);
                _orderItemManager.AddLogItem(nodeId, "LOG", logEntry, eventId, false, false);
                _orderItemManager.SetStatus(nodeId, "13:Transport", eventId);

                json.Success = true;
                json.Message = "Saved provider data.";
            }
            catch (Exception e)
            {
                json.Success = false;
                json.Message = "Error: " + e.Message;
            }

            return Json(json);
        }

        /// <summary>
        /// Log message, set delivered status, log delivery type and send mail to user.
        /// </summary>
        /// <param name="packJson">The serialized object of type DeliveryByMailPackage.</param>
        /// <returns>JSON result</returns>
        [HttpPost]
        public ActionResult SetArticleAvailableForPickup(string packJson)
        {
            var res = new ResultResponse();

            try
            {
                var pack = JsonConvert.DeserializeObject<ArticleDelivered>(packJson);

                var eventId = _orderItemManager.GenerateEventId(EVENT_TYPE);
                _orderItemManager.AddLogItem(pack.nodeId, "LEVERERAD", "Leveranstyp: Avhämtas i lånedisken.", eventId, false, false);
                _orderItemManager.AddLogItem(pack.nodeId, "LOG", pack.logEntry, eventId, false, false);
                _orderItemManager.SetStatus(pack.nodeId, "05:Levererad", eventId, false, false);

                // We save everything here first so that we get the new values injected into the message by the template service.
                _orderItemManager.SetPatronEmail(pack.nodeId, pack.mail.recipientEmail, eventId);

                // Overwrite the message with message from template service so that we get the new values injected.
                pack.mail.message = _templateService.ReplaceMoustaches("ArticleAvailableInInfodiskMailTemplate", 
                    pack.mail.message, _orderItemManager.GetOrderItem(pack.nodeId));

                _mailService.SendMail(pack.mail);
                _orderItemManager.AddLogItem(pack.nodeId, "MAIL_NOTE", "Skickat mail till " + pack.mail.recipientEmail, eventId, false, false);
                _orderItemManager.AddLogItem(pack.nodeId, "MAIL", pack.mail.message, eventId);

                res.Success = true;
                res.Message = "Lyckades leverera.";
            }
            catch (Exception e)
            {
                res.Success = false;
                res.Message = "Fel vid leveransförsök: " + e.Message;
            }

            return Json(res);
        }

        /// <summary>
        /// Set delivered status, log delivery type and send mail to user.
        /// </summary>
        /// <param name="nodeId">The node ID for the order item in question.</param>
        /// <returns>JSON result</returns>
        [HttpPost]
        public ActionResult SetArticleAvailableForPickupAtBranch(int nodeId)
        {
            var res = new ResultResponse();

            try
            {
                var orderItem = _orderItemManager.GetOrderItem(nodeId);

                ArticleDelivered pack = new ArticleDelivered();
                pack.mail = new OutgoingMailModel(orderItem.OrderId, orderItem.PatronName, orderItem.PatronEmail);
                pack.nodeId = nodeId;

                var eventId = _orderItemManager.GenerateEventId(EVENT_TYPE);
                _orderItemManager.AddLogItem(pack.nodeId, "LEVERERAD", "Leveranstyp: Avhämtas i lånedisken.", eventId, false, false);
                _orderItemManager.SetStatus(pack.nodeId, "05:Levererad", eventId, false, false);

                // We save everything here first so that we get the new values injected into the message by the template service.
                _orderItemManager.SetPatronEmail(pack.nodeId, pack.mail.recipientEmail, eventId);

                // Overwrite the message with message from template service so that we get the new values injected.
                pack.mail.message = _templateService.GetTemplateData("ArticleAvailableInInfodiskMailTemplate", _orderItemManager.GetOrderItem(pack.nodeId));

                _mailService.SendMail(pack.mail);
                _orderItemManager.AddLogItem(pack.nodeId, "MAIL_NOTE", "Skickat mail till " + pack.mail.recipientEmail, eventId, false, false);
                _orderItemManager.AddLogItem(pack.nodeId, "MAIL", pack.mail.message, eventId);

                res.Success = true;
                res.Message = "Lyckades leverera.";
            }
            catch (Exception e)
            {
                res.Success = false;
                res.Message = "Fel vid leveransförsök: " + e.Message;
            }

            return Json(res);
        }

        /// <summary>
        /// Log message, set delivered status, log delivery type and send mail to user.
        /// </summary>
        /// <param name="packJson">The serialized object of type DeliveryByMailPackage.</param>
        /// <returns>JSON result</returns>
        [HttpPost]
        public ActionResult DeliverByMail(string packJson)
        {
            var res = new ResultResponse();

            try
            {
                var pack = JsonConvert.DeserializeObject<DeliverByMailPackage>(packJson);

                var eventId = _orderItemManager.GenerateEventId(EVENT_TYPE);
                _orderItemManager.AddLogItem(pack.nodeId, "LEVERERAD", "Leveranstyp: Direktleverans via e-post.", eventId, false, false);
                _orderItemManager.AddLogItem(pack.nodeId, "LOG", pack.logEntry, eventId, false, false);
                _orderItemManager.SetStatus(pack.nodeId, "05:Levererad", eventId, false, false);

                // We save everything here first so that we get the new values injected into the message by the template service.
                _orderItemManager.SetPatronEmail(pack.nodeId, pack.mail.recipientEmail, eventId);

                // Overwrite the message with message from template service so that we get the new values injected.
                pack.mail.message = _templateService.ReplaceMoustaches("ArticleDeliveryByMailTemplate", 
                    pack.mail.message, _orderItemManager.GetOrderItem(pack.nodeId));

                _mailService.SendMail(pack.mail);
                _orderItemManager.AddLogItem(pack.nodeId, "MAIL_NOTE", "Skickat mail till " + pack.mail.recipientEmail, eventId, false, false);
                _orderItemManager.AddLogItem(pack.nodeId, "MAIL", pack.mail.message, eventId);

                res.Success = true;
                res.Message = "Lyckades leverera via mail.";
            }
            catch (Exception e)
            {
                res.Success = false;
                res.Message = "Fel vid leveransförsök via mail: " + e.Message;
            }

            return Json(res);
        }

        /// <summary>
        /// Log message, set delivered status, log delivery type and send mail to user.
        /// </summary>
        /// <param name="packJson">The serialized object of type DeliveryByMailPackage.</param>
        /// <returns>JSON result</returns>
        [HttpPost]
        public ActionResult DeliverByPost(string packJson)
        {
            var res = new ResultResponse();

            try
            {
                var pack = JsonConvert.DeserializeObject<ArticleDelivered>(packJson);
                RegisterArticleDeliveryAndSendMail(pack, "post", "ArticleDeliveryByPostTemplate", res);
            }
            catch (Exception e)
            {
                res.Success = false;
                res.Message = "Fel vid leveransförsök via post: " + e.Message;
            }

            return Json(res);
        }

        /// <summary>
        /// Log message, set delivered status, log delivery type and send mail to user.
        /// </summary>
        /// <param name="packJson">The serialized object of type DeliveryByMailPackage.</param>
        /// <returns>JSON result</returns>
        [HttpPost]
        public ActionResult DeliverByInternpost(string packJson)
        {
            var res = new ResultResponse();

            try
            {
                var pack = JsonConvert.DeserializeObject<ArticleDelivered>(packJson);
                RegisterArticleDeliveryAndSendMail(pack, "internpost", "ArticleDeliveryByInternpostTemplate", res);
            }
            catch (Exception e)
            {
                res.Success = false;
                res.Message = "Fel vid leveransförsök via internpost: " + e.Message;
            }

            return Json(res);
        }

        public class DeliverByMailPackage
        {
            public int nodeId { get; set; }
            public string logEntry { get; set; }
            public OutgoingMailModel mail { get; set; }
        }

        public class ArticleDelivered
        {
            public int nodeId { get; set; }
            public OutgoingMailModel mail { get; set; }
            public string logEntry { get; set; }
        }

        private ResultResponse RegisterArticleDeliveryAndSendMail(ArticleDelivered pack, string deliveryType, string mailTemplateName, /* out */ ResultResponse res)
        {
            var eventId = _orderItemManager.GenerateEventId(EVENT_TYPE);
            _orderItemManager.AddLogItem(pack.nodeId, "LEVERERAD", "Leveranstyp: " + deliveryType, eventId, false, false);
            _orderItemManager.AddLogItem(pack.nodeId, "LOG", pack.logEntry, eventId, false, false);
            _orderItemManager.SetStatus(pack.nodeId, "05:Levererad", eventId, false, false);

            // We save everything here first so that we get the new values injected into the message by the template service.
            _orderItemManager.SetPatronEmail(pack.nodeId, pack.mail.recipientEmail, eventId);
           
            // Overwrite the message with message from template service so that we get the new values injected.
            pack.mail.message = _templateService.ReplaceMoustaches("ArticleDeliveryByPostTemplate",
                pack.mail.message, _orderItemManager.GetOrderItem(pack.nodeId));

            _mailService.SendMail(pack.mail);
            _orderItemManager.AddLogItem(pack.nodeId, "MAIL_NOTE", "Skickat mail till " + pack.mail.recipientEmail, eventId, false, false);
            _orderItemManager.AddLogItem(pack.nodeId, "MAIL", pack.mail.message, eventId);

            res.Success = true;
            res.Message = "Lyckades leverera via mail.";

            return res;
        }
    }
}