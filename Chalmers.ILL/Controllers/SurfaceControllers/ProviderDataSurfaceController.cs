using Chalmers.ILL.Models;
using Chalmers.ILL.OrderItems;
using Chalmers.ILL.Providers;
using Chalmers.ILL.Templates;
using Chalmers.ILL.UmbracoApi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace Chalmers.ILL.Controllers.SurfaceControllers
{
    [Authorize]
    public class ProviderDataSurfaceController : Controller
    {
        ITemplateService _templateService;
        IProviderService _providerService;
        IOrderItemSearcher _orderItemsSearcher;
        IOrderItemManager _orderItemManager;

        public ProviderDataSurfaceController(ITemplateService templateService, IProviderService providerService, 
            IOrderItemSearcher orderItemsSearcher, IOrderItemManager orderItemManager)
        {
            _templateService = templateService;
            _providerService = providerService;
            _orderItemsSearcher = orderItemsSearcher;
            _orderItemManager = orderItemManager;
        }

        [HttpGet]
        public ActionResult RenderModifyProviderDataAction()
        {
            var pageModel = new Models.PartialPage.Settings.ModifyProviderData();

            pageModel.Providers = _providerService.FetchAndCreateListOfUsedProviders().ToList();

            return PartialView("Settings/ModifyProviderData", pageModel);
        }

        [HttpGet]
        public ActionResult GetNodeIdsForOrderItemsWithGivenProviderName(string providerName)
        {
            var ids = new List<int>();

            try
            {
                ids = _orderItemsSearcher.Search("providerName:\"" + providerName + "\"")
                    .Where(x => x.ProviderName == providerName)
                    .Select(x => x.NodeId)
                    .ToList();
            }
            catch (Exception)
            {
                // NOP. Just return empty list if we fail, this should be enough indication that something is wrong and needs investigation.
            }

            return Json(ids);
        }

        [HttpGet]
        public ActionResult GetDeliveryTimeInHoursForProvider(string providerName)
        {
            int time = 0;

            try
            {
                time = _providerService.GetSuggestedDeliveryTimeInHoursForProvider(providerName);
            }
            catch (Exception)
            {
                // NOP, fail silently and return zero delivery time.
            }

            return Json(time);
        }

        [HttpPost]
        public ActionResult SetProviderName(int nodeId, string providerName)
        {
            var res = new ResultResponse();

            try
            {
                _orderItemManager.SetProviderNameWithoutLogging(nodeId, providerName, true, false);

                res.Success = true;
                res.Message = "Genomförde ändring av leverantörsnamn på order.";
            }
            catch (Exception e)
            {
                res.Success = false;
                res.Message = "Misslyckades med att ändra leverantörsnamn på order: " + e.Message;
            }

            return Json(res);
        }
    }
}
