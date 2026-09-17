using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Chalmers.ILL.Models;
using Chalmers.ILL.Utilities;
using Chalmers.ILL.Extensions;
using Chalmers.ILL.OrderItems;
using Chalmers.ILL.Members;
using Newtonsoft.Json;
using System.Configuration;
using Chalmers.ILL.SignalR;
using Chalmers.ILL.UmbracoApi;
using Chalmers.ILL.Models.PartialPage;

namespace Chalmers.ILL.Controllers.SurfaceControllers
{

    [Authorize]
    public class OrderItemSurfaceController : Controller
    {
        private static readonly log4net.ILog _log = log4net.LogManager.GetLogger(typeof(OrderItemSurfaceController));

        IMemberInfoManager _memberInfoManager;
        IOrderItemManager _orderItemManager;
        INotifier _notifier;
        IChillinOrderConfiguration _orderConfig;

        public OrderItemSurfaceController(IMemberInfoManager memberInfoManager, IOrderItemManager orderItemManager,
            INotifier notifier, IChillinOrderConfiguration orderConfig)
        {
            _memberInfoManager = memberInfoManager;
            _orderItemManager = orderItemManager;
            _notifier = notifier;
            _orderConfig = orderConfig;
        }

        public const string lockRelationType = "memberLocked";

        /// <summary>
        /// Get a partial view for an OrderItem, used when opening an OrderItem for editing.
        /// Fetches the data for the order item and also fills in meta data for which member that has the lock on the order item.
        /// </summary>
        /// <param name="nodeId">The OrderItem Node Id</param>
        /// <returns>Partial View html data</returns>
        [HttpGet]
        public ActionResult RenderOrderItem(int nodeId)
        {
            // Get current member.
            int memberId = _memberInfoManager.GetCurrentMemberId(Request, Response);

            // Get a new OrderItem populated with values for this node
            var pageModel = new ChalmersILLOrderItemModel(_orderItemManager.GetOrderItem(nodeId));

            // Check if the current user has the lock for the item.
            pageModel.OrderItem.EditedByCurrentMember = pageModel.OrderItem.EditedBy != "" && pageModel.OrderItem.EditedBy == memberId.ToString();

            _orderConfig.PopulateModelWithAvailableValues(pageModel);

            // Return Partial View to the client
            return PartialView("Chalmers.ILL.OrderItem", pageModel);
        }

        /// <summary>
        /// Get JSON about an OrderItem, useful for updating the list with latest data
        /// </summary>
        /// <param name="nodeId">The OrderItem Node Id</param>
        /// <returns>JSON result</returns>
        [HttpGet]
        public ActionResult GetOrderItem(int nodeId)
        {
            // Get current member.
            int memberId = _memberInfoManager.GetCurrentMemberId(Request, Response);

            // Get a new OrderItem populated with values for this node
            var orderItem = _orderItemManager.GetOrderItem(nodeId);

            // Check if the current user has the lock for the item.
            orderItem.EditedByCurrentMember = orderItem.EditedBy != "" && orderItem.EditedBy == memberId.ToString();

            // Return JSON object to the client to handle
            return Json(orderItem);
        }

        /// <summary>
        /// Take over lock for OrderItem
        /// </summary>
        /// <param name="nodeId">OrderItem Node Id</param>
        /// <returns>JSON result</returns>
        public ActionResult TakeOverLockedOrderItem(int nodeId)
        {
            // Response JSON
            var json = new ResultResponse();

            try
            {
                // Get current member
                int memberId = _memberInfoManager.GetCurrentMemberId(Request, Response);

                var memberText = _memberInfoManager.GetCurrentMemberText(Request, Response);
                _orderItemManager.SetEditedByData(nodeId, memberId.ToString(), memberText);

                // Return JSON to client
                json.Success = true;
                json.Message = "Took over lock.";

                // Notify SignalR clients of the update
                _notifier.UpdateOrderItemUpdate(nodeId, memberId.ToString(), memberText);

            }
            catch (Exception e)
            {
                // Return JSON to client
                json.Success = false;
                json.Message = "Error taking lock: " + e.Message;
            }

            return Json(json);
        }

        /// <summary>
        /// Locks OrderItem to current MemberId
        /// </summary>
        /// <param name="nodeId">OrderItem node id</param>
        /// <returns>JSON result</returns>
        public ActionResult LockOrderItem(int nodeId)
        {
            // Response JSON
            var json = new ResultResponse();

            try
            {
                // Get current member.
                int memberId = _memberInfoManager.GetCurrentMemberId(Request, Response);

                var orderItem = _orderItemManager.GetOrderItem(nodeId);

                if (orderItem.EditedBy != "")
                {
                    // Locked by someone else
                    json.Success = false;
                    json.Message = "OrderItem is already locked by MemberId " + memberId;
                }
                else if (orderItem.EditedBy == "")
                {
                    // Unlocked
                    _orderItemManager.SetEditedByData(nodeId, memberId.ToString(), _memberInfoManager.GetCurrentMemberText(Request, Response));
                    json.Success = true;
                    json.Message = "Order item locked by current member.";
                }

                // Notify SignalR clients of the update
                _notifier.UpdateOrderItemUpdate(nodeId, memberId.ToString(), _memberInfoManager.GetCurrentMemberText(Request, Response));

            }
            catch (Exception e)
            {
                // Return JSON to client
                json.Success = false;
                json.Message = "Error locking OrderItem: " + e.Message;
            }

            return Json(json);
        }

        /// <summary>
        /// Unlocks OrderItem if locked by current MemberId
        /// </summary>
        /// <param name="nodeId"></param>
        /// <returns></returns>
        public ActionResult UnlockOrderItem(int nodeId)
        {
            // Response JSON
            var json = new ResultResponse();

            try
            {
                // Get current member.
                int memberId = _memberInfoManager.GetCurrentMemberId(Request, Response);

                var orderItem = _orderItemManager.GetOrderItem(nodeId);

                if (orderItem.EditedBy == memberId.ToString())
                {
                    _orderItemManager.SetEditedByData(nodeId, "", "");
                    json.Success = true;
                    json.Message = "OrderItem unlocked by Current Member.";
                }
                else if (orderItem.EditedBy == "")
                {
                    json.Success = true;
                    json.Message = "OrderItem unlocked by current member.";
                }

                // Notify SignalR clients of the update
                _notifier.UpdateOrderItemUpdate(nodeId, memberId.ToString(), _memberInfoManager.GetCurrentMemberText(Request, Response));

            }
            catch (Exception e)
            {
                // Return JSON to client
                json.Success = false;
                json.Message = "Error unlocking OrderItem: " + e.Message;
            }

            return Json(json);
        }

        /// <summary>
        /// Get all locked OrderItems for Current Member
        /// </summary>
        /// <returns>JSON result</returns>
        public ActionResult GetLocksForCurrentMember()
        {
            // Response JSON
            var json = new LockResponse();

            try
            {
                // Get current member
                int memberId = _memberInfoManager.GetCurrentMemberId(Request, Response);

                var items = _orderItemManager.GetLockedOrderItems(memberId.ToString());

                // Set response metadata.
                json.MemberId = memberId;
                json.MemberName = _memberInfoManager.GetCurrentMemberText(Request, Response);
                json.List = new List<Lock>();

                foreach (var item in items)
                {
                    var lockItem = new Lock();
                    lockItem.NodeId = item.NodeId;
                    // Missing data for the Lock model, but this data is currently not used.
                    json.List.Add(lockItem);
                }

                json.Message = String.Format("Found {0} locked OrderItems for Current Member.", json.List.Count);

                // Return JSON to client.
                json.Success = true;
            }
            catch (Exception e)
            {
                // Return JSON to client.
                json.Success = false;
                json.Message = "Error reading locked OrderItems: " + e.Message;
                _log.Error("Error reading locked OrderItems", e);
            }

            return Json(json);
        }
    }
}