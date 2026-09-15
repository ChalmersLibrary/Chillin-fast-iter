using Chalmers.ILL.Models;
using Chalmers.ILL.OrderItems;
using System;
using Microsoft.AspNetCore.Mvc;

namespace Chalmers.ILL.Controllers.SurfaceControllers
{
    public class BookCirculationSurfaceController : Controller
    {
        public static int BOOK_RETURNED_FROM_BORROWER_EVENT_TYPE { get { return 23; } }
        public static int BOOK_LOANED_EVENT_TYPE { get { return 24; } }

        IOrderItemManager _orderItemManager;

        public BookCirculationSurfaceController(IOrderItemManager orderItemManager)
        {
            _orderItemManager = orderItemManager;
        }

        [HttpPost]
        public ActionResult Returned(int nodeId)
        {
            var json = new ResultResponse();

            try
            {
                var eventId = _orderItemManager.GenerateEventId(BOOK_RETURNED_FROM_BORROWER_EVENT_TYPE);
                _orderItemManager.SetStatus(nodeId, "13:Transport", eventId);

                json.Success = true;
                json.Message = "Lyckades med att återlämna bok till filial.";
            }
            catch (Exception e)
            {
                json.Success = false;
                json.Message = "Misslyckades med att returnera från filial: " + e.Message;
            }

            return Json(json);
        }

        [HttpPost]
        public ActionResult Loaned(int nodeId)
        {
            var json = new ResultResponse();

            try
            {
                var eventId = _orderItemManager.GenerateEventId(BOOK_LOANED_EVENT_TYPE);
                _orderItemManager.SetStatus(nodeId, "11:Utlånad", eventId);

                json.Success = true;
                json.Message = "Lyckades med att låna ut bok.";
            }
            catch (Exception e)
            {
                json.Success = false;
                json.Message = "Misslyckades med att låna ut bok: " + e.Message;
            }

            return Json(json);
        }
    }
}