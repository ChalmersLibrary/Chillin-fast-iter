using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Chalmers.ILL.Models;
using Chalmers.ILL.Utilities;
using Newtonsoft.Json;
using System.Globalization;
using Chalmers.ILL.Statistics;
using Chalmers.ILL.OrderItems;

namespace Chalmers.ILL.Controllers.SurfaceControllers
{
    [Authorize]
    [AllowViewer]
    public class StatisticsSurfaceController : Controller
    {
        private IOrderItemSearcher _orderItemSearcher;

        public StatisticsSurfaceController(IOrderItemSearcher orderItemSearcher)
        {
            _orderItemSearcher = orderItemSearcher;
        }

        /// <summary>
        /// Get statistics data given a specific statistics request.
        /// </summary>
        /// <remarks>Using HTTP POST here because the query data could be fairly large, even though the method won't change anything.</remarks>
        /// <param name="json">The json data representing a statistics request.</param>
        /// <returns>JsonResult containing the statistics result.</returns>
        [HttpPost]
        public ActionResult GetData(string json)
        {
            var sReq = JsonConvert.DeserializeObject<StatisticsRequest>(json);

            var res = new StatisticsResult();

            try
            {
                IStatisticsManager statMngr = new DefaultStatMngr(_orderItemSearcher, new DefaultStatCalc());

                statMngr.CalculateAllData(sReq);

                res.Success = true;
                res.Message = "Succeessfully fetched statistics.";
                res.StatisticsData = sReq.StatisticsData;
            }
            catch (Exception e)
            {
                res.Success = false;
                res.Message = "Failed to import document from data: " + e.Message;
            }

            return Json(res);
        }

        /// <summary>
        /// Get available values from the order item searcher for a list of keys.
        /// </summary>
        /// <param name="req">The KeyValueRequest specifying what keys we want to fetch values for.</param>
        /// <returns>JsonResult containing the list of keys and the fetched available values for each key.</returns>
        [HttpGet]
        public ActionResult GetAvailableValues(KeyValueRequest req)
        {
            var res = new KeyValueResult();
            res.KeyValues = new List<KeyValues>();

            try
            {
                var allOrders = _orderItemSearcher.Search("*");

                foreach (var k in req.Keys) {
                    var keyValues = new KeyValues();
                    keyValues.Key = k;
                    keyValues.QueryField = GetQueryField(k);
                    SetPrettyName(keyValues);
                    keyValues.AvailableValues = allOrders
                        .Select(x => GetFieldValue(x, k))
                        .Where(v => !string.IsNullOrEmpty(v))
                        .Distinct()
                        .OrderBy(x => x)
                        .ToList();
                    res.KeyValues.Add(keyValues);
                }

                res.Success = true;
                res.Message = "Successfully fetched available values for keys.";
            }
            catch (Exception e)
            {
                res.Success = false;
                res.Message = "Failed to fetch available values for keys: " + e.Message;
            }

            return Json(res);
        }

        #region Private

        /// <summary>
        /// The search-index name for a filter key. The index stores the camelCased JSON property
        /// names (see how the app's own queries elsewhere read status:, providerName:,
        /// followUpDate:), while the keys here are C# property names - and two of them are not
        /// top-level properties at all. Lucene field names are case sensitive, so filtering on
        /// "Status:" or "Type:" matched nothing at all and every filtered figure came out zero.
        /// </summary>
        private static string GetQueryField(string key)
        {
            if (key == "pType") return "sierraInfo.ptype";
            if (key == "HomeLibrary") return "sierraInfo.home_library";
            return char.ToLowerInvariant(key[0]) + key.Substring(1);
        }

        private static string GetFieldValue(OrderItemModel item, string key)
        {
            if (key == "pType")
            {
                return item.SierraInfo?.ptype.ToString();
            }
            else if (key == "HomeLibrary")
            {
                return item.SierraInfo?.home_library;
            }

            return item.GetType().GetProperty(key)?.GetValue(item) as string;
        }

        private void SetPrettyName(KeyValues kv)
        {
            if (kv.Key == "Type")
            {
                kv.Name = "Typ";
            }
            else if (kv.Key == "ProviderName")
            {
                kv.Name = "Leverantörsnamn";
            }
            else if (kv.Key == "pType")
            {
                kv.Name = "P-Typ";
            }
            else if (kv.Key == "DeliveryLibrary")
            {
                kv.Name = "Ägandebibliotek";
            }
            else if (kv.Key == "HomeLibrary")
            {
                kv.Name = "Hembibliotek";
            }
            else if (kv.Key == "CancellationReason")
            {
                kv.Name = "Annulleringsorsak";
            }
            else if (kv.Key == "PurchasedMaterial")
            {
                kv.Name = "Inköpt Material";
            }
            else
            {
                kv.Name = kv.Key;
            }
        }

        #endregion
    }
}