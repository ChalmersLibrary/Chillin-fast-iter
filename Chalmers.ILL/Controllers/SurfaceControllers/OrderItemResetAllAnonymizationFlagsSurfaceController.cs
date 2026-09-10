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
    public class OrderItemResetAllAnonymizationFlagsSurfaceController : Controller
    {
        public static int RESET_ALL_ANONYMIZATION_FLAGS_EVENT_TYPE { get { return 33; } }

        IOrderItemManager _orderItemManager;

        public OrderItemResetAllAnonymizationFlagsSurfaceController(IOrderItemManager orderItemManager)
        {
            _orderItemManager = orderItemManager;
        }

        [HttpPost, ValidateInput(false)]
        public ActionResult Reset(int nodeId)
        {
            var json = new ResultResponse();

            try
            {
                var eventId = _orderItemManager.GenerateEventId(RESET_ALL_ANONYMIZATION_FLAGS_EVENT_TYPE);
                _orderItemManager.ResetAllAnonymizationFlags(nodeId, eventId, false, false);
                _orderItemManager.AddLogItem(nodeId, "ANONYMISERING", "Anonymiseringsflaggor återställda.", eventId);

                // Construct JSON response for client (ie jQuery/getJSON)
                json.Success = true;
                json.Message = "Reset.";
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
