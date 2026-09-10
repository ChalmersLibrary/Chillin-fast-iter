using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using Chalmers.ILL.Models;
using Chalmers.ILL.Utilities;
using Chalmers.ILL.Extensions;
using System.Configuration;
using System.Globalization;
using System.Text.RegularExpressions;
using Chalmers.ILL.OrderItems;
using Chalmers.ILL.Mail;
using Chalmers.ILL.Models.Mail;
using Chalmers.ILL.Models.PartialPage;
using Chalmers.ILL.UmbracoApi;
using Chalmers.ILL.Templates;

namespace Chalmers.ILL.Controllers.SurfaceControllers
{
    [Authorize]
    public class OrderItemMailSurfaceController : Controller
    {
        public static int EVENT_TYPE { get { return 6; } }

        private static readonly log4net.ILog _log = log4net.LogManager.GetLogger(typeof(OrderItemMailSurfaceController));

        IOrderItemManager _orderItemManager;
        IExchangeMailWebApi _exchangeMailWebApi;
        IChillinOrderConfiguration _orderConfig;
        IMailService _mailService;
        ITemplateService _templateService;

        public OrderItemMailSurfaceController(IOrderItemManager orderItemManager, IExchangeMailWebApi exchangeMailWebApi,
            IChillinOrderConfiguration orderConfig, IMailService mailService, ITemplateService templateService)
        {
            _orderItemManager = orderItemManager;
            _exchangeMailWebApi = exchangeMailWebApi;
            _orderConfig = orderConfig;
            _mailService = mailService;
            _templateService = templateService;
        }

        /// <summary>
        /// Render the Partial View for sending mail to user from within the system
        /// </summary>
        /// <param name="nodeId">OrderItem Node Id</param>
        /// <returns>Partial View</returns>
        [HttpGet]
        public ActionResult RenderMailAction(int nodeId)
        {
            var model = new ChalmersILLActionMailModel(_orderItemManager.GetOrderItem(nodeId));

            _orderConfig.PopulateModelWithAvailableValues(model);
            model.SignatureTemplate = _templateService.GetTemplateData("SignatureTemplate", model.OrderItem);
            model.Templates = _templateService.GetManualTemplates();

            // The return format depends on the client's Accept-header
            return PartialView("Chalmers.ILL.Action.Mail", model);
        }

        /// <summary>
        /// Send mail to user from within the system, possibly change status and PatronEmail
        /// </summary>
        /// <param name="m">The data for the outgoing mail.</param>
        /// <returns>JSON result</returns>
        [HttpPost, ValidateInput(false)]
        public ActionResult SendMail(OutgoingMailPackageModel m)
        {
            var json = new ResultResponse();

            try
            {
                // Read current values that can be affected
                var orderItem = _orderItemManager.GetOrderItem(m.nodeId);
                var currentPatronEmail = orderItem.PatronEmail;
                var currentStatus = orderItem.Status;

                var eventId = _orderItemManager.GenerateEventId(EVENT_TYPE);

                // Send mail to recipient
                _mailService.SendMail(new OutgoingMailModel(orderItem.OrderId, m));

                _orderItemManager.AddLogItem(m.nodeId, "MAIL_NOTE", "Skickat mail till " + m.recipientEmail, eventId, false, false);
                _orderItemManager.AddLogItem(m.nodeId, "MAIL", m.message, eventId, false, false);

                // Set PatronEmail property if it differs from recipientEmail
                if (currentPatronEmail != m.recipientEmail)
                {
                    _orderItemManager.SetPatronEmail(m.nodeId, m.recipientEmail, eventId, false, false);
                }

                // Set FollowUpDate property if it differs from current
                DateTime currentFollowUpDate = orderItem.FollowUpDate;

                if (!String.IsNullOrEmpty(m.newFollowUpDate))
	            {
                    DateTime parsedNewFollowUpDate = Convert.ToDateTime(m.newFollowUpDate);
                    if (currentFollowUpDate != parsedNewFollowUpDate)
                    {
                        _orderItemManager.SetFollowUpDate(m.nodeId, parsedNewFollowUpDate, eventId, false, false);
                    }
	            }

                // Set status property if it differs from current and if newStatusId is not -1 (no change)
                if (orderItem.StatusId != m.newStatusId && m.newStatusId != -1)
                {
                    _orderItemManager.SetStatus(m.nodeId, m.newStatusId, eventId, false, false);
                }

                // Update cancellation reason if we have a value that is not -1 (no change)
                if (orderItem.CancellationReasonId != m.newCancellationReasonId && m.newCancellationReasonId != -1)
                {
                    _orderItemManager.SetCancellationReason(m.nodeId, m.newCancellationReasonId, eventId, false, false);
                }

                // Update purchased material if we have a value that is not -1 (no change)
                if (orderItem.PurchasedMaterialId != m.newPurchasedMaterialId && m.newPurchasedMaterialId != -1)
                {
                    _orderItemManager.SetPurchasedMaterial(m.nodeId, m.newPurchasedMaterialId, eventId, false, false);
                }

                _orderItemManager.SaveWithoutEventsAndWithSynchronousReindexing(m.nodeId);

                // Construct JSON response for client (ie jQuery/getJSON)
                json.Success = true;
                json.Message = "Sent mail and eventually changed some properties.";
            }
            catch (Exception e)
            {
                json.Success = false;
                json.Message = "Error: " + e.Message;
            }

            return Json(json, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Send mail to create a new order.
        /// </summary>
        /// <remarks>Mainly used for testing purposes.</remarks>
        /// <param name="nodeId">OrderItem Node Id</param>
        /// <param name="recipientEmail">User Email</param>
        /// <param name="recipientName">User Name</param>
        /// <param name="message">Mail message</param>
        /// <param name="newStatus">Change OrderItem status</param>
        /// <returns>JSON result</returns>
        [HttpPost, ValidateInput(false)]
        public ActionResult SendMailForNewOrder(string message, string name, string mail, string libraryCardNumber, string deliveryLibrary)
        {
            var json = new ResultResponse();

            try
            {
                string body = "<div id='chalmers.ill.orderitem'>\n" +
                        "<div id='OriginalOrder'>" + message + "</div>\n" +
                        "<div id='PatronName'>" + name + "</div>\n" +
                        "<div id='PatronEmail'>" + mail + "</div>\n" +
                        "<div id='PatronCardNo'>" + libraryCardNumber + "</div>\n" +
                        "<div id='Purchase'>False</div>\n" +
                        "<div id='DeliveryLibrary'>" + deliveryLibrary + "</div>\n" +
                    "</div>\n";
                _exchangeMailWebApi.ConnectToExchangeService(ConfigurationManager.AppSettings["chalmersIllExhangeLogin"], ConfigurationManager.AppSettings["chalmersIllExhangePass"]);
                _exchangeMailWebApi.SendPlainMailMessage(body, "New request from TEST #new", ConfigurationManager.AppSettings["chalmersIllSenderAddress"]);

                json.Success = true;
                json.Message = "Sent mail for new order.";
            }
            catch (Exception e)
            {
                _log.Error("Error while sending mail for new order.", e);
                json.Success = false;
                json.Message = "Error: " + e.Message;
            }

            return Json(json, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Just send a mail.
        /// </summary>
        /// <param name="m">The data for the outgoing mail.</param>
        /// <returns>JSON result</returns>
        [HttpPost, ValidateInput(false)]
        public ActionResult SendSimpleMail(OutgoingMailModel m)
        {
            var res = new ResultResponse();

            try
            {
                _mailService.SendMail(m);

                res.Success = true;
                res.Message = "Lyckades med att skicka ut mail.";
            }
            catch (Exception e)
            {
                res.Success = false;
                res.Message = "Misslyckades med att skicka ut mail: " + e.Message;
            }

            return Json(res, JsonRequestBehavior.AllowGet);
        }
    }
}