using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Chalmers.ILL.Configuration;
using Chalmers.ILL.Models;
using Chalmers.ILL.Models.Mail;
using Chalmers.ILL.SignalR;
using Chalmers.ILL.UmbracoApi;
using Chalmers.ILL.Utilities;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using static Chalmers.ILL.Models.OrderItemModel;

namespace Chalmers.ILL.OrderItems
{
    // Replaces EntityFrameworkOrderItemManager (fas 7, "Implementera filbaserad IOrderItemManager").
    // One JSON file per order under DataPath/orders/{NodeId/1000:D3}/{NodeId}.json - see fas 7's
    // "Varför det fungerar" in TODO-remove-dotnet-framework.md: every read already pulls the whole
    // aggregate (LogItemsList/AttachmentList/SierraInfo), and there is no relational query anywhere.
    //
    // The old thread-keyed DbContext dictionary is gone, but the batching behavior it enabled is
    // NOT incidental - controllers all over the app call Set*/AddLogItem with doReindex=false,
    // doSignal=false several times in a row, then a final call with the defaults (true, true) to
    // flush everything together (one disk write, one Elasticsearch update, one SignalR notify).
    // _threadIdToPendingOrder replicates exactly that: an in-progress, not-yet-persisted order
    // keyed by the current managed thread (every request here is synchronous end to end - fas 4,
    // "noll async-actions" - so thread affinity holds for the lifetime of one request, same
    // assumption the EF version relied on).
    public class FileOrderItemManager : IOrderItemManager
    {
        private static readonly log4net.ILog _log = log4net.LogManager.GetLogger(typeof(FileOrderItemManager));

        private INotifier _notifier;
        private readonly IChillinOrderConfiguration _orderConfig;
        private readonly IOrderItemSearcher _orderItemSearcher;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly string _ordersDirectory;
        private readonly NodeIdGenerator _nodeIdGenerator;
        private readonly OrderIdIndex _orderIdIndex;
        private readonly Random _rand;

        private readonly Dictionary<int, PendingOrder> _threadIdToPendingOrder = new Dictionary<int, PendingOrder>();
        private readonly ConcurrentDictionary<int, SemaphoreSlim> _orderLocks = new ConcurrentDictionary<int, SemaphoreSlim>();

        private class PendingOrder
        {
            public int NodeId;
            public OrderItemModel Item;
            public bool IsNew;
        }

        public FileOrderItemManager(
            IChillinOrderConfiguration orderConfig,
            IOrderItemSearcher orderItemSearcher,
            IChillinConfiguration config,
            NodeIdGenerator nodeIdGenerator,
            OrderIdIndex orderIdIndex,
            IHttpContextAccessor httpContextAccessor = null)
        {
            _orderConfig = orderConfig;
            _orderItemSearcher = orderItemSearcher;
            _ordersDirectory = Path.Combine(config.DataPath, "orders");
            _nodeIdGenerator = nodeIdGenerator;
            _orderIdIndex = orderIdIndex;
            _httpContextAccessor = httpContextAccessor ?? new HttpContextAccessor();
            _rand = new Random();
        }

        public void SetNotifier(INotifier notifier)
        {
            _notifier = notifier;
        }

        #region Reads

        public List<LogItem> GetLogItems(int nodeId)
        {
            var orderItem = LoadForRead(nodeId);
            if (orderItem == null)
                throw new OrderItemNotFoundException("Failed to find order item when trying to fetch log items.");

            return orderItem.LogItemsList;
        }

        public OrderItemModel GetOrderItem(int nodeId)
        {
            try
            {
                var orderItem = LoadForRead(nodeId);
                if (orderItem == null)
                {
                    _log.Warn("GetOrderItem: Couldn't find any node with the ID " + nodeId + ".");
                    return null;
                }

                ApplyReadTimeFixups(orderItem);
                return orderItem;
            }
            catch (Exception e)
            {
                _log.Error("Failed to query node.", e);
                return null;
            }
        }

        public OrderItemModel GetOrderItem(string orderId)
        {
            try
            {
                var nodeId = _orderIdIndex.Lookup(orderId);
                if (nodeId == null)
                {
                    _log.Warn("GetOrderItem: Couldn't find any node with the order ID " + orderId + ".");
                    return null;
                }

                var orderItem = LoadForRead(nodeId.Value);
                if (orderItem == null)
                {
                    _log.Warn("GetOrderItem: Couldn't find any node with the order ID " + orderId + ".");
                    return null;
                }

                ApplyReadTimeFixups(orderItem);
                return orderItem;
            }
            catch (Exception e)
            {
                _log.Error("Failed to query node.", e);
                return null;
            }
        }

        // GetLockedOrderItems used to be `.Where(x => x.EditedBy == memberId)` against the database
        // - the one query in this class that wasn't a key lookup. A full directory scan of every
        // order file to answer it would be unacceptable at the documented volume (10 000-100 000
        // orders), so it goes to Elasticsearch instead (fas 7, "Flytta EditedBy-uppslaget till
        // Elasticsearch") - editedBy is already indexed on every order.
        public IEnumerable<OrderItemModel> GetLockedOrderItems(string memberId)
        {
            return _orderItemSearcher.Search("editedBy:\"" + memberId + "\"");
        }

        private void ApplyReadTimeFixups(OrderItemModel orderItem)
        {
            orderItem.AttachmentList = orderItem.AttachmentList.OrderBy(x => x.Title).ToList();
            orderItem.LogItemsList = orderItem.LogItemsList.OrderByDescending(x => x.CreateDate).ToList();

            if (String.IsNullOrWhiteSpace(orderItem.SierraInfo.home_library_pretty_name))
            {
                orderItem.SierraInfo.home_library_pretty_name = GetPrettyLibraryNameFromLibraryAbbreviation(orderItem.SierraInfo.home_library);
            }

            FillOutStuff(orderItem);
        }

        #endregion

        #region Mutations

        public void AddExistingMediaItemAsAnAttachment(int orderNodeId, string mediaNodeId, string title, string link, string eventId, bool doReindex = true, bool doSignal = true)
        {
            Mutate(orderNodeId, "add existing media item as an attachment", orderItem =>
            {
                if (!String.IsNullOrEmpty(title) && !String.IsNullOrEmpty(link))
                {
                    if (orderItem.AttachmentList == null)
                        orderItem.AttachmentList = new List<OrderAttachment>();

                    orderItem.AttachmentList.Add(new OrderAttachment { Title = title, Link = link, MediaItemNodeId = mediaNodeId });

                    AppendLogItem(orderItem, "ATTACHMENT", "Nytt dokument bundet till ordern.", eventId);
                }
            }, doReindex, doSignal);
        }

        public void AddExistingMediaItemAsAnAttachmentWithoutLogging(int orderNodeId, string mediaNodeId, string title, string link, bool doReindex = true, bool doSignal = true)
        {
            Mutate(orderNodeId, "add existing media item as an attachment without logging", orderItem =>
            {
                if (!String.IsNullOrEmpty(title) && !String.IsNullOrEmpty(link))
                {
                    if (orderItem.AttachmentList == null)
                        orderItem.AttachmentList = new List<OrderAttachment>();

                    orderItem.AttachmentList.Add(new OrderAttachment { Title = title, Link = link, MediaItemNodeId = mediaNodeId });
                }
            }, doReindex, doSignal);
        }

        public void AddLogItem(int OrderItemNodeId, string Type, string Message, string eventId, bool doReindex = true, bool doSignal = true)
        {
            Mutate(OrderItemNodeId, "add log item", orderItem => AppendLogItem(orderItem, Type, Message, eventId), doReindex, doSignal);
        }

        public void AddSierraDataToLog(int orderItemNodeId, SierraModel sm, string eventId, bool doReindex = true, bool doSignal = true)
        {
            if (!string.IsNullOrEmpty(sm.id))
            {
                string logtext = "Firstname: " + sm.first_name + " Lastname: " + sm.last_name + "\n" +
                                    "Barcode: " + sm.barcode + " Email: " + sm.email + " Ptyp: " + sm.ptype + "\n";
                AddLogItem(orderItemNodeId, "SIERRA", logtext, eventId, doReindex, doSignal);
            }
            else if (!String.IsNullOrEmpty(sm.cid))
            {
                AddLogItem(orderItemNodeId, "SIERRA", "Låntagaren hittades inte, men en person på Chalmers hittades.", eventId, doReindex, doSignal);
            }
            else
            {
                AddLogItem(orderItemNodeId, "SIERRA", "Låntagaren hittades inte.", eventId, doReindex, doSignal);
            }
        }

        public int CreateOrderItemInDbFromOrderItemModel(OrderItemModel orderItem, bool doReindex = true, bool doSignal = true)
        {
            return PersistNewOrder(orderItem, orderId: null, doReindex, doSignal);
        }

        public int CreateOrderItemInDbFromMailQueueModel(MailQueueModel model, bool doReindex = true, bool doSignal = true)
        {
            var orderId = "cthb-" + Helpers.CalculateMD5Hash(DateTime.Now.Ticks.ToString());

            var newOrderItem = new OrderItemModel();

            var originalOrder = UrlDecodeAndEscapeAllLinks(model.OriginalOrder);
            newOrderItem.OriginalOrder = originalOrder;
            newOrderItem.Reference = originalOrder;
            newOrderItem.PatronName = model.PatronName;
            newOrderItem.PatronEmail = model.PatronEmail;
            newOrderItem.PatronCardNo = model.PatronCardNo;
            newOrderItem.PatronAffiliation = model.SierraPatronInfo.aff;
            newOrderItem.FollowUpDate = DateTime.Now;
            newOrderItem.EditedBy = "";
            newOrderItem.StatusId = _orderConfig.GetIdByValue("OrderStatus", "01:Ny");
            newOrderItem.Status = "01:Ny";
            newOrderItem.SierraInfo = model.SierraPatronInfo;
            newOrderItem.LogItemsList = new List<LogItem>();
            newOrderItem.AttachmentList = new List<OrderAttachment>();
            newOrderItem.DueDate = DateTime.Now;
            newOrderItem.ProviderDueDate = DateTime.Now;
            newOrderItem.DeliveryDate = new DateTime(1970, 1, 1);
            newOrderItem.BookId = "";
            newOrderItem.ProviderInformation = "";

            switch (model.DeliveryLibrary)
            {
                case "Z":
                    newOrderItem.DeliveryLibraryId = _orderConfig.GetIdByValue("DeliveryLibrary", "Huvudbiblioteket");
                    newOrderItem.DeliveryLibrary = "Huvudbiblioteket";
                    break;
                case "Za":
                    newOrderItem.DeliveryLibraryId = _orderConfig.GetIdByValue("DeliveryLibrary", "Arkitekturbiblioteket");
                    newOrderItem.DeliveryLibrary = "Arkitekturbiblioteket";
                    break;
                case "Zl":
                    newOrderItem.DeliveryLibraryId = _orderConfig.GetIdByValue("DeliveryLibrary", "Lindholmenbiblioteket");
                    newOrderItem.DeliveryLibrary = "Lindholmenbiblioteket";
                    break;
                default:
                    break;
            }

            if (model.IsPurchaseRequest)
            {
                newOrderItem.TypeId = _orderConfig.GetIdByValue("OrderType", "Inköpsförslag");
                newOrderItem.Type = "Inköpsförslag";
            }

            return PersistNewOrder(newOrderItem, orderId, doReindex, doSignal);
        }

        public int CreateOrderItemInDbFromOrderItemSeedModel(OrderItemSeedModel model, bool doReindex = true, bool doSignal = true)
        {
            var orderId = "cthb-" + Helpers.CalculateMD5Hash(DateTime.Now.Ticks.ToString());

            var newOrderItem = new OrderItemModel();

            newOrderItem.OriginalOrder = model.Message;
            newOrderItem.Reference = model.MessagePrefix + model.Message;
            newOrderItem.PatronName = model.PatronName;
            newOrderItem.PatronEmail = model.PatronEmail;
            newOrderItem.PatronCardNo = model.PatronCardNumber;
            newOrderItem.FollowUpDate = DateTime.Now;
            newOrderItem.EditedBy = "";
            newOrderItem.StatusId = _orderConfig.GetIdByValue("OrderStatus", "01:Ny");
            newOrderItem.Status = "01:Ny";
            newOrderItem.SierraInfo = model.SierraPatronInfo;
            newOrderItem.LogItemsList = new List<LogItem>();
            newOrderItem.AttachmentList = new List<OrderAttachment>();
            newOrderItem.DueDate = DateTime.Now;
            newOrderItem.ProviderDueDate = DateTime.Now;
            newOrderItem.DeliveryDate = new DateTime(1970, 1, 1);
            newOrderItem.BookId = "";
            newOrderItem.ProviderInformation = "";
            newOrderItem.SeedId = model.Id;

            if (model.DeliveryLibrarySigel == "Z")
            {
                newOrderItem.DeliveryLibraryId = _orderConfig.GetIdByValue("DeliveryLibrary", "Huvudbiblioteket");
                newOrderItem.DeliveryLibrary = "Huvudbiblioteket";
            }
            else if (model.DeliveryLibrarySigel == "ZL")
            {
                newOrderItem.DeliveryLibraryId = _orderConfig.GetIdByValue("DeliveryLibrary", "Lindholmenbiblioteket");
                newOrderItem.DeliveryLibrary = "Lindholmenbiblioteket";
            }
            else if (model.DeliveryLibrarySigel == "ZA")
            {
                newOrderItem.DeliveryLibraryId = _orderConfig.GetIdByValue("DeliveryLibrary", "Arkitekturbiblioteket");
                newOrderItem.DeliveryLibrary = "Arkitekturbiblioteket";
            }
            else if (!String.IsNullOrEmpty(model.SierraPatronInfo.home_library))
            {
                if (model.SierraPatronInfo.home_library.ToLower() == "abib")
                {
                    newOrderItem.DeliveryLibraryId = _orderConfig.GetIdByValue("DeliveryLibrary", "Arkitekturbiblioteket");
                    newOrderItem.DeliveryLibrary = "Arkitekturbiblioteket";
                }
                else if (model.SierraPatronInfo.home_library.ToLower() == "lbib")
                {
                    newOrderItem.DeliveryLibraryId = _orderConfig.GetIdByValue("DeliveryLibrary", "Lindholmenbiblioteket");
                    newOrderItem.DeliveryLibrary = "Lindholmenbiblioteket";
                }
                else
                {
                    newOrderItem.DeliveryLibraryId = _orderConfig.GetIdByValue("DeliveryLibrary", "Huvudbiblioteket");
                    newOrderItem.DeliveryLibrary = "Huvudbiblioteket";
                }
            }

            return PersistNewOrder(newOrderItem, orderId, doReindex, doSignal);
        }

        public string GenerateEventId(int type)
        {
            return "event-" + _rand.Next(0, 65535).ToString("X4") + "-" + type.ToString("D2");
        }

        public void RemoveConnectionToMediaItem(int orderNodeId, string mediaNodeId, bool doReindex = true, bool doSignal = true)
        {
            Mutate(orderNodeId, "remove connection to media item", orderItem =>
            {
                orderItem.AttachmentList.RemoveAll(i => i.MediaItemNodeId == mediaNodeId);
            }, doReindex, doSignal);
        }

        public void SaveWithoutEventsAndWithSynchronousReindexing(int nodeId, bool doReindex = true, bool doSignal = true)
        {
            Mutate(nodeId, "save without events", orderItem => { }, doReindex, doSignal);
        }

        public void SetBookId(int nodeId, string bookId, string eventId, bool doReindex = true, bool doSignal = true)
        {
            Mutate(nodeId, "set book ID", orderItem =>
            {
                if (orderItem.BookId != bookId)
                {
                    orderItem.BookId = bookId;
                    AppendLogItem(orderItem, "BOKINFO", "Bok-ID ändrat till " + bookId + ".", eventId);
                }
            }, doReindex, doSignal);
        }

        public void SetTitleInformation(int nodeId, string titleInformation, string eventId, bool doReindex = true, bool doSignal = true)
        {
            Mutate(nodeId, "set title information", orderItem =>
            {
                orderItem.TitleInformation = titleInformation;
            }, doReindex, doSignal);
        }

        public void SetCancellationReason(int orderNodeId, int cancellationReasonId, string eventId, bool doReindex = true, bool doSignal = true)
        {
            Mutate(orderNodeId, "set cancellation reason", orderItem =>
            {
                if (orderItem.CancellationReasonId != cancellationReasonId)
                {
                    orderItem.CancellationReasonId = cancellationReasonId;
                    AppendLogItem(orderItem, "ANNULLERINGSORSAK", "Annulleringsorsak ändrad till " + _orderConfig.GetValueById(cancellationReasonId), eventId);
                }
            }, doReindex, doSignal);
        }

        public void SetDeliveryLibrary(int orderNodeId, string deliveryLibraryPrevalue, string eventId, bool doReindex = true, bool doSignal = true)
        {
            var deliveryLibraryId = _orderConfig.GetIdByValue("DeliveryLibrary", deliveryLibraryPrevalue);
            SetDeliveryLibrary(orderNodeId, deliveryLibraryId, eventId, doReindex, doSignal);
        }

        public void SetDeliveryLibrary(int orderNodeId, int deliveryLibraryId, string eventId, bool doReindex = true, bool doSignal = true)
        {
            Mutate(orderNodeId, "set delivery library", orderItem =>
            {
                var currentDeliveryLibrary = orderItem.DeliveryLibraryId;
                if (currentDeliveryLibrary != deliveryLibraryId)
                {
                    orderItem.DeliveryLibraryId = deliveryLibraryId;
                    var fromLib = currentDeliveryLibrary != -1 ? _orderConfig.GetValueById(currentDeliveryLibrary).Split(':').Last() : "Odefinierad";
                    if (fromLib == "Lindholmenbiblioteket") fromLib = "Kuggen";
                    var toLib = _orderConfig.GetValueById(deliveryLibraryId).Split(':').Last();
                    if (toLib == "Lindholmenbiblioteket") toLib = "Kuggen";
                    AppendLogItem(orderItem, "BIBLIOTEK", "Leveransbibliotek ändrat från " + fromLib + " till " + toLib, eventId);
                }
            }, doReindex, doSignal);
        }

        public void SetPurchaseLibrary(int orderNodeId, PurchaseLibraries purchaseLibrary, string eventId, bool doReindex = true, bool doSignal = true)
        {
            Mutate(orderNodeId, "set delivery library", orderItem =>
            {
                var currentPurchaseLibrary = orderItem.PurchaseLibrary;
                if (currentPurchaseLibrary != purchaseLibrary)
                {
                    orderItem.PurchaseLibrary = purchaseLibrary;
                    AppendLogItem(orderItem, "BIBLIOTEK", "Inköpsbibliotek ändrat från " + currentPurchaseLibrary.ToString() + " till " + purchaseLibrary.ToString() + ".", eventId);
                }
            }, doReindex, doSignal);
        }

        public void SetDrmWarning(int orderNodeId, bool status, string eventId, bool doReindex = true, bool doSignal = true)
        {
            Mutate(orderNodeId, "set DRM warning", orderItem =>
            {
                if (orderItem.DrmWarning != (status ? "1" : "0"))
                {
                    orderItem.DrmWarning = (status ? "1" : "0");
                    AppendLogItem(orderItem, "DRM", "Kan innehålla drm-material!", eventId);
                }
            }, doReindex, doSignal);
        }

        public void SetDrmWarningWithoutLogging(int orderNodeId, bool status, bool doReindex = true, bool doSignal = true)
        {
            Mutate(orderNodeId, "set DRM warning without logging", orderItem =>
            {
                orderItem.DrmWarning = (status ? "1" : "0");
            }, doReindex, doSignal);
        }

        public void SetDueDate(int nodeId, DateTime date, string eventId, bool doReindex = true, bool doSignal = true)
        {
            Mutate(nodeId, "set due date", orderItem =>
            {
                if (orderItem.DueDate != date)
                {
                    orderItem.DueDate = date;
                    AppendLogItem(orderItem, "DATE", "Återlämnas av låntagare senast " + date.ToString("yyyy-MM-dd HH:mm"), eventId);
                }
            }, doReindex, doSignal);
        }

        public void SetFollowUpDate(int nodeId, DateTime date, string eventId, bool doReindex = true, bool doSignal = true)
        {
            Mutate(nodeId, "set follow up date", orderItem =>
            {
                if (orderItem.FollowUpDate != date)
                {
                    orderItem.FollowUpDate = date;
                    AppendLogItem(orderItem, "DATE", "Följs upp senast " + date.ToString("yyyy-MM-dd HH:mm"), eventId);
                }
            }, doReindex, doSignal);
        }

        public void SetFollowUpDateWithoutLogging(int nodeId, DateTime date, bool doReindex = true, bool doSignal = true)
        {
            Mutate(nodeId, "set follow up date without logging", orderItem =>
            {
                orderItem.FollowUpDate = date;
            }, doReindex, doSignal);
        }

        public void SetPatronData(int nodeId, string sierraInfo, int sierraPatronRecordId, int pType, string homeLibrary, string aff, bool doReindex = true, bool doSignal = true)
        {
            Mutate(nodeId, "set patron data", orderItem =>
            {
                var newSierraInfo = JsonConvert.DeserializeObject<SierraModel>(sierraInfo);
                orderItem.SierraInfo.adress = newSierraInfo.adress;
                orderItem.SierraInfo.barcode = newSierraInfo.barcode;
                orderItem.SierraInfo.email = newSierraInfo.email;
                orderItem.SierraInfo.first_name = newSierraInfo.first_name;
                orderItem.SierraInfo.home_library = newSierraInfo.home_library;
                orderItem.SierraInfo.home_library_pretty_name = GetPrettyLibraryNameFromLibraryAbbreviation(newSierraInfo.home_library);
                orderItem.SierraInfo.id = newSierraInfo.id;
                orderItem.SierraInfo.last_name = newSierraInfo.last_name;
                orderItem.SierraInfo.mblock = newSierraInfo.mblock;
                orderItem.SierraInfo.ptype = newSierraInfo.ptype;
                orderItem.SierraInfo.record_id = newSierraInfo.record_id;
                orderItem.SierraInfo.active = newSierraInfo.active;
                orderItem.SierraInfo.aff = aff;
                orderItem.SierraInfo.cid = newSierraInfo.cid;
                orderItem.SierraInfo.e_resource_access = newSierraInfo.e_resource_access;
                orderItem.PatronAffiliation = aff;
                orderItem.SierraInfoStr = JsonConvert.SerializeObject(orderItem.SierraInfo);
            }, doReindex, doSignal);
        }

        public void SetPatronEmail(int nodeId, string email, string eventId, bool doReindex = true, bool doSignal = true)
        {
            Mutate(nodeId, "set patron email", orderItem =>
            {
                if (orderItem.PatronEmail != email)
                {
                    orderItem.PatronEmail = email;
                    AppendLogItem(orderItem, "MAIL_NOTE", "E-post mot låntagare ändrad till " + email, eventId);
                }
            }, doReindex, doSignal);
        }

        public void SetProviderDueDate(int nodeId, DateTime date, string eventId, bool doReindex = true, bool doSignal = true)
        {
            Mutate(nodeId, "set provider due date", orderItem =>
            {
                if (orderItem.ProviderDueDate != date)
                {
                    orderItem.ProviderDueDate = date;
                    AppendLogItem(orderItem, "DATE", "Återlämnas till leverantör senast " + date.ToString("yyyy-MM-dd HH:mm"), eventId);
                }
            }, doReindex, doSignal);
        }

        public void SetDeliveryDateWithoutLogging(int nodeId, DateTime date, string eventId, bool doReindex = true, bool doSignal = true)
        {
            Mutate(nodeId, "set delivery date", orderItem =>
            {
                orderItem.DeliveryDate = date;
            }, doReindex, doSignal);
        }

        public void SetProviderInformation(int nodeId, string providerInformation, string eventId, bool doReindex = true, bool doSignal = true)
        {
            Mutate(nodeId, "set provider information", orderItem =>
            {
                if (orderItem.ProviderInformation != providerInformation)
                {
                    orderItem.ProviderInformation = providerInformation;
                    AppendLogItem(orderItem, "LEVERANTÖR", "Leverantörsinformation ändrad till \"" + providerInformation + "\".", eventId);
                }
            }, doReindex, doSignal);
        }

        public void SetProviderName(int nodeId, string providerName, string eventId, bool doReindex = true, bool doSignal = true)
        {
            Mutate(nodeId, "set provider name", orderItem =>
            {
                if (orderItem.ProviderName != providerName)
                {
                    orderItem.ProviderName = providerName;
                    AppendLogItem(orderItem, "ORDER", "Beställd från " + providerName, eventId);
                }
            }, doReindex, doSignal);
        }

        public void SetProviderNameWithoutLogging(int nodeId, string providerName, bool doReindex = true, bool doSignal = true)
        {
            Mutate(nodeId, "set provider name without logging", orderItem =>
            {
                orderItem.ProviderName = providerName;
            }, doReindex, doSignal);
        }

        public void SetProviderOrderId(int nodeId, string providerOrderId, string eventId, bool doReindex = true, bool doSignal = true)
        {
            Mutate(nodeId, "set provider order ID", orderItem =>
            {
                if (orderItem.ProviderOrderId != providerOrderId)
                {
                    orderItem.ProviderOrderId = providerOrderId;
                    AppendLogItem(orderItem, "ORDER", "Beställningsnr ändrat till " + providerOrderId, eventId);
                }
            }, doReindex, doSignal);
        }

        public void SetPurchasedMaterial(int orderNodeId, int purchasedMaterialId, string eventId, bool doReindex = true, bool doSignal = true)
        {
            Mutate(orderNodeId, "set purchased material", orderItem =>
            {
                if (orderItem.PurchasedMaterialId != purchasedMaterialId)
                {
                    orderItem.PurchasedMaterialId = purchasedMaterialId;
                    AppendLogItem(orderItem, "MATERIALINKÖP", "Inköpt material ändrat till " + _orderConfig.GetValueById(purchasedMaterialId), eventId);
                }
            }, doReindex, doSignal);
        }

        public void SetReference(int nodeId, string reference, string eventId, bool doReindex = true, bool doSignal = true)
        {
            Mutate(nodeId, "set reference", orderItem =>
            {
                if (orderItem.Reference != reference)
                {
                    orderItem.Reference = reference;
                    AppendLogItem(orderItem, "REF", "Referens ändrad", eventId);
                }
            }, doReindex, doSignal);
        }

        /**
         * Manual anonymization.
         */
        public void SilentAnonymization(int nodeId, string reference, IList<LogItem> logs, string eventId, bool doReindex = true, bool doSignal = true)
        {
            Mutate(nodeId, "do silent anonymization", orderItem =>
            {
                if (reference != null)
                {
                    orderItem.Reference = reference;
                }
                foreach (var log in orderItem.LogItemsList)
                {
                    var editLogItem = logs.FirstOrDefault(x => x.Id == log.Id);
                    if (editLogItem != null)
                    {
                        log.Message = editLogItem.Message;
                    }
                }
                orderItem.IsAnonymized = true;
            }, doReindex, doSignal);
        }

        /**
         * Set all anonymization flags to false
         */
        public void ResetAllAnonymizationFlags(int nodeId, string eventId, bool doReindex = true, bool doSignal = true)
        {
            MutateConditional(nodeId, "reset all anonymization flags", orderItem =>
            {
                if (!orderItem.IsAnonymized && !orderItem.IsAnonymizedAutomatically)
                    return false;

                orderItem.IsAnonymized = false;
                orderItem.IsAnonymizedAutomatically = false;
                AppendLogItem(orderItem, "ANONYMISERING", "Anonymiseringsstatus ändrad till ej anonymiserad.", eventId);
                AppendLogItem(orderItem, "ANONYMISERING", "Automatisk anonymiseringsstatus ändrad till ej anonymiserad.", eventId);
                return true;
            }, doReindex, doSignal);
        }

        /**
         * Set anonymized flag
         */
        public void SetIsAnonymized(int nodeId, bool isAnonymized, string eventId, bool doReindex = true, bool doSignal = true)
        {
            MutateConditional(nodeId, "set anonymized flag to " + isAnonymized, orderItem =>
            {
                if (orderItem.IsAnonymized == isAnonymized)
                    return false;

                orderItem.IsAnonymized = isAnonymized;
                AppendLogItem(orderItem, "ANONYMISERING", "Anonymiseringsstatus ändrad till " + (orderItem.IsAnonymized ? "anonymiserad" : "ej anonymiserad") + ".", eventId);
                return true;
            }, doReindex, doSignal);
        }

        public void SetStatus(int orderNodeId, string statusPrevalue, string eventId, bool doReindex = true, bool doSignal = true)
        {
            var statusId = _orderConfig.GetIdByValue("OrderStatus", statusPrevalue);
            SetStatus(orderNodeId, statusId, eventId, doReindex, doSignal);
        }

        public void SetStatus(int orderNodeId, int statusId, string eventId, bool doReindex = true, bool doSignal = true)
        {
            Mutate(orderNodeId, "set status", orderItem =>
            {
                if (orderItem.StatusId != statusId)
                {
                    var currentStatus = orderItem.StatusId;
                    orderItem.PreviousStatusId = orderItem.StatusId;
                    orderItem.StatusId = statusId;
                    OnStatusChanged(orderItem, statusId);
                    AppendLogItem(orderItem, "STATUS", "Status ändrad från " + (currentStatus != -1 ? _orderConfig.GetValueById(currentStatus).Split(':').Last() : "Odefinierad") + " till " + _orderConfig.GetValueById(statusId).Split(':').Last(), eventId);
                }
            }, doReindex, doSignal);
        }

        public void SetType(int orderNodeId, int typeId, string eventId, bool doReindex = true, bool doSignal = true)
        {
            Mutate(orderNodeId, "set type", orderItem =>
            {
                if (orderItem.TypeId != typeId)
                {
                    orderItem.TypeId = typeId;
                    AppendLogItem(orderItem, "TYP", "Typ ändrad till " + _orderConfig.GetValueById(typeId), eventId);
                }
            }, doReindex, doSignal);
        }

        // The EF version's MakeDuplicate touched two entities (the new duplicate and a log entry
        // on the source) in one SaveChanges batch - this manager only tracks one order's pending
        // changes per thread at a time (see LoadForMutation), so the two are handled as separate,
        // sequential operations instead. Nothing calls MakeDuplicate with doReindex=false today,
        // so both honoring doReindex/doSignal for both halves is a safe, principled choice rather
        // than a guess.
        public void MakeDuplicate(int orderNodeId, string eventId, bool doReindex = true, bool doSignal = true)
        {
            var source = LoadOrderFromDisk(orderNodeId);
            if (source == null)
                throw new OrderItemNotFoundException("Failed to find order item when trying to set type.");

            var sourceOrderId = source.OrderId;

            // Deep-copy via round-tripping through JSON so the duplicate doesn't share any
            // reference-typed fields (LogItemsList, AttachmentList, SierraInfo) with the source -
            // AsNoTracking() served the same purpose in the EF version.
            var duplicate = JsonConvert.DeserializeObject<OrderItemModel>(JsonConvert.SerializeObject(source));
            var tempOrderId = "cthb-" + Helpers.CalculateMD5Hash(DateTime.Now.Ticks.ToString());
            duplicate.Reference = duplicate.Reference + "\n\nKopia av " + sourceOrderId + ".";

            var threadId = Thread.CurrentThread.ManagedThreadId;
            try
            {
                var newNodeId = _nodeIdGenerator.Next();
                duplicate.NodeId = newNodeId;
                duplicate.OrderId = tempOrderId.Substring(0, 13) + "-" + newNodeId.ToString();
                duplicate.CreateDate = DateTime.Now;
                duplicate.UpdateDate = DateTime.Now;
                AppendLogItem(duplicate, "DUPLICERING", "Skapade denna order som en kopia av " + sourceOrderId + ".", eventId);

                AcquireLock(newNodeId);
                _threadIdToPendingOrder[threadId] = new PendingOrder { NodeId = newNodeId, Item = duplicate, IsNew = true };
                // The duplicate always flushes immediately, regardless of doReindex - it's a brand
                // new order with nothing else pending against its NodeId, so there is no later call
                // that could plausibly flush it, unlike an edit to an already-existing order.
                Flush(threadId, doReindex: true, doSignal: true);
            }
            catch
            {
                DiscardPending(threadId);
                throw;
            }

            AddLogItem(orderNodeId, "DUPLICERING", "Kopia " + duplicate.OrderId + " skapad med denna order som källa.", eventId, doReindex, doSignal);
        }

        public void SetEditedByData(int orderNodeId, string memberId, string memberName, bool doReindex = true, bool doSignal = true)
        {
            Mutate(orderNodeId, "set type", orderItem =>
            {
                orderItem.EditedBy = memberId;
                orderItem.EditedByMemberName = memberName;
            }, doReindex, doSignal);
        }

        public void SetReadOnlyAtLibrary(int nodeId, bool readOnlyAtLibrary, string eventId, bool doReindex = true, bool doSignal = true)
        {
            Mutate(nodeId, "set read only at library", orderItem =>
            {
                orderItem.ReadOnlyAtLibrary = readOnlyAtLibrary;
                AppendLogItem(orderItem, "LÄSESALSLÅN", "Läsesalslån satt till \"" + readOnlyAtLibrary + "\".", eventId);
            }, doReindex, doSignal);
        }

        /**
         * Automatic anonymization.
         */
        public void AnonymizeOrder(int nodeId, string eventId, bool doReindex = true, bool doSignal = true)
        {
            var mailNoteMessageAlts = new string[] {
                "Skickat mail till",
                "E-post mot låntagare ändrad till",
                "Skickat automatiskt leveransmail till",
                "Skickat automatiskt \"courtesy notice\" till",
                "Skickat automatiskt påminnelsemail nummer ett till",
                "Skickat automatiskt påminnelsemail nummer två till",
                "Skickat automatiskt påminnelsemail nummer tre till",
                "Skickat automatiskt leveransmail till",
                "Svar från",
                "Svar från",
                "Leverans från",
                "PatronEmail ändrad till"
            };

            Mutate(nodeId, "anonymize order", orderItem =>
            {
                orderItem.OriginalOrder = "ANONYMIZED";
                orderItem.PatronName = "ANONYMIZED";
                orderItem.PatronCardNo = "ANONYMIZED";
                orderItem.PatronEmail = "ANONYMIZED";

                if (orderItem.LogItemsList != null)
                {
                    foreach (var logItem in orderItem.LogItemsList)
                    {
                        if (logItem.Type == "MAIL_NOTE")
                        {
                            logItem.Message = Regex.Replace(logItem.Message, "^(" + String.Join("|", mailNoteMessageAlts) + ").*$", "$1 ANONYMIZED");
                        }
                        if (logItem.Type == "SIERRA")
                        {
                            logItem.Message = "ANONYMIZED";
                        }
                        if (logItem.Type == "MAIL")
                        {
                            logItem.Message = "ANONYMIZED";
                        }
                    }
                }

                if (orderItem.SierraInfo != null)
                {
                    orderItem.SierraInfo.id = "ANONYMIZED";
                    orderItem.SierraInfo.barcode = "ANONYMIZED";
                    orderItem.SierraInfo.pnum = "ANONYMIZED";
                    orderItem.SierraInfo.email = "ANONYMIZED";
                    orderItem.SierraInfo.first_name = "ANONYMIZED";
                    orderItem.SierraInfo.last_name = "ANONYMIZED";
                    orderItem.SierraInfo.adress = new List<SierraAddressModel>();
                    orderItem.SierraInfo.cid = "ANONYMIZED";
                    orderItem.SierraInfoStr = JsonConvert.SerializeObject(orderItem.SierraInfo);
                }

                orderItem.IsAnonymizedAutomatically = true;
            }, doReindex, doSignal);
        }

        #endregion

        #region Private helpers - status/type side effects (unchanged business logic)

        private void OnStatusChanged(OrderItemModel orderItem, int newStatusId)
        {
            UpdateLastDeliveryStatusWhenProper(orderItem, newStatusId);
            UpdateDeliveryDateWhenProper(orderItem, newStatusId);
        }

        private void UpdateLastDeliveryStatusWhenProper(OrderItemModel orderItem, int newStatusId)
        {
            var statusStr = _orderConfig.GetValueById(newStatusId).Split(':').Last();
            if (statusStr.Contains("Levererad") || statusStr.Contains("Utlånad") || statusStr.Contains("Transport") || statusStr.Contains("Infodisk") || statusStr.Contains("FOLIO"))
            {
                orderItem.LastDeliveryStatusId = newStatusId;
            }
        }

        private void UpdateDeliveryDateWhenProper(OrderItemModel orderItem, int newStatusId)
        {
            var deliveryDateStr = orderItem.DeliveryDate == null ? "" : orderItem.DeliveryDate.ToString();
            var deliveryDate = deliveryDateStr == "" ? new DateTime(1970, 1, 1) : Convert.ToDateTime(deliveryDateStr);
            var statusStr = _orderConfig.GetValueById(newStatusId).Split(':').Last();
            if (deliveryDate.Year == 1970 && (statusStr.Contains("Levererad") || statusStr.Contains("Utlånad") || statusStr.Contains("Transport") ||
                    statusStr.Contains("Infodisk")))
            {
                orderItem.DeliveryDate = DateTime.Now;
            }
        }

        private bool IsDeliveryLibrarySameAsHomeLibrary(OrderItemModel orderItem)
        {
            return orderItem.SierraInfo.home_library == null ||
                (orderItem.DeliveryLibrary == "Huvudbiblioteket" && orderItem.SierraInfo.home_library.Contains("hbib")) ||
                (orderItem.DeliveryLibrary == "Lindholmenbiblioteket" && orderItem.SierraInfo.home_library.Contains("lbib")) ||
                (orderItem.DeliveryLibrary == "Arkitekturbiblioteket" && orderItem.SierraInfo.home_library.Contains("abib"));
        }

        private string GetCurrentUserOrSystem()
        {
            var identity = _httpContextAccessor.HttpContext?.User?.Identity;
            if (identity != null && identity.IsAuthenticated)
                return identity.Name;
            return "System";
        }

        private string UrlDecodeAndEscapeAllLinks(string str)
        {
            var res = "";
            var regex = new Regex(@"((?:https?|ftp|file)(?::|%3a)(?:\/|%2f)(?:\/|%2f)[-a-zA-Z0-9+&@#\/%?=~_|!:,.;()]*[-a-zA-Z0-9+&@#()\/%=~_|()])");
            var match = regex.Match(str);
            res = System.Net.WebUtility.UrlDecode(str);
            for (int i = 1; i < match.Groups.Count; i++)
            {
                var urlDecodedUrlStr = System.Net.WebUtility.UrlDecode(match.Groups[i].ToString());
                res = res.Replace(urlDecodedUrlStr, Uri.EscapeUriString(urlDecodedUrlStr));
            }
            return res;
        }

        // Always run right before a save (see Flush) rather than only where the EF version
        // happened to remember to call it - a mutated *Id (e.g. DeliveryLibraryId) with no matching
        // FillOutStuff call left the derived string field (DeliveryLibrary) stale in what got
        // persisted/indexed, until some *other* call touched the order and finally recomputed it.
        // Idempotent and cheap, so running it unconditionally on every save is a pure correctness
        // improvement (fas 7 rewrite), not a behavior change worth gating.
        private void FillOutStuff(OrderItemModel orderItem)
        {
            orderItem.Log = JsonConvert.SerializeObject(orderItem.LogItemsList);
            orderItem.Attachments = JsonConvert.SerializeObject(orderItem.AttachmentList);

            orderItem.FollowUpDateIsDue = orderItem.FollowUpDate <= DateTime.Now ? true : false;

            if (orderItem.StatusId != -1)
            {
                var v = _orderConfig.GetValueById(orderItem.StatusId);
                if (!string.IsNullOrEmpty(v)) orderItem.Status = v;
            }
            orderItem.StatusString = (orderItem.Status ?? "").Split(':').Last();

            if (orderItem.PreviousStatusId != -1)
            {
                var v = _orderConfig.GetValueById(orderItem.PreviousStatusId);
                if (!string.IsNullOrEmpty(v)) orderItem.PreviousStatus = v;
            }
            orderItem.PreviousStatusString = (orderItem.PreviousStatus ?? "").Split(':').Last();

            if (orderItem.LastDeliveryStatusId != -1)
            {
                var v = _orderConfig.GetValueById(orderItem.LastDeliveryStatusId);
                if (!string.IsNullOrEmpty(v)) orderItem.LastDeliveryStatus = v;
            }
            orderItem.LastDeliveryStatusString = (orderItem.LastDeliveryStatus ?? "").Split(':').Last();

            if (orderItem.TypeId != -1)
            {
                var v = _orderConfig.GetValueById(orderItem.TypeId);
                if (!string.IsNullOrEmpty(v)) orderItem.Type = v;
            }
            else
            {
                orderItem.Type = "";
            }

            if (orderItem.DeliveryLibraryId != -1)
            {
                var v = _orderConfig.GetValueById(orderItem.DeliveryLibraryId);
                if (!string.IsNullOrEmpty(v)) orderItem.DeliveryLibrary = v;
            }
            else
            {
                orderItem.DeliveryLibrary = "";
            }

            if (orderItem.CancellationReasonId != -1)
            {
                var v = _orderConfig.GetValueById(orderItem.CancellationReasonId);
                if (!string.IsNullOrEmpty(v)) orderItem.CancellationReason = v;
            }
            else
            {
                orderItem.CancellationReason = "";
            }

            if (orderItem.PurchasedMaterialId != -1)
            {
                var v = _orderConfig.GetValueById(orderItem.PurchasedMaterialId);
                if (!string.IsNullOrEmpty(v)) orderItem.PurchasedMaterial = v;
            }
            else
            {
                orderItem.PurchasedMaterial = "";
            }

            orderItem.EditedByCurrentMember = false;
            orderItem.ContentVersionsCount = 0;
            orderItem.DeliveryLibrarySameAsHomeLibrary = IsDeliveryLibrarySameAsHomeLibrary(orderItem);
        }

        private string GetPrettyLibraryNameFromLibraryAbbreviation(string libraryName)
        {
            var res = OrderItemModel.LIBRARY_UNKNOWN_PRETTY_STRING;
            if (libraryName != null && libraryName.Contains("hbib"))
            {
                res = OrderItemModel.LIBRARY_Z_PRETTY_STRING;
            }
            else if (libraryName != null && libraryName.Contains("lbib"))
            {
                res = OrderItemModel.LIBRARY_ZL_PRETTY_STRING;
            }
            else if (libraryName != null && libraryName.Contains("abib"))
            {
                res = OrderItemModel.LIBRARY_ZA_PRETTY_STRING;
            }
            return res;
        }

        private void AppendLogItem(OrderItemModel orderItem, string type, string message, string eventId)
        {
            if (String.IsNullOrEmpty(message))
                return;

            if (orderItem.LogItemsList == null)
                orderItem.LogItemsList = new List<LogItem>();

            orderItem.LogItemsList.Add(new LogItem
            {
                // Was EF's [DatabaseGenerated(Identity)] - SilentAnonymization matches log items
                // back to their originals by Id, so it must be set explicitly now.
                Id = Guid.NewGuid(),
                MemberName = GetCurrentUserOrSystem(),
                Type = type,
                Message = message,
                CreateDate = DateTime.Now,
                EventId = eventId
            });
        }

        #endregion

        #region Private helpers - storage, batching, locking

        private string BucketDirectory(int nodeId) => Path.Combine(_ordersDirectory, (nodeId / 1000).ToString("D3"));
        private string OrderFilePath(int nodeId) => Path.Combine(BucketDirectory(nodeId), nodeId + ".json");

        private OrderItemModel LoadOrderFromDisk(int nodeId)
        {
            var path = OrderFilePath(nodeId);
            if (!File.Exists(path))
                return null;

            return JsonConvert.DeserializeObject<OrderItemModel>(File.ReadAllText(path));
        }

        private void SaveOrderToDisk(OrderItemModel orderItem)
        {
            var directory = BucketDirectory(orderItem.NodeId);
            Directory.CreateDirectory(directory);

            var path = OrderFilePath(orderItem.NodeId);
            var json = JsonConvert.SerializeObject(orderItem, Formatting.Indented);

            var tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(tempPath, json);
            if (File.Exists(path))
                File.Replace(tempPath, path, null);
            else
                File.Move(tempPath, path);
        }

        // A request for the SAME order that's already pending on this thread reuses the in-memory
        // (possibly still-unsaved) instance - that's what lets a chain of doReindex=false calls
        // accumulate into one flush. A request for a DIFFERENT order while one is still pending
        // would mean two units of work interleaved on one thread, which nothing in this codebase
        // does today (every batched sequence targets a single nodeId) - fail loudly rather than
        // silently drop the first order's unflushed changes if that assumption is ever wrong.
        private OrderItemModel LoadForMutation(int nodeId, int threadId)
        {
            if (_threadIdToPendingOrder.TryGetValue(threadId, out var pending))
            {
                if (pending.NodeId != nodeId)
                {
                    throw new InvalidOperationException(
                        "FileOrderItemManager: order " + pending.NodeId + " still has unflushed changes on this thread " +
                        "while a mutation for order " + nodeId + " was requested. Chained doReindex=false calls must " +
                        "all target the same order.");
                }
                return pending.Item;
            }

            var item = LoadOrderFromDisk(nodeId);
            if (item == null)
                return null;

            AcquireLock(nodeId);
            _threadIdToPendingOrder[threadId] = new PendingOrder { NodeId = nodeId, Item = item, IsNew = false };
            return item;
        }

        private OrderItemModel LoadForRead(int nodeId)
        {
            var threadId = Thread.CurrentThread.ManagedThreadId;
            if (_threadIdToPendingOrder.TryGetValue(threadId, out var pending) && pending.NodeId == nodeId)
            {
                return pending.Item;
            }

            return LoadOrderFromDisk(nodeId);
        }

        private void Mutate(int nodeId, string notFoundContext, Action<OrderItemModel> mutate, bool doReindex, bool doSignal)
        {
            var threadId = Thread.CurrentThread.ManagedThreadId;
            try
            {
                var orderItem = LoadForMutation(nodeId, threadId);
                if (orderItem == null)
                    throw new OrderItemNotFoundException("Failed to find order item when trying to " + notFoundContext + ".");

                mutate(orderItem);
                Flush(threadId, doReindex, doSignal);
            }
            catch
            {
                DiscardPending(threadId);
                throw;
            }
        }

        // For the two methods (ResetAllAnonymizationFlags, SetIsAnonymized) whose EF version only
        // saved when the flag actually flipped - mutate returns false to mean "nothing to persist".
        private void MutateConditional(int nodeId, string notFoundContext, Func<OrderItemModel, bool> mutateIfChanged, bool doReindex, bool doSignal)
        {
            var threadId = Thread.CurrentThread.ManagedThreadId;
            try
            {
                var orderItem = LoadForMutation(nodeId, threadId);
                if (orderItem == null)
                    throw new OrderItemNotFoundException("Failed to find order item when trying to " + notFoundContext + ".");

                var changed = mutateIfChanged(orderItem);
                if (changed)
                {
                    Flush(threadId, doReindex, doSignal);
                }
                else if (doReindex)
                {
                    // Nothing to save, but this call was meant to finalize - release the slot,
                    // matching the EF version disposing its (in this case entirely clean) DbContext.
                    DiscardPending(threadId);
                }
            }
            catch
            {
                DiscardPending(threadId);
                throw;
            }
        }

        private int PersistNewOrder(OrderItemModel newOrderItem, string orderId, bool doReindex, bool doSignal)
        {
            var threadId = Thread.CurrentThread.ManagedThreadId;
            try
            {
                var nodeId = _nodeIdGenerator.Next();
                newOrderItem.NodeId = nodeId;
                if (orderId != null)
                {
                    newOrderItem.OrderId = orderId.Substring(0, 13) + "-" + nodeId.ToString();
                }
                newOrderItem.CreateDate = DateTime.Now;
                newOrderItem.UpdateDate = DateTime.Now;

                AcquireLock(nodeId);
                _threadIdToPendingOrder[threadId] = new PendingOrder { NodeId = nodeId, Item = newOrderItem, IsNew = true };

                Flush(threadId, doReindex, doSignal);

                return nodeId;
            }
            catch
            {
                DiscardPending(threadId);
                throw;
            }
        }

        private void Flush(int threadId, bool doReindex, bool doSignal)
        {
            if (!doReindex)
                return; // stays buffered in _threadIdToPendingOrder for a later call to flush

            if (!_threadIdToPendingOrder.TryGetValue(threadId, out var pending))
                return;

            FillOutStuff(pending.Item);
            SaveOrderToDisk(pending.Item);

            if (pending.IsNew)
            {
                _orderItemSearcher.Added(pending.Item);
                if (!string.IsNullOrEmpty(pending.Item.OrderId))
                {
                    _orderIdIndex.Register(pending.Item.OrderId, pending.Item.NodeId);
                }
            }
            else
            {
                _orderItemSearcher.Modified(pending.Item);
            }

            _threadIdToPendingOrder.Remove(threadId);
            ReleaseLock(pending.NodeId);

            if (doSignal)
            {
                _notifier.ReportNewOrderItemUpdate(pending.Item);
            }
        }

        private void DiscardPending(int threadId)
        {
            if (_threadIdToPendingOrder.TryGetValue(threadId, out var pending))
            {
                _threadIdToPendingOrder.Remove(threadId);
                ReleaseLock(pending.NodeId);
            }
        }

        // SemaphoreSlim rather than `lock`/Monitor: Enter and Exit for one order's read-modify-write
        // cycle can happen on different calls (though always the same thread in this codebase -
        // synchronous, single-instance), and Monitor requires the *same* call frame to release what
        // it acquired.
        private void AcquireLock(int nodeId) => _orderLocks.GetOrAdd(nodeId, _ => new SemaphoreSlim(1, 1)).Wait();
        private void ReleaseLock(int nodeId)
        {
            if (_orderLocks.TryGetValue(nodeId, out var semaphore))
            {
                semaphore.Release();
            }
        }

        #endregion
    }
}
