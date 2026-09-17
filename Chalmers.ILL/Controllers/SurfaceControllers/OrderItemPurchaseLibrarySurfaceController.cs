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
using static Chalmers.ILL.Models.OrderItemModel;

namespace Chalmers.ILL.Controllers.SurfaceControllers
{
    [Authorize]
    public class OrderItemPurchaseLibrarySurfaceController : Controller
    {
        public static int EVENT_TYPE { get { return 28; } }

        IOrderItemManager _orderItemManager;

        public OrderItemPurchaseLibrarySurfaceController(IOrderItemManager orderItemManager)
        {
            _orderItemManager = orderItemManager;
        }

        /// <summary>
        /// Set purchase library property for OrderItem
        /// </summary>
        /// <param name="orderNodeId">OrderItem Node Id</param>
        /// <param name="purchaseLibraryId">Purchase library</param>
        /// <returns>MVC ActionResult with JSON</returns>
        [HttpGet]
        public ActionResult SetOrderItemPurchaseLibrary(int orderNodeId, PurchaseLibraries purchaseLibrary)
        {
            var json = new ResultResponse();

            try
            {
                var eventId = _orderItemManager.GenerateEventId(EVENT_TYPE);

                // Use internal method to set status property and log the result
                _orderItemManager.SetPurchaseLibrary(orderNodeId, purchaseLibrary, eventId);

                // Construct JSON response for client (ie jQuery/getJSON)
                json.Success = true;
                json.Message = "Changed purchase library to " + purchaseLibrary.ToString();
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
