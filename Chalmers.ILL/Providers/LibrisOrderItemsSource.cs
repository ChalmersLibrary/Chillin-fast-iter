using Chalmers.ILL.Configuration;
using Chalmers.ILL.Models;
using Chalmers.ILL.OrderItems;
using Chalmers.ILL.Patron;
using Chalmers.ILL.Utilities;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Web;

namespace Chalmers.ILL.Providers
{
    public class LibrisOrderItemsSource : ISource
    {
        public static int CREATE_ORDER_FROM_LIBRIS_DATA_EVENT_TYPE { get { return 17; } }
        public static int UPDATE_ORDER_FROM_LIBRIS_DATA_EVENT_TYPE { get { return 18; } }

        private static readonly log4net.ILog _log = log4net.LogManager.GetLogger(typeof(LibrisOrderItemsSource));

        IOrderItemManager _orderItemManager;
        IPatronDataProvider _patronDataProvider;
        IOrderItemSearcher _orderItemSearcher;
        IChillinConfiguration _config;

        private List<OrderItemSeedModel> _seeds;
        private SourcePollingResult _result;
        public SourcePollingResult Result
        {
            get
            {
                return _result;
            }
        }

        public LibrisOrderItemsSource(IOrderItemManager orderItemManager, IPatronDataProvider patronDataProvider,
            IOrderItemSearcher orderItemSearcher, IChillinConfiguration config)
        {
            _orderItemManager = orderItemManager;
            _patronDataProvider = patronDataProvider;
            _orderItemSearcher = orderItemSearcher;
            _config = config;
        }

        public SourcePollingResult Poll()
        {
            _result = new SourcePollingResult("Libris");
            _seeds = new List<OrderItemSeedModel>();

            AddSeedsForNewUserRequestsFromLibris();

            AddPatronInfoToSeeds();

            CreateNewOrdersFromSeeds();

            UpdateExistingOrdersFromLibris();

            return Result;
        }

        #region Private methods

        private void AddSeedsForNewUserRequestsFromLibris()
        {
            try
            {
                var sigels = _config.LibrarySigel.Split(',').Select(x => x.Trim()).ToList();

                foreach (var sigel in sigels)
                {
                    var addressStr = _config.LibrisApiBaseAddress + "/api/userrequests/" + sigel;

                    var httpClient = new HttpClient();
                    httpClient.DefaultRequestHeaders.Add("api-key", _config.LibrisApiKey);
                    var task = httpClient.GetStringAsync(new Uri(addressStr));

                    task.Wait();

                    var userRequestsQueryResult = JsonConvert.DeserializeObject<dynamic>(task.Result);

                    var userRequests = userRequestsQueryResult.user_requests;
                    for (int index = 0; index < userRequestsQueryResult.count.Value; index++)
                    {
                        try
                        {
                            var req = userRequests[index];
                            var seed = new OrderItemSeedModel();
                            seed.Id = "LIBRIS-LTB-" + req.request_id.Value;

                            if (!OrderWithSeedIdAlreadyExists(seed.Id))
                            {
                                seed.PatronEmail = req.user.email.Value;
                                seed.PatronName = "";  // req.user.full_name.Value; Borttaget pga. minskad mängd personuppgifter.
                                seed.PatronCardNumber = req.user.library_card.Value;
                                seed.DeliveryLibrarySigel = sigel;
                                seed.Message = "Författare: " + ReplaceWithNotAvailableIfEmptyString(req.author.Value) + "\n" +
                                    "Titel: " + ReplaceWithNotAvailableIfEmptyString(req.title.Value) + "\n" +
                                    "Utgivning: " + ReplaceWithNotAvailableIfEmptyString(req.imprint.Value) + "\n" +
                                    "ISBN/ISSN: " + ReplaceWithNotAvailableIfEmptyString(req.isxn.Value) + "\n" +
                                    "Meddelande från låntagare: " + ReplaceWithNotAvailableIfEmptyString(req.user_message.Value);
                                seed.MessagePrefix = "LIBRIS LÅNTAGARBESTÄLLNING" + "\n\n" +
                                    _config.LibrisApiBaseAddress + _config.LibrisApiUserRequestSuffix + "\n\n";
                                _seeds.Add(seed);
                            }
                        }
                        catch (Exception e)
                        {
                            var msg = "Error when trying to add seed for a new user request. ";
                            _result.Errors++;
                            _result.Messages.Add(msg + e.Message);
                            _log.Error(msg, e);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                throw new SourcePollingException("Error when trying to add seeds for new user requests.", e);
            }
        }

        private void AddPatronInfoToSeeds()
        {
            try
            {
                foreach (var seed in _seeds)
                {
                    seed.SierraPatronInfo = _patronDataProvider.GetPatronInfoFromLibraryCardNumberOrPersonnummer(seed.PatronCardNumber, seed.PatronCardNumber);
                }
            }
            catch (Exception e)
            {
                throw new SourcePollingException("Error connecting to Sierra.", e);
            }
        }

        private void CreateNewOrdersFromSeeds()
        {
            try
            {
                foreach (var seed in _seeds)
                {
                    try
                    {
                        int orderItemNodeId = _orderItemManager.CreateOrderItemInDbFromOrderItemSeedModel(seed, false, false);

                        var eventId = _orderItemManager.GenerateEventId(CREATE_ORDER_FROM_LIBRIS_DATA_EVENT_TYPE);
                        _orderItemManager.AddSierraDataToLog(orderItemNodeId, seed.SierraPatronInfo, eventId);

                        _result.NewOrders++;
                    }
                    catch (Exception e)
                    {
                        var msg = "Error creating new OrderItem node. ";
                        _log.Error(msg, e);
                        _result.Errors++;
                        _result.Messages.Add(msg + e.Message);
                    }
                }
            }
            catch (Exception e)
            {
                throw new SourcePollingException("Error when creating new orders from seeds.", e);
            }
        }

        private void UpdateExistingOrdersFromLibris()
        {
            try
            {
                var orders = GetSearchResultsForAllActiveOrdersThatAreOrderedFromLibris();

                foreach (var order in orders)
                {
                    var addressStr = _config.LibrisApiBaseAddress + "/api/illrequests/Z/" + order.ProviderOrderId;

                    var httpClient = new HttpClient();
                    httpClient.DefaultRequestHeaders.Add("api-key", _config.LibrisApiKey);
                    var task = httpClient.GetStringAsync(new Uri(addressStr));

                    task.Wait();

                    var illRequestsQueryResult = JsonConvert.DeserializeObject<dynamic>(task.Result);

                    var illRequests = illRequestsQueryResult.ill_requests;
                    var illRequestsCount = illRequestsQueryResult.count.Value;
                    if (illRequestsCount == 0)
                    {
                        _result.Errors++;
                        _result.Messages.Add("Couldn't find any Libris ILL request with the ID: " + order.ProviderOrderId);
                    }
                    else
                    {
                        for (int index = 0; index < illRequestsCount; index++)
                        {
                            var req = illRequests[index];

                            if (order.Status == "03:Beställd" && req.status_code.Value == "6") // Status code 6 is "Negativt svar" in Libris
                            {
                                var eventId = _orderItemManager.GenerateEventId(UPDATE_ORDER_FROM_LIBRIS_DATA_EVENT_TYPE);
                                _orderItemManager.SetStatus(order.NodeId, "02:Åtgärda", eventId, false, false);
                                _orderItemManager.AddLogItem(order.NodeId, "LIBRIS", "Negativt svar. " + _config.LibrisApiBaseAddress + "/lf.php?action=notfullfilled&id=" + req.request_id.Value, eventId);
                            }
                            else if (order.Status == "03:Beställd" && req.status_code.Value == "7") // Status code 7 is "Kan reserveras" in Libris
                            {
                                var eventId = _orderItemManager.GenerateEventId(UPDATE_ORDER_FROM_LIBRIS_DATA_EVENT_TYPE);
                                _orderItemManager.SetStatus(order.NodeId, "02:Åtgärda", eventId, false, false);
                                _orderItemManager.AddLogItem(order.NodeId, "LIBRIS", "Kan reserveras." + _config.LibrisApiBaseAddress + "/lf.php?action=may_reserve&id=" + req.request_id.Value, eventId);
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                throw new SourcePollingException("Error when updating existing orders from Libris.", e);
            }
        }

        private bool OrderWithSeedIdAlreadyExists(string seedId)
        {
            return _orderItemSearcher.Search("seedId:\"" + seedId + "\"").Count() > 0;
        }

        private IEnumerable<OrderItemModel> GetSearchResultsForAllActiveOrdersThatAreOrderedFromLibris()
        {
            return _orderItemSearcher.Search(@"(status:01\:Ny OR 
                 status:02\:Åtgärda OR
                 status:03\:Beställd OR
                 status:04\:Väntar OR
                 status:09\:Mottagen) AND
                 providerName:libris");
        }

        private string ReplaceWithNotAvailableIfEmptyString(string val)
        {
            return (val == "" ? "N/A" : val);
        }

        #endregion
    }
}