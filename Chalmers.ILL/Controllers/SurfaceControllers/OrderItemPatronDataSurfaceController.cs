using Chalmers.ILL.Models;
using Chalmers.ILL.Utilities;
using Chalmers.ILL.Extensions;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Newtonsoft.Json;
using Chalmers.ILL.Patron;
using System.Configuration;
using System.Net;
using System.IO;
using Chalmers.ILL.OrderItems;
using Chalmers.ILL.UmbracoApi;

namespace Chalmers.ILL.Controllers.SurfaceControllers
{
    [Authorize]
    public class OrderItemPatronDataSurfaceController : Controller
    {
        public static int EVENT_TYPE { get { return 5; } }

        IOrderItemManager _orderItemManager;
        IPatronDataProvider _patronDataProviderSierraCache;
        IPatronDataProvider _patronDataProviderSierra;
        IPersonDataProvider _personDataProvider;

        public OrderItemPatronDataSurfaceController(IOrderItemManager orderItemManager, IPatronDataProvider patronDataProviderSierraCache,
            IPatronDataProvider patronDataProviderSierra, IPersonDataProvider personDataProvider)
        {
            _orderItemManager = orderItemManager;
            _patronDataProviderSierraCache = patronDataProviderSierraCache;
            _patronDataProviderSierra = patronDataProviderSierra;
            _personDataProvider = personDataProvider;
        }

        [HttpGet]
        public ActionResult RenderPatronDataView(int nodeId)
        {
            // Get a new OrderItem populated with values for this node
            var orderItem = _orderItemManager.GetOrderItem(nodeId);

            return PartialView("Chalmers.ILL.Action.PatronData", orderItem);
        }


        /// <summary>
        /// Query data from our Solr cache.
        /// </summary>
        /// <param name="pnr">The query string.</param>
        /// <returns>Returns a result indicating how the request went.</returns>
        [HttpGet]
        public ActionResult QueryPatronDataFromCache(string query)
        {
            var json = new ResultResponse();

            try
            {
                json.Message = JsonConvert.SerializeObject(_patronDataProviderSierraCache.GetPatrons(query));
                json.Success = true;
            }
            catch (Exception e)
            {
                json.Message = e.Message;
                json.Success = false;
            }

            return Json(json);
        }

        /// <summary>
        /// Retrieves the data from Sierra using first library card number and secondly personnummer if the first 
        /// fails. After successfully fetching the data it binds it to the order item with the given id.
        /// </summary>
        /// <param name="orderItemNodeId">The id of the order item which the document should be bound to.</param>
        /// <param name="lcn">The library card number which we should search for.</param>
        /// <param name="pnr">The "personnummer" which we should search for.</param>
        /// <returns>Returns a result indicating how the request went.</returns>
        [HttpPost]
        public ActionResult FetchPatronDataUsingLcnOrPnr(int orderItemNodeId, string lcn, string pnr, bool cache=true)
        {
            var json = new ResultResponse();

            try
            {
                var content = _orderItemManager.GetOrderItem(orderItemNodeId);
                var eventId = _orderItemManager.GenerateEventId(EVENT_TYPE);

                SierraModel sm = null;
                if (cache)
                {
                    sm = _patronDataProviderSierraCache.GetPatronInfoFromLibraryCardNumberOrPersonnummer(lcn, pnr);
                }
                else
                {
                    sm = _patronDataProviderSierra.GetPatronInfoFromLibraryCardNumberOrPersonnummer(lcn, pnr);
                }

                if (!String.IsNullOrEmpty(sm.id))
                {
                    _orderItemManager.SetPatronData(content.NodeId, JsonConvert.SerializeObject(sm), sm.record_id, sm.ptype, sm.home_library, sm.aff);
                    _orderItemManager.SaveWithoutEventsAndWithSynchronousReindexing(content.NodeId, false, false);
                }
                _orderItemManager.AddSierraDataToLog(orderItemNodeId, sm, eventId);

                json.Success = true;
                json.Message = "Succcessfully loaded Sierra data from \"personnummer\" or library card number.";
            }
            catch (Exception e)
            {
                json.Success = false;
                json.Message = "Failed to load Sierra data from \"personnummer\" or library card number: " + e.Message;
            }

            return Json(json);
        }

        /// <summary>
        /// Retrieves the data from Sierra using Sierra identifier. After successfully fetching the data it binds it to 
        /// the order item with the given id.
        /// </summary>
        /// <remarks>Cache storage expects sierra identifier with starting 'p' and ending control number. Non cache 
        /// storage expects Sierra identifier without this.</remarks>
        /// <param name="orderItemNodeId">Order item node identifier.</param>
        /// <param name="sierraId">Sierra identifier.</param>
        /// <param name="cache">If we should fetch data from cache or not.</param>
        /// <returns>Result indicating how the request went.</returns>
        [HttpGet]
        public ActionResult FetchPatronDataUsingSierraId(int orderItemNodeId, string sierraId, bool cache = true)
        {
            var json = new ResultResponse();

            try
            {
                var content = _orderItemManager.GetOrderItem(orderItemNodeId);
                var eventId = _orderItemManager.GenerateEventId(EVENT_TYPE);

                SierraModel sm = null;
                if (cache)
                {
                    // Cache expects sierra identifier with starting 'p' and ending control number.
                    sm = _patronDataProviderSierraCache.GetPatronInfoFromSierraId(sierraId);
                }
                else
                {
                    // Non cache expects sierra identifier without starting 'p' and ending control number.
                    sm = _patronDataProviderSierra.GetPatronInfoFromSierraId(sierraId);
                }

                if (!String.IsNullOrEmpty(sm.id))
                {
                    _orderItemManager.SetPatronData(content.NodeId, JsonConvert.SerializeObject(sm), sm.record_id, sm.ptype, sm.home_library, sm.aff);
                    _orderItemManager.SaveWithoutEventsAndWithSynchronousReindexing(content.NodeId, false, false);
                }
                _orderItemManager.AddSierraDataToLog(orderItemNodeId, sm, eventId);

                json.Success = true;
                json.Message = "Succcessfully loaded Sierra data using sierra identifier.";
            }
            catch (Exception e)
            {
                json.Success = false;
                json.Message = "Failed to load Sierra data using sierra identifier: " + e.Message;
            }

            return Json(json);
        }

        /// <summary>
        /// Retrieves the data from Sierra using the library card number and binds the data to the order item
        /// with the given id.
        /// </summary>
        /// <param name="orderItemNodeId">The id of the order item which the document should be bound to.</param>
        /// <param name="lcn">The library card number which we should search for.</param>
        /// <returns>Returns a result indicating how the request went.</returns>
        [HttpPost]
        public ActionResult FetchPatronDataUsingLcn(int orderItemNodeId, string lcn, bool cache=true)
        {
            var json = new ResultResponse();

            try
            {
                var content = _orderItemManager.GetOrderItem(orderItemNodeId);
                var eventId = _orderItemManager.GenerateEventId(EVENT_TYPE);

                SierraModel sm = null;
                if (cache)
                {
                    sm = _patronDataProviderSierraCache.GetPatronInfoFromLibraryCardNumber(lcn);
                }
                else
                {
                    sm = _patronDataProviderSierra.GetPatronInfoFromLibraryCardNumber(lcn);
                }

                if (!String.IsNullOrEmpty(sm.id))
                {
                    _orderItemManager.SetPatronData(content.NodeId, JsonConvert.SerializeObject(sm), sm.record_id, sm.ptype, sm.home_library, sm.aff);
                    _orderItemManager.SaveWithoutEventsAndWithSynchronousReindexing(content.NodeId, false, false);
                }
                _orderItemManager.AddSierraDataToLog(orderItemNodeId, sm, eventId);

                json.Success = true;
                json.Message = "Succcessfully loaded Sierra data from library card number.";
            }
            catch (Exception e)
            {
                json.Success = false;
                json.Message = "Failed to load Sierra data from library card number: " + e.Message;
            }

            return Json(json);
        }

        /// <summary>
        /// Retrieves data from PDB and then from FOLIO.
        /// </summary>
        /// <param name="orderItemNodeId">The id of the order item which the document should be bound to.</param>
        /// <param name="data">CID, e-mail or pnr</param>
        /// <returns>Returns a result indicating how the request went.</returns>
        [HttpPost]
        public ActionResult FetchPatronDataUsingPdbRoundtrip(int orderItemNodeId, string data)
        {
            var json = new ResultResponse();

            try
            {
                var content = _orderItemManager.GetOrderItem(orderItemNodeId);
                var eventId = _orderItemManager.GenerateEventId(EVENT_TYPE);

                var sm = new SierraModel();
                var smFromPdb = _personDataProvider.GetPatronInfoFromLibraryCidPersonnummerOrEmail(data, data);

                if (!String.IsNullOrEmpty(smFromPdb.cid))
                {
                    sm = _patronDataProviderSierra.GetPatronInfoFromLibraryCardNumberOrPersonnummer(smFromPdb.pnum, smFromPdb.pnum);

                    if (String.IsNullOrEmpty(sm.id))
                    {
                        // Found in PDB but nothing in FOLIO, use data from PDB
                        sm = smFromPdb;

                        // Remove this data, so that it is clear that it did not come from FOLIO.
                        sm.first_name = "";
                        sm.last_name = "";
                        sm.email = "";
                    }
                }


                if (!String.IsNullOrEmpty(sm.id) || !String.IsNullOrEmpty(sm.cid))
                {
                    _orderItemManager.SetPatronData(content.NodeId, JsonConvert.SerializeObject(sm), sm.record_id, sm.ptype, sm.home_library, sm.aff);
                    _orderItemManager.SaveWithoutEventsAndWithSynchronousReindexing(content.NodeId, false, false);
                }
                _orderItemManager.AddSierraDataToLog(orderItemNodeId, sm, eventId);

                json.Success = true;
                json.Message = "Succcessfully loaded Sierra data from \"personnummer\" or library card number.";
            }
            catch (Exception e)
            {
                json.Success = false;
                json.Message = "Failed to load Sierra data from \"personnummer\" or library card number: " + e.Message;
            }

            return Json(json);
        }

        #region Private methods

        private void UpdateDeliveryLibraryIfNeeded(int nodeId, SierraModel sierraModel, string eventId)
        {
            var orderItem = _orderItemManager.GetOrderItem(nodeId);

            if (sierraModel.home_library != null && sierraModel.home_library.Contains("hbib"))
            {
                _orderItemManager.SetDeliveryLibrary(nodeId, "Huvudbiblioteket", eventId, false, false);
            }
            else if (sierraModel.home_library != null && sierraModel.home_library.Contains("abib"))
            {
                _orderItemManager.SetDeliveryLibrary(nodeId, "Arkitekturbiblioteket", eventId, false, false);
            }
            else if (sierraModel.home_library != null && sierraModel.home_library.Contains("lbib"))
            {
                _orderItemManager.SetDeliveryLibrary(nodeId, "Lindholmenbiblioteket", eventId, false, false);
            }
        }

        #endregion
    }
}
