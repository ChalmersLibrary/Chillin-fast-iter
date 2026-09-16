using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Chalmers.ILL.OrderItems;
using Chalmers.ILL.SignalR;
using Chalmers.ILL.Mail;
using Chalmers.ILL.UmbracoApi;
using Chalmers.ILL.Models;
using Chalmers.ILL.Configuration;
using Nest;

namespace Chalmers.ILL.Controllers.SurfaceControllers
{
    // Called by an external cron server with no user login (see IsRequestAuthorized's IP check
    // below), so it must be exempt from the global AuthorizeAttribute or the cron server just
    // gets redirected to the login page and every automated job silently stops running.
    [AllowAnonymous]
    public class SystemSurfaceController : Controller
    {
        public static int TIME_BASED_UPDATE_OF_ORDER_EVENT_TYPE { get { return 19; } }
        public static int ANONYMIZATION_OF_ORDER_EVENT_TYPE { get { return 30; } }

        private static readonly log4net.ILog _log = log4net.LogManager.GetLogger(typeof(SystemSurfaceController));

        IOrderItemManager _orderItemManager;
        INotifier _notifier;
        IMailWebApi _exchangeMailWebApi;
        IChillinOrderConfiguration _orderConfig;
        ISourceFactory _sourceFactory;
        IOrderItemSearcher _orderItemsSearcher;
        IAutomaticMailSendingEngine _automaticMailSendingEngine;
        IChillinConfiguration _config;

        public SystemSurfaceController(IOrderItemManager orderItemManager, INotifier notifier, IMailWebApi exchangeMailWebApi,
            IChillinOrderConfiguration orderConfig, ISourceFactory sourceFactory, IOrderItemSearcher orderItemsSearcher,
            IAutomaticMailSendingEngine automaticMailSendingEngine, IChillinConfiguration config)
        {
            _orderItemManager = orderItemManager;
            _notifier = notifier;
            _exchangeMailWebApi = exchangeMailWebApi;
            _orderConfig = orderConfig;
            _sourceFactory = sourceFactory;
            _orderItemsSearcher = orderItemsSearcher;
            _automaticMailSendingEngine = automaticMailSendingEngine;
            _config = config;
        }

        /// <summary>
        /// Updates the system.
        /// Checks if statuses should be changed, if something should be notified, polls sources, etc.
        /// </summary>
        /// <remarks>Should be called regularly.</remarks>
        /// <returns>Json</returns>
        [HttpPost]
        public ActionResult Update()
        {
            List<SourcePollingResult> res = new List<SourcePollingResult>();

            try
            {
                if (IsRequestAuthorized())
                {
                    ConvertOrdersWithExpiredFollowUpDateAndCertainStatusToNewStatus();

                    SignalExpiredFollowUpDates();

                    AnonymizeOldOrderItems();

                    foreach (var source in _sourceFactory.Sources())
                    {
                        try
                        {
                            res.Add(source.Poll());
                        }
                        catch (Exception e)
                        {
                            _log.Error("Error while polling source.", e);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                _log.Error("Error while running regular update.", e);
            }

            return Json(res);
        }

        /// <summary>
        /// Send out all the automatic e-mails that should be sent out.
        /// </summary>
        /// <remarks>Should be called once a day.</remarks>
        /// <returns>Json</returns>
        [HttpPost]
        public ActionResult SendOutAutomaticMailsThatAreDue()
        {
            var res = new ResultResponseWithData();

            try
            {
                if (IsRequestAuthorized())
                {
                    var mailOperationResults = _automaticMailSendingEngine.SendOutMailsThatAreDue();
                    res.Success = true;

                    foreach (var mailOperationResult in mailOperationResults)
                    {
                        res.Success &= mailOperationResult.Success;
                    }

                    res.Data = mailOperationResults;

                    if (res.Success)
                    {
                        res.Message = "Successfully processed all the pending mail operations.";
                    }
                    else
                    {
                        res.Message = "One or more mail operations failed.";
                    }
                }
                else
                {
                    res.Success = false;
                    res.Message = "Failed to send out mail.";
                }
            }
            catch (Exception e)
            {
                res.Success = false;
                res.Message = "Failed to send out mail: " + e.Message;
            }

            try
            {
                if (IsRequestAuthorized())
                {
                    _automaticMailSendingEngine.RemoveOldSentMails();
                }
                else
                {
                    _log.Warn("Request was not authorized when trying to clean old sent mails.");
                }
            }
            catch (Exception e)
            {
                _log.Error("Encountered error when cleaning old sent mails.", e);
            }

            return Json(res);
        }

        #region Private methods.

        private bool IsRequestAuthorized()
        {
            var serverName = Request.Host.Host;
            var isLocalhost = serverName == "localhost";
            var isTestServer = serverName == _config.TestServer;

            // ForwardedHeadersMiddleware (Program.cs) already folds X-Forwarded-For into
            // Connection.RemoteIpAddress, so no manual header parsing is needed here (fas 2).
            var clientIpAddr = Request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty;

            var allowedIp = _config.CronServerIpAddress;
            var res = isLocalhost || isTestServer || (!String.IsNullOrWhiteSpace(allowedIp) && clientIpAddr == allowedIp);

            if (!res)
            {
                _log.Warn("Denied access to system APIs for IP: " + clientIpAddr);
            }

            return res;
        }

        private void SignalExpiredFollowUpDates()
        {
            try
            {
                var query = @"status:03\:Beställd AND 
                    followUpDate:[" + DateTime.Now.AddMinutes(-60).ToString("yyyy-MM-ddTHH:mm:ss.fffZ") + " TO " + DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") + "]";

                // Search for our items and signal the ones that have expired recently.
                var results = _orderItemsSearcher.Search(query);
                foreach (var item in results)
                {
                    // -1 means that we haven't checked edited by properly and should disregard it
                    var memberId = -1;
                    _notifier.UpdateOrderItemUpdate(item.NodeId, memberId.ToString(), "", true, true);
                }
            }
            catch (Exception e)
            {
                _log.Error("Failed to signal expired follow up dates.", e);
            }
        }

        private void ConvertOrdersWithExpiredFollowUpDateAndCertainStatusToNewStatus()
        {
            var query = @"status:04\:Väntar AND 
                followUpDate:[1975-01-01T00:00:00.000Z TO " + DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") + "]";

            // -1 means that we haven't checked edited by properly and should disregard it
            var memberId = -1;

            // Search for our items and signal the ones that have expired recently.
            var ids = _orderItemsSearcher.Search(query).Select(x => x.NodeId).ToList();
            foreach (var id in ids)
            {
                var eventId = _orderItemManager.GenerateEventId(TIME_BASED_UPDATE_OF_ORDER_EVENT_TYPE);
                _orderItemManager.AddLogItem(id, "LOG", "Automatisk statusändring på grund av att uppföljningsdatum löpt ut.", eventId, false, false);
                var atordraSt = _orderConfig.GetAvailableStatuses().FirstOrDefault(x => x.Value.Contains("Åtgärda"));
                if (atordraSt != null)
                    _orderItemManager.SetStatus(id, atordraSt.Id, eventId);
                _notifier.UpdateOrderItemUpdate(id, memberId.ToString(), "", true, true);
            }
        }

        private void AnonymizeOldOrderItems()
        {
            var anonymizationOrderItemDateBreakpoint = DateTime.Now.AddYears(-1);
            var anonymizationOrderItemDateBreakpointText = anonymizationOrderItemDateBreakpoint.ToString("yyyy-MM-dd");

            var query = "updateDate:[* TO " + anonymizationOrderItemDateBreakpointText + "] AND status:(\"05:Levererad\" OR \"06:Annullerad\" OR \"07:Överförd\" OR \"08:Inköpt\" OR \"10:Återsänd\" OR \"15:Förlorad?\" OR \"16:Förlorad\") AND (isAnonymizedAutomatically:false OR (!_exists_:isAnonymizedAutomatically)) AND (isAnonymized:false OR (!_exists_:isAnonymized))";
            var orderItems = _orderItemsSearcher.Search(query, 10, new string[] { "nodeId" });
            foreach (var orderItem in orderItems)
            {
                try
                {
                    var id = orderItem.NodeId;
                    var eventId = _orderItemManager.GenerateEventId(ANONYMIZATION_OF_ORDER_EVENT_TYPE);
                    _orderItemManager.AddLogItem(id, "ANONYMISERING", "Automatisk anonymisering av order.", eventId, false, false);
                    _orderItemManager.AnonymizeOrder(id, eventId);
                } catch (Exception)
                {
                    // On test some order items exist in common search but only in some databases.
                    // NOOP
                }
            }
        }

        #endregion
    }
}