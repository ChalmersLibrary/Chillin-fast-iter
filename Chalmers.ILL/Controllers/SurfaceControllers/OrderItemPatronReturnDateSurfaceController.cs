using Chalmers.ILL.Mail;
using Chalmers.ILL.Models;
using Chalmers.ILL.Models.Mail;
using Chalmers.ILL.Models.PartialPage;
using Chalmers.ILL.OrderItems;
using Chalmers.ILL.Templates;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace Chalmers.ILL.Controllers.SurfaceControllers
{
    [Authorize]
    public class OrderItemPatronReturnDateSurfaceController : Controller
    {
        public static int EVENT_TYPE { get { return 11; } }

        IOrderItemManager _orderItemManager;
        ITemplateService _templateService;
        IMailService _mailService;

        public OrderItemPatronReturnDateSurfaceController(IOrderItemManager orderItemManager, ITemplateService templateService, IMailService mailService)
        {
            _orderItemManager = orderItemManager;
            _templateService = templateService;
            _mailService = mailService;
        }

        /// <summary>
        /// Render the Partial View for changing the return date against the patron.
        /// </summary>
        /// <param name="nodeId">OrderItem Node Id</param>
        /// <returns>Partial View</returns>
        [HttpGet]
        public ActionResult RenderPatronReturnDateAction(int nodeId)
        {
            var pageModel = new ChalmersILLActionPatronReturnDateModel(_orderItemManager.GetOrderItem(nodeId));

            pageModel.ReturnDateChangedMailTemplate = _templateService.GetTemplateData("ReturnDateChangedMailTemplate", pageModel.OrderItem);

            // The return format depends on the client's Accept-header
            return PartialView("Chalmers.ILL.Action.PatronReturnDate", pageModel);
        }

        [HttpPost]
        public ActionResult ChangeReturnDate(string packJson)
        {
            var json = new ResultResponse();

            try
            {
                var pack = JsonConvert.DeserializeObject<ChangeReturnDatePackage>(packJson);

                var orderItem = _orderItemManager.GetOrderItem(pack.nodeId);

                var eventId = _orderItemManager.GenerateEventId(EVENT_TYPE);

                if (pack.logMsg != "")
                {
                    _orderItemManager.AddLogItem(pack.nodeId, "LOG", pack.logMsg, eventId, false, false);
                }

                if (orderItem.LastDeliveryStatusId != -1)
                {
                    _orderItemManager.SetStatus(pack.nodeId, orderItem.LastDeliveryStatusId, eventId, false, false);
                }
                _orderItemManager.SetDueDate(pack.nodeId, pack.dueDate, eventId, false, false);
                _orderItemManager.SetProviderDueDate(pack.nodeId, pack.dueDate, eventId, false, false);

                // We save everything here first so that we get the new values injected into the message by the template service.
                _orderItemManager.SetPatronEmail(pack.nodeId, pack.mail.recipientEmail, eventId);

                // Overwrite the message with message from template service so that we get the new values injected.
                pack.mail.message = _templateService.GetTemplateData("ReturnDateChangedMailTemplate", _orderItemManager.GetOrderItem(pack.nodeId));

                _mailService.SendMail(pack.mail);
                _orderItemManager.AddLogItem(pack.nodeId, "MAIL_NOTE", "Skickat mail till " + pack.mail.recipientEmail, eventId, false, false);
                _orderItemManager.AddLogItem(pack.nodeId, "MAIL", pack.mail.message, eventId);

                json.Success = true;
                json.Message = "Återlämningsdatum mot låntagare ändrat.";
            }
            catch (Exception e)
            {
                json.Success = false;
                json.Message = "Misslyckades med att ändra återlämningsdatum mot låntagare: " + e.Message;
            }

            return Json(json);
        }

        public class ChangeReturnDatePackage
        {
            public int nodeId;
            public string logMsg;
            public DateTime dueDate;
            public OutgoingMailModel mail;
        }
    }
}
