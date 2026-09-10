using Chalmers.ILL.Models;
using Chalmers.ILL.OrderItems;
using Nest;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;

namespace Chalmers.ILL.Controllers.SurfaceControllers
{
    [Authorize]
    public class OrderItemAnonymizationSurfaceController : Controller
    {
        public static int MANUAL_ANONYMIZATION_EVENT_TYPE { get { return 31; } }

        IOrderItemManager _orderItemManager;

        public OrderItemAnonymizationSurfaceController(IOrderItemManager orderItemManager)
        {
            _orderItemManager = orderItemManager;
        }

        [HttpGet]
        public ActionResult RenderAnonymizeAction(int nodeId)
        {
            // Get a new OrderItem populated with values for this node
            var orderItem = _orderItemManager.GetOrderItem(nodeId);
            
            // The return format depends on the client's Accept-header
            return PartialView("Chalmers.ILL.Action.Anonymize", orderItem);
        }

        [HttpPost, ValidateInput(false)]
        public ActionResult Anonymize(int nodeId, string reference, string logsSerialized)
        {
            var json = new ResultResponse();

            try
            {
                var logs = JsonConvert.DeserializeObject<List<LogItem>>(logsSerialized);

                var eventId = _orderItemManager.GenerateEventId(MANUAL_ANONYMIZATION_EVENT_TYPE);
                _orderItemManager.SilentAnonymization(nodeId, reference, logs, eventId, false, false);
                _orderItemManager.AddLogItem(nodeId, "ANONYMISERING", "Manuell anonymisering av order.", eventId);

                // Construct JSON response for client (ie jQuery/getJSON)
                json.Success = true;
                json.Message = "Anonymized.";
            }
            catch (Exception e)
            {
                json.Success = false;
                json.Message = "Error: " + e.Message;
            }

            return Json(json, JsonRequestBehavior.AllowGet);
        }

        [HttpPost]
        public ActionResult SetIsAnonymizedOnMultiple(int[] nodeIds, bool isAnonymized)
        {
            var json = new ResultResponse();

            try
            {
                foreach (var nodeId in nodeIds)
                {
                    var eventId = _orderItemManager.GenerateEventId(MANUAL_ANONYMIZATION_EVENT_TYPE);
                    _orderItemManager.SetIsAnonymized(nodeId, isAnonymized, eventId);
                }

                // Construct JSON response for client (ie jQuery/getJSON)
                json.Success = true;
                json.Message = "Anonymized multiple.";
            }
            catch (Exception e)
            {
                json.Success = false;
                json.Message = "Error: " + e.Message;
            }

            return Json(json, JsonRequestBehavior.AllowGet);
        }
    }
}
