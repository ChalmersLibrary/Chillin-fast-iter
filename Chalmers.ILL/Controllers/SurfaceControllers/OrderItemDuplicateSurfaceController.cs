using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.ComponentModel.DataAnnotations;
using Chalmers.ILL.Models;
using Chalmers.ILL.Utilities;
using Chalmers.ILL.OrderItems;

namespace Chalmers.ILL.Controllers.SurfaceControllers
{

    [Authorize]
    public class OrderItemDuplicateSurfaceController : Controller
    {
        public static int MAKE_DUPLICATE_EVENT_TYPE { get { return 32; } }

        IOrderItemManager _orderItemManager;

        public OrderItemDuplicateSurfaceController(IOrderItemManager orderItemManager)
        {
            _orderItemManager = orderItemManager;
        }

        [HttpPost]
        public ActionResult MakeDuplicate(int orderNodeId)
        {
            var json = new ResultResponse();

            try
            {
                var eventId = _orderItemManager.GenerateEventId(MAKE_DUPLICATE_EVENT_TYPE);

                // Use internal method to make duplicate and log the result
                _orderItemManager.MakeDuplicate(orderNodeId, eventId);

                // Construct JSON response for client (ie jQuery/getJSON)
                json.Success = true;
                json.Message = "Created duplicate of order " + orderNodeId;
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