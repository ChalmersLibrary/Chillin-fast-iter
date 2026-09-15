using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Chalmers.ILL.Models;
using Chalmers.ILL.Utilities;
using Chalmers.ILL.Extensions;
using Chalmers.ILL.OrderItems;

namespace Chalmers.ILL.Controllers.SurfaceControllers
{

    [Authorize]
    public class OrderItemReferenceSurfaceController : Controller
    {
        public static int EVENT_TYPE { get { return 4; } }

        IOrderItemManager _orderItemManager;

        public OrderItemReferenceSurfaceController(IOrderItemManager orderItemManager)
        {
            _orderItemManager = orderItemManager;
        }

        [HttpGet]
        public ActionResult RenderReferenceAction(int nodeId)
        {
            // Get a new OrderItem populated with values for this node
            var orderItem = _orderItemManager.GetOrderItem(nodeId);

            // The return format depends on the client's Accept-header
            return PartialView("Chalmers.ILL.Action.Reference", orderItem);
        }

        [HttpPost]
        public ActionResult SetReference(int nodeId, string reference)
        {
            var json = new ResultResponse();

            try
            {
                // Find OrderItem
                var orderItem = _orderItemManager.GetOrderItem(nodeId);

                _orderItemManager.SetReference(nodeId, reference, _orderItemManager.GenerateEventId(EVENT_TYPE), false, false);

                // Construct JSON response for client (ie jQuery/getJSON)
                json.Success = true;
                json.Message = "Saved reference.";
            }
            catch (Exception e)
            {
                json.Success = false;
                json.Message = "Error: " + e.Message;
            }

            return Json(json);
        }

    }
}