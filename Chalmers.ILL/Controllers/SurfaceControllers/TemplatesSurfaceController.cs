using Chalmers.ILL.Templates;
using Chalmers.ILL.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Chalmers.ILL.OrderItems;

namespace Chalmers.ILL.Controllers.SurfaceControllers
{
    [Authorize]
    public class TemplatesSurfaceController : Controller
    {
        ITemplateService _templateService;
        IOrderItemManager _orderItemManager;

        public TemplatesSurfaceController(ITemplateService templateService, IOrderItemManager orderItemManager)
        {
            _templateService = templateService;
            _orderItemManager = orderItemManager;
        }

        [HttpGet]
        public ActionResult RenderEditTemplatesAction()
        {
            var pageModel = new Models.PartialPage.Settings.EditTemplates();

            _templateService.PopulateTemplateList(pageModel.Templates);
            PopulateAvailableOrderItemProperties(pageModel.AvailableOrderItemProperties);

            return PartialView("Settings/EditTemplates", pageModel);
        }

        [HttpGet]
        public ActionResult GetTemplateData(int nodeId)
        {
            var json = new ResultResponseWithStringData();

            try
            {
                json.Success = true;
                json.Message = "Lyckades med att hitta mall.";
                json.Data = _templateService.GetTemplateData(nodeId);
            }
            catch (Exception e)
            {
                json.Success = false;
                json.Message = "Misslyckades med att hitta malldata: " + e.Message;
            }

            return Json(json);
        }

        [HttpGet]
        public ActionResult GetPopulatedTemplateData(int templateId, int orderItemNodeId)
        {
            var json = new ResultResponseWithStringData();

            try
            {
                var orderItem = _orderItemManager.GetOrderItem(orderItemNodeId);
                json.Success = true;
                json.Message = "Populerade malldata.";
                json.Data = _templateService.GetTemplateData(templateId, orderItem);
            }
            catch (Exception e)
            {
                json.Success = false;
                json.Message = "Misslyckades med att populera malldata: " + e.Message;
            }

            return Json(json);
        }

        [HttpPost]
        public ActionResult SetTemplateData(int nodeId, string data)
        {
            var json = new ResultResponse();

            try
            {
                _templateService.SetTemplateData(nodeId, data);

                json.Success = true;
                json.Message = "Sparade ny malldata.";
            }
            catch (Exception e)
            {
                json.Success = false;
                json.Message = "Error: " + e.Message;
            }

            return Json(json);
        }

        [HttpPost]
        public ActionResult CreateTemplate(string description, bool acquisition)
        {
            var json = new ResultResponse();

            try
            {
                _templateService.CreateTemplate(description, acquisition);

                json.Success = true;
                json.Message = "Skapade ny mall.";
            }
            catch (Exception e)
            {
                json.Success = false;
                json.Message = "Error: " + e.Message;
            }

            return Json(json);
        }

        #region Private methods.

        private List<string> PopulateAvailableOrderItemProperties(List<string> list)
        {
            foreach (var property in typeof(OrderItemModel).GetProperties())
            {
                list.Add(property.Name);
            }

            return list;
        }

        #endregion
    }
}
