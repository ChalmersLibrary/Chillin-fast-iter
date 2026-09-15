using Chalmers.ILL.Mail;
using Chalmers.ILL.Models;
using Chalmers.ILL.Models.Mail;
using Chalmers.ILL.Models.PartialPage;
using Chalmers.ILL.OrderItems;
using Chalmers.ILL.Templates;
using Chalmers.ILL.Utilities;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace Chalmers.ILL.Controllers.SurfaceControllers
{
    [Authorize]
    public class OrderItemClaimSurfaceController : Controller
    {
        public static int EVENT_TYPE { get { return 13; } }

        IOrderItemManager _orderItemManager;
        ITemplateService _templateService;
        IMailService _mailService;

        public OrderItemClaimSurfaceController(IOrderItemManager orderItemManager, ITemplateService templateService, IMailService mailService)
        {
            _orderItemManager = orderItemManager;
            _templateService = templateService;
            _mailService = mailService;
        }

        [HttpGet]
        public ActionResult RenderClaimAction(int nodeId)
        {
            var pageModel = new ChalmersILLActionClaimModel(_orderItemManager.GetOrderItem(nodeId));

            pageModel.ClaimBookMailTemplate = _templateService.GetTemplateData("ClaimBookMailTemplate", pageModel.OrderItem);
            pageModel.ClaimDueDate = DateTime.Now > pageModel.OrderItem.DueDate ? pageModel.OrderItem.DueDate : DateTime.Now;

            return PartialView("Chalmers.ILL.Action.Claim", pageModel);
        }

        [HttpPost]
        public ActionResult ClaimItem(string packJson)
        {
            var json = new ResultResponse();

            try
            {
                var pack = JsonConvert.DeserializeObject<ClaimItemPackage>(packJson);

                var eventId = _orderItemManager.GenerateEventId(EVENT_TYPE);
                _orderItemManager.SetDueDate(pack.nodeId, pack.dueDate, eventId, false, false);
                _orderItemManager.SetProviderDueDate(pack.nodeId, pack.dueDate, eventId, false, false);
                _orderItemManager.SetStatus(pack.nodeId, "12:Krävd", eventId, false, false);
                
                // We save everything here first so that we get the new values injected into the message by the template service.
                _orderItemManager.SetPatronEmail(pack.nodeId, pack.mail.recipientEmail, eventId);

                // Overwrite the message with message from template service so that we get the new values injected.
                pack.mail.message = _templateService.GetTemplateData("ClaimBookMailTemplate", _orderItemManager.GetOrderItem(pack.nodeId));

                _mailService.SendMail(pack.mail);
                _orderItemManager.AddLogItem(pack.nodeId, "MAIL_NOTE", "Skickat mail till " + pack.mail.recipientEmail, eventId, false, false);
                _orderItemManager.AddLogItem(pack.nodeId, "MAIL", pack.mail.message, eventId);

                json.Success = true;
                json.Message = "Krav genomfört.";
            }
            catch (Exception e)
            {
                json.Success = false;
                json.Message = "Misslyckades med att kräva: " + e.Message;
            }

            return Json(json);
        }

        public class ClaimItemPackage
        {
            public int nodeId;
            public DateTime dueDate;
            public OutgoingMailModel mail;
        }
    }
}
