using Chalmers.ILL.Models;
using Chalmers.ILL.OrderItems;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using Microsoft.AspNetCore.Mvc;

namespace Chalmers.ILL.Controllers.SurfaceControllers
{
    public class OrderItemSearchSurfaceController : Controller
    {
        private static readonly log4net.ILog _log = log4net.LogManager.GetLogger(typeof(OrderItemSearchSurfaceController));

        private IOrderItemSearcher _orderItemSearcher;
        private IOrderItemManager _orderItemManager;

        public OrderItemSearchSurfaceController(IOrderItemSearcher orderItemSearcher, IOrderItemManager orderItemManager)
        {
            _orderItemSearcher = orderItemSearcher;
            _orderItemManager = orderItemManager;
        }

        [HttpPost]
        public ActionResult ReindexOrderItem(string orderId)
        {
            var json = new ResultResponse();

            try
            {
                var orderItem = _orderItemManager.GetOrderItem(orderId);

                if (orderItem != null)
                {
                    _orderItemSearcher.Modified(orderItem);

                    json.Success = true;
                    json.Message = "Indexerade order item '" + orderId + "' i sökindexet.";
                }
                else
                {
                    json.Success = false;
                    json.Message = "Kunde inte hitta någon order item med '" + orderId + "' som orderId och ingen indexering har därför genomförts.";
                }
            }
            catch (Exception e)
            {
                _log.Error("Failed to reindex order item.", e);
                json.Success = false;
                json.Message = "Error: " + e.Message;
            }

            return Json(json);
        }
    }
}