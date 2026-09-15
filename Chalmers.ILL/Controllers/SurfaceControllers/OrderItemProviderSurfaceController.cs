using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Chalmers.ILL.Models;
using Chalmers.ILL.Utilities;
using Chalmers.ILL.Extensions;
using System.Configuration;
using Chalmers.ILL.OrderItems;
using Chalmers.ILL.Models.PartialPage;
using Chalmers.ILL.Providers;

namespace Chalmers.ILL.Controllers.SurfaceControllers
{

    [Authorize]
    public class OrderItemProviderSurfaceController : Controller
    {
        public static int EVENT_TYPE { get { return 7; } }
        public static int PROVIDER_DATA_UPDATED_EVENT_TYPE { get { return 27; } }

        IOrderItemManager _orderItemManager;
        IProviderService _providerService;

        public OrderItemProviderSurfaceController(IOrderItemManager orderItemManager, IProviderService providerService)
        {
            _orderItemManager = orderItemManager;
            _providerService = providerService;
        }

        [HttpGet]
        public ActionResult RenderProviderAction(int nodeId)
        {
            // Get a new OrderItem populated with values for this node
            var pageModel = new ChalmersILLActionProviderModel(_orderItemManager.GetOrderItem(nodeId));

            pageModel.Providers = _providerService.FetchAndCreateListOfUsedProviders().ToList();
            var deliveryTimeInHours = _providerService.GetSuggestedDeliveryTimeInHoursForProvider(pageModel.OrderItem.ProviderName);
            pageModel.EstimatedDeliveryCurrentProvider = DateTime.Now.AddHours(deliveryTimeInHours);

            // The return format depends on the client's Accept-header
            return PartialView("Chalmers.ILL.Action.Provider", pageModel);
        }

        [HttpGet]
        public ActionResult SetProvider(int nodeId, string providerName, string providerOrderId, string providerInformation, string newFollowUpDate, bool updateStatusAndFollowUpDate = true)
        {
            var json = new ResultResponse();

            try
            {
                var eventType = PROVIDER_DATA_UPDATED_EVENT_TYPE;
                if (updateStatusAndFollowUpDate)
                {
                    eventType = EVENT_TYPE;
                }

                var eventId = _orderItemManager.GenerateEventId(eventType);
                if (updateStatusAndFollowUpDate)
                {
                    _orderItemManager.SetFollowUpDate(nodeId, Convert.ToDateTime(newFollowUpDate), eventId, false, false);
                }

                _orderItemManager.SetProviderName(nodeId, providerName, eventId, false, false);
                _orderItemManager.SetProviderOrderId(nodeId, providerOrderId, eventId, false, false);
                _orderItemManager.SetProviderInformation(nodeId, providerInformation, eventId, false, false);

                if (updateStatusAndFollowUpDate)
                {
                    _orderItemManager.SetStatus(nodeId, "03:Beställd", eventId, false, false);
                }

                _orderItemManager.SaveWithoutEventsAndWithSynchronousReindexing(nodeId);

                json.Success = true;
                json.Message = "Sparade data för beställning.";
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