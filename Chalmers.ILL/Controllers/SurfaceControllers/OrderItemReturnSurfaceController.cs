using Chalmers.ILL.Models;
using Chalmers.ILL.Models.PartialPage;
using Chalmers.ILL.OrderItems;
using Chalmers.ILL.Services;
using Chalmers.ILL.UmbracoApi;
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace Chalmers.ILL.Controllers.SurfaceControllers
{
    [Authorize]
    public class OrderItemReturnSurfaceController : Controller
    {
        public static int BOOK_RETURNED_HOME_EVENT_TYPE { get { return 14; } }

        IOrderItemManager _orderItemManager;
        IChillinOrderConfiguration _orderConfig;

        private readonly IFolioService _folioService;
        private const int STATUS_FOLIO = 17;

        public OrderItemReturnSurfaceController(IOrderItemManager orderItemManager, IChillinOrderConfiguration orderConfig, IFolioService folioService)
        {
            _orderItemManager = orderItemManager;
            _orderConfig = orderConfig;
            _folioService = folioService;
        }

        /// <summary>
        /// Render the Partial View for returning a book to its library
        /// </summary>
        /// <param name="nodeId">OrderItem Node Id</param>
        /// <returns>Partial View</returns>
        [HttpGet]
        public ActionResult RenderReturnAction(int nodeId)
        {
            var pageModel = new ChalmersILLActionReturnModel(_orderItemManager.GetOrderItem(nodeId));

            _orderConfig.PopulateModelWithAvailableValues(pageModel);

            // The return format depends on the client's Accept-header
            return PartialView("Chalmers.ILL.Action.Return", pageModel);
        }

        [HttpPost]
        public ActionResult ReturnItem(int nodeId, string bookId, int status)
        {
            var json = new ResultResponse();

            try
            {
                if (status == STATUS_FOLIO)
                {
                    _folioService.SetItemToWithdrawn(bookId);
                }
                var eventId = _orderItemManager.GenerateEventId(BOOK_RETURNED_HOME_EVENT_TYPE);
                _orderItemManager.SetStatus(nodeId, "10:Återsänd", eventId);

                json.Success = true;
                json.Message = "Returnering genomförd.";
            }
            catch (Exception e)
            {
                json.Success = false;
                json.Message = "Misslyckades med att returnera: " + e.Message;
            }

            return Json(json);
        }
    }
}
