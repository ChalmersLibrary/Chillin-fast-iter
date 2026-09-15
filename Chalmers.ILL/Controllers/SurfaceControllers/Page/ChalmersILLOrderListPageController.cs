using Chalmers.ILL.Members;
using Chalmers.ILL.Models;
using Chalmers.ILL.Models.Page;
using Chalmers.ILL.OrderItems;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;

namespace Chalmers.ILL.Controllers.SurfaceControllers.Page
{
    public class ChalmersILLOrderListPageController : Controller
    {
        IMemberInfoManager _memberInfoManager;
        IOrderItemSearcher _orderItemSearcher;

        public ChalmersILLOrderListPageController(IMemberInfoManager memberInfoManager, IOrderItemSearcher orderItemSearcher)
        {
            _memberInfoManager = memberInfoManager;
            _orderItemSearcher = orderItemSearcher;
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

                // ConfigurationManager.AppSettings can come back null here under `dotnet test`:
                // the modern System.Configuration.ConfigurationManager package resolves config
                // against the *entry* assembly (the vstest host), not Chalmers.ILL.Tests.dll, so
                // app.config's appSettings aren't found in that hosting context - a
                // ConfigurationManager limitation fas 6 will remove, not something worth
                // papering over further here. The literal default matches the value already
                // configured in both Web.config and the test project's app.config.
                var implementationDateStringParts = (ConfigurationManager.AppSettings["ManualAnonymizationImplementationDate"] ?? "2020-01-01").Split('-');
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
