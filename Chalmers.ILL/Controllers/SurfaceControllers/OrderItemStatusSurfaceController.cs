using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.ComponentModel.DataAnnotations;
using Chalmers.ILL.Models;
using Chalmers.ILL.Utilities;
using Chalmers.ILL.OrderItems;

namespace Chalmers.ILL.Controllers.SurfaceControllers
{

    [Authorize]
    public class OrderItemStatusSurfaceController : Controller
    {
        public static int EVENT_TYPE { get { return 2; } }

        IOrderItemManager _orderItemManager;

        public OrderItemStatusSurfaceController(IOrderItemManager orderItemManager)
        {
            _orderItemManager = orderItemManager;
        }

        /// <summary>
        /// Set status property for OrderItem
        /// </summary>
        /// <param name="orderNodeId">OrderItem Node Id</param>
        /// <param name="statusId">Status property DataType Id</param>
        /// <returns>MVC ActionResult with JSON</returns>
        [HttpGet]
        public ActionResult SetOrderItemStatus(int orderNodeId, int statusId, int cancellationReasonId = -1, int purchasedMaterialId = -1)
        {
            var json = new ResultResponse();

            try 
	        {
                var eventId = _orderItemManager.GenerateEventId(EVENT_TYPE);

                if (cancellationReasonId != -1)
                {
                    _orderItemManager.SetCancellationReason(orderNodeId, cancellationReasonId, eventId, false, false);
                }

                if (purchasedMaterialId != -1)
                {
                    _orderItemManager.SetPurchasedMaterial(orderNodeId, purchasedMaterialId, eventId, false, false);
                }

                // Use internal method to set status property and log the result
                _orderItemManager.SetStatus(orderNodeId, statusId, eventId);

                // Construct JSON response for client (ie jQuery/getJSON)
                json.Success = true;
                json.Message = "Changed status to " + statusId;
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