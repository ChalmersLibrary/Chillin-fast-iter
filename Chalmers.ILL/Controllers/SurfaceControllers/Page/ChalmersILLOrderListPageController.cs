using Chalmers.ILL.Configuration;
using Chalmers.ILL.Members;
using Chalmers.ILL.Models;
using Chalmers.ILL.Models.Page;
using Chalmers.ILL.OrderItems;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;

namespace Chalmers.ILL.Controllers.SurfaceControllers.Page
{
    public class ChalmersILLOrderListPageController : Controller
    {
        IMemberInfoManager _memberInfoManager;
        IOrderItemSearcher _orderItemSearcher;
        IChillinConfiguration _config;

        public ChalmersILLOrderListPageController(IMemberInfoManager memberInfoManager, IOrderItemSearcher orderItemSearcher, IChillinConfiguration config)
        {
            _memberInfoManager = memberInfoManager;
            _orderItemSearcher = orderItemSearcher;
            _config = config;
        }

        public ActionResult Index()
        {
            var customModel = new ChalmersILLOrderListPageModel();

            _memberInfoManager.PopulateModelWithMemberData(Request, Response, customModel);

            int start = Request.Query["start"].ToString() != "" ? Int32.Parse(Request.Query["start"]) : 0;

            if (!String.IsNullOrEmpty(Request.Query["query"]))
            {
                var queryString = Request.Query["query"].ToString().Trim();

                if (IsOrderId(queryString))
                {
                    queryString = "\"" + queryString + "\"";
                }

                customModel.PendingOrderItems = _orderItemSearcher.Search(queryString, start, 50);
                customModel.ManualAnonymizationItems = new SearchResult
                {
                    Items = new List<OrderItemModel>(),
                    Count = 0
                };
            }
            else
            {
                customModel.PendingOrderItems = _orderItemSearcher.Search(@"status:01\:Ny OR status:02\:Åtgärda OR status:09\:Mottagen OR
                     (status:03\:Beställd AND followUpDate:[1975-01-01T00:00:00.000Z TO " + DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") + @"]) OR
                     (status:14\:Infodisk AND dueDate:[1975-01-01T00:00:00.000Z TO " + DateTime.Now.AddDays(5).Date.ToString("yyyy-MM-ddT") + @"23:59:59.999Z])", start, 50);

                // The literal default matches the value already configured in appsettings.json -
                // this only kicks in if the key is ever removed from config entirely.
                var implementationDateStringParts = (_config.ManualAnonymizationImplementationDate ?? "2020-01-01").Split('-');
                var implementationDate = new DateTime(int.Parse(implementationDateStringParts[0]), int.Parse(implementationDateStringParts[1]), int.Parse(implementationDateStringParts[2]));
                var differenceBetweenNowAndImplementationDate = DateTime.Now - implementationDate;
                var daysToSubtract = differenceBetweenNowAndImplementationDate.Days * 5;
                var implementationDateMinusOneYear = implementationDate.AddYears(-1);
                var manualAnonymizationDateLimit = implementationDateMinusOneYear.AddDays(-daysToSubtract);
                // customModel.ManualAnonymizationItems = _orderItemSearcher.Search(manualAnonymizationQueryString, 0, 5);
                customModel.ManualAnonymizationItems = new SearchResult
                {
                    Items = new List<OrderItemModel>(),
                    Count = 0
                };
            }

            return View("~/Views/ChalmersILLOrderListPage.cshtml", customModel);
        }

        private bool IsOrderId(string text)
        {
            return new Regex("^cthb-[a-zA-Z0-9]{8}-[0-9]+$").IsMatch(text);
        }
    }
}
