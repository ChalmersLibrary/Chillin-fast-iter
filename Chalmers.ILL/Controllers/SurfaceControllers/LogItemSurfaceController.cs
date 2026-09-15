using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.ComponentModel.DataAnnotations;
using Chalmers.ILL.Models;
using Chalmers.ILL.Models.PartialPage;
using System.Globalization;
using Chalmers.ILL.Utilities;
using Chalmers.ILL.OrderItems;
using Chalmers.ILL.UmbracoApi;

namespace Chalmers.ILL.Controllers.SurfaceControllers
{
    [Authorize]
    public class LogItemSurfaceController : Controller
    {
        public static int EVENT_TYPE { get { return 8; } }

        IOrderItemManager _orderItemManager;
        IChillinOrderConfiguration _orderConfig;

        public LogItemSurfaceController(IOrderItemManager orderItemManager, IChillinOrderConfiguration orderConfig)
        {
            _orderItemManager = orderItemManager;
            _orderConfig = orderConfig;
        }

        /// <summary>
        /// Render the Partial View for logging
        /// </summary>
        /// <param name="nodeId">OrderItem Node Id</param>
        /// <returns>Partial View</returns>
        [HttpGet]
        public ActionResult RenderLogEntryAction(int nodeId)
        {
            var pageModel = new ChalmersILLActionLogEntryModel(_orderItemManager.GetOrderItem(nodeId));

            _orderConfig.PopulateModelWithAvailableValues(pageModel);

            // The return format depends on the client's Accept-header
            return PartialView("Chalmers.ILL.Action.LogEntry", pageModel);
        }

        /// <summary>
        /// Get LogItems for OrderItem and return a rendered Partial View
        /// </summary>
        /// <param name="nodeId">OrderItem</param>
        /// <returns>Partial View</returns>
        [HttpGet]
        public ActionResult GetLogItemsAsPartial(int nodeId)
        {
            // Call internal method to return List of LogItems for this OrderItem nodeId
            var logItems = _orderItemManager.GetLogItems(nodeId);

            // Return Partial View for LogItems bound to Model with LogItems
            return PartialView("Chalmers.ILL.LogItem", logItems);
        }

        /// <summary>
        /// Get LogItems for OrderItem and return a JsonResult
        /// </summary>
        /// <param name="nodeId">OrderItem</param>
        /// <returns>Json Result</returns>
        public JsonResult GetLogItems(int nodeId)
        {
            // The list of log entries to return binds to the model
            var logItems = _orderItemManager.GetLogItems(nodeId);

            // Return Json Result
            return Json(logItems);
        }

        /// <summary>
        /// Write LogItem without model binding
        /// </summary>
        /// <param name="OrderItemNodeId"></param>
        /// <param name="Type"></param>
        /// <param name="Message"></param>
        /// <returns></returns>
        [HttpPost]
        public ActionResult WriteLogItem(int nodeId, string Type, string Message, string newFollowUpDate, int statusId, int cancellationReasonId, int purchasedMaterialId)
        {
            // Json response
            var json = new ResultResponse();

            try
            {
                var eventId = _orderItemManager.GenerateEventId(EVENT_TYPE);

                // Set FollowUpDate property if it differs from current
                DateTime currentFollowUpDate = _orderItemManager.GetOrderItem(nodeId).FollowUpDate;

                if (!String.IsNullOrEmpty(newFollowUpDate))
                {
                    DateTime parsedNewFollowUpDate = Convert.ToDateTime(newFollowUpDate);
                    if (currentFollowUpDate != parsedNewFollowUpDate)
                    {
                        _orderItemManager.SetFollowUpDate(nodeId, parsedNewFollowUpDate, eventId, false, false);
                    }
                }

                if (statusId != -1)
                {
                    _orderItemManager.SetStatus(nodeId, statusId, eventId, false, false);
                }

                if (cancellationReasonId != -1)
                {
                    _orderItemManager.SetCancellationReason(nodeId, cancellationReasonId, eventId, false, false);
                }

                if (purchasedMaterialId != -1)
                {
                    _orderItemManager.SetPurchasedMaterial(nodeId, purchasedMaterialId, eventId, false, false);
                }

                // Use internal method to set type property and log the result
                _orderItemManager.AddLogItem(nodeId, Type, Message, eventId);

                // Construct JSON response for client (ie jQuery/getJSON)
                json.Success = true;
                json.Message = "Wrote log entry to node" + nodeId;
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
