using System;
using System.Collections.Generic;
using System.Linq;
using System.Data.Entity;
using Chalmers.ILL.Models;
using Chalmers.ILL.Models.Mail;
using Chalmers.ILL.Database;
using Newtonsoft.Json;
using Chalmers.ILL.Utilities;
using System.Text.RegularExpressions;
using Chalmers.ILL.UmbracoApi;
using Chalmers.ILL.SignalR;
using System.Threading;
using static Chalmers.ILL.Models.OrderItemModel;
using Nest;
using Microsoft.AspNetCore.Http;

namespace Chalmers.ILL.OrderItems
{
    public class EntityFrameworkOrderItemManager : IOrderItemManager
    {
        private static readonly log4net.ILog _log = log4net.LogManager.GetLogger(typeof(EntityFrameworkOrderItemManager));

        private INotifier _notifier;
        private IChillinOrderConfiguration _orderConfig;
        private Random _rand;
        private IOrderItemSearcher _orderItemSearcher;
        private readonly IHttpContextAccessor _httpContextAccessor;

        private Dictionary<int, OrderItemsDbContext> _threadIdToDbContextMap = new Dictionary<int, OrderItemsDbContext>();

        // httpContextAccessor is optional (defaults to a fresh accessor) because this class is
        // still constructed manually in Bootstrapper rather than resolved through DI (fas 6).
        // HttpContextAccessor's backing store is a static AsyncLocal, so any instance sees the
        // current request regardless of who constructed it - see fas 2's "Ersätt HttpContext.Current".
        public EntityFrameworkOrderItemManager(IChillinOrderConfiguration orderConfig, IOrderItemSearcher orderItemSearcher, IHttpContextAccessor httpContextAccessor = null)
        {
            _orderConfig = orderConfig;
            _orderItemSearcher = orderItemSearcher;
            _rand = new Random();
            _httpContextAccessor = httpContextAccessor ?? new HttpContextAccessor();
        }

        public List<LogItem> GetLogItems(int nodeId)
        {
            var res = new List<LogItem>();
            EnsureDatabaseContext();
            try
            {
                var orderItem = _threadIdToDbContextMap[Thread.CurrentThread.ManagedThreadId].OrderItems
                    .Where(x => x.NodeId == nodeId)
                    .Include(x => x.LogItemsList)
                    .FirstOrDefault();
                if (orderItem != null)
                {
                    res = orderItem.LogItemsList;
                }
                else
                {
                    throw new OrderItemNotFoundException("Failed to find order item when trying to fetch log items.");
                }
                return res;
            }
            finally
            {
                DisposeDatabaseContext(true);
            }
        }

        public OrderItemModel GetOrderItem(int nodeId)
        {
            OrderItemModel res = null;
            EnsureDatabaseContext();
            try
            {
                try
                {
                    var orderItems = _threadIdToDbContextMap[Thread.CurrentThread.ManagedThreadId].OrderItems
                        .Where(x => x.NodeId == nodeId)
                        .Include(x => x.AttachmentList)
                        .Include(x => x.LogItemsList)
                        .Include(x => x.SierraInfo)
                        .Include(x => x.SierraInfo.adress)
                        .ToList();

                    foreach (var orderItem in orderItems)
                    {
                        orderItem.AttachmentList = orderItem.AttachmentList.OrderBy(x => x.Title).ToList();
                        orderItem.LogItemsList = orderItem.LogItemsList.OrderByDescending(x => x.CreateDate).ToList();

                        if (String.IsNullOrWhiteSpace(orderItem.SierraInfo.home_library_pretty_name))
                        {
                            orderItem.SierraInfo.home_library_pretty_name = GetPrettyLibraryNameFromLibraryAbbreviation(orderItem.SierraInfo.home_library);
                        }
                    }

                    if (orderItems.Count() == 0)
                    {
                        _log.Warn("GetOrderItem: Couldn't find any node with the ID " + nodeId + ".");
                    }
                    else if (orderItems.Count() > 1)
                    {
                        // should never happen
                        _log.Warn("GetOrderItem: Found more than one node with the ID " + nodeId + ".");
                    }
                    else
                    {
                        res = orderItems.Single();

                        FillOutStuff(res);
                    }
                }
                catch (Exception e)
                {
                    _log.Error("Failed to query node.", e);
                }

                return res;
            }
            finally
            {
                DisposeDatabaseContext(true);
            }
        }

        public OrderItemModel GetOrderItem(string orderId)
        {
            OrderItemModel res = null;
            EnsureDatabaseContext();
            try
            {
                try
                {
                    var orderItems = _threadIdToDbContextMap[Thread.CurrentThread.ManagedThreadId].OrderItems
                        .Where(x => x.OrderId == orderId)
                        .Include(x => x.AttachmentList)
                        .Include(x => x.LogItemsList)
                        .Include(x => x.SierraInfo)
                        .Include(x => x.SierraInfo.adress)
                        .ToList();

                    foreach (var orderItem in orderItems)
                    {
                        orderItem.AttachmentList = orderItem.AttachmentList.OrderBy(x => x.Title).ToList();
                        orderItem.LogItemsList = orderItem.LogItemsList.OrderByDescending(x => x.CreateDate).ToList();

                        if (String.IsNullOrWhiteSpace(orderItem.SierraInfo.home_library_pretty_name))
                        {
                            orderItem.SierraInfo.home_library_pretty_name = GetPrettyLibraryNameFromLibraryAbbreviation(orderItem.SierraInfo.home_library);
                        }
                    }

                    if (orderItems.Count() == 0)
                    {
                        _log.Warn("GetOrderItem: Couldn't find any node with the order ID " + orderId + ".");
                    }
                    else if (orderItems.Count() > 1)
                    {
                        // should never happen
                        _log.Warn("GetOrderItem: Found more than one node with the order ID " + orderId + ".");
                    }
                    else
                    {
                        res = orderItems.Single();

                        FillOutStuff(res);
                    }
                }
                catch (Exception e)
                {
                    _log.Error("Failed to query node.", e);
                }

                return res;
            }
            finally
            {
                DisposeDatabaseContext(true);
            }
        }

        public IEnumerable<OrderItemModel> GetLockedOrderItems(string memberId)
        {
            EnsureDatabaseContext();
            try
            {
                return _threadIdToDbContextMap[Thread.CurrentThread.ManagedThreadId].OrderItems
                    .Where(x => x.EditedBy == memberId)
                    .ToList();
            }
            finally
            {
                DisposeDatabaseContext(true);
            }
        }

        public void SetNotifier(INotifier notifier)
        {
            _notifier = notifier;
        }

        public void AddExistingMediaItemAsAnAttachment(int orderNodeId, string mediaNodeId, string title, string link, string eventId, bool doReindex = true, bool doSignal = true)
        {
            EnsureDatabaseContext();
            try
            {
                var orderItem = GetOrderItemFromEntityFramework(orderNodeId);
                if (orderItem != null)
                {
                    if (!String.IsNullOrEmpty(title) && !String.IsNullOrEmpty(link))
                    {
                        if (orderItem.AttachmentList == null)
                        {
                            orderItem.AttachmentList = new List<OrderAttachment>();
                        }

                        var att = new OrderAttachment();
                        att.Title = title;
                        att.Link = link;
                        att.MediaItemNodeId = mediaNodeId;
                        orderItem.AttachmentList.Add(att);
                        FillOutStuff(orderItem);

                        AddLogItem(orderNodeId, "ATTACHMENT", "Nytt dokument bundet till ordern.", eventId, false, false);
                    }

                    MaybeSaveToDatabase(doReindex, doSignal ? orderItem : null);
                }
                else
                {
                    throw new OrderItemNotFoundException("Failed to find order item when trying to add existing media item as an attachment.");
                }
            }
            catch (Exception)
            {
                DisposeDatabaseContext(true);
                throw;
            }
            finally
            {
                DisposeDatabaseContext(doReindex);
            }
        }

        public void AddExistingMediaItemAsAnAttachmentWithoutLogging(int orderNodeId, string mediaNodeId, string title, string link, bool doReindex = true, bool doSignal = true)
        {
            EnsureDatabaseContext();
            try
            {
                var orderItem = GetOrderItemFromEntityFramework(orderNodeId);
                if (orderItem != null)
                {
                    if (!String.IsNullOrEmpty(title) && !String.IsNullOrEmpty(link))
                    {
                        if (orderItem.AttachmentList == null)
                        {
                            orderItem.AttachmentList = new List<OrderAttachment>();
                        }

                        var att = new OrderAttachment();
                        att.Title = title;
                        att.Link = link;
                        att.MediaItemNodeId = mediaNodeId;
                        orderItem.AttachmentList.Add(att);
                        FillOutStuff(orderItem);
                    }

                    MaybeSaveToDatabase(doReindex, doSignal ? orderItem : null);
                }
                else
                {
                    throw new OrderItemNotFoundException("Failed to find order item when trying to add existing media item as an attachment without logging.");
                }
            }
            catch (Exception)
            {
                DisposeDatabaseContext(true);
                throw;
            }
            finally
            {
                DisposeDatabaseContext(doReindex);
            }
        }

        public void AddLogItem(int OrderItemNodeId, string Type, string Message, string eventId, bool doReindex = true, bool doSignal = true)
        {
            EnsureDatabaseContext();
            try
            {
                var orderItem = GetOrderItemFromEntityFramework(OrderItemNodeId);
                if (orderItem != null)
                {
                    if (!String.IsNullOrEmpty(Message))
                    {
                        if (orderItem.LogItemsList == null)
                        {
                            orderItem.LogItemsList = new List<LogItem>();
                        }

                        LogItem newLog = new LogItem
                        {
                            MemberName = GetCurrentUserOrSystem(),
                            Type = Type,
                            Message = Message,
                            CreateDate = DateTime.Now,
                            EventId = eventId
                        };

                        orderItem.LogItemsList.Add(newLog);
                        FillOutStuff(orderItem);
                    }

                    MaybeSaveToDatabase(doReindex, doSignal ? orderItem : null);
                }
                else
                {
                    throw new OrderItemNotFoundException("Failed to find order item when trying to add log item.");
                }
            }
            catch (Exception)
            {
                DisposeDatabaseContext(true);
                throw;
            }
            finally
            {
                DisposeDatabaseContext(doReindex);
            }
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
            EnsureDatabaseContext();

            try
            {
                _threadIdToDbContextMap[Thread.CurrentThread.ManagedThreadId].OrderItems.Add(orderItem);

                MaybeSaveToDatabase(doReindex, doSignal ? orderItem : null);

                return orderItem.NodeId;
            }
            catch (Exception)
            {
                DisposeDatabaseContext(true);
                throw;
            }
            finally
            {
                DisposeDatabaseContext(doReindex);
            }
        }

        public int CreateOrderItemInDbFromMailQueueModel(MailQueueModel model, bool doReindex = true, bool doSignal = true)
        {
            EnsureDatabaseContext();

            try
            {
                // Temporary OrderId with MD5 Hash
                var orderId = "cthb-" + Helpers.CalculateMD5Hash(DateTime.Now.Ticks.ToString());
                var contentName = orderId;

                var newOrderItem = new OrderItemModel();

                // Set properties
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

                // Set Type directly if "IsPurchaseRequest" is true
                if (model.IsPurchaseRequest)
                {
                    newOrderItem.TypeId = _orderConfig.GetIdByValue("OrderType", "Inköpsförslag");
                    newOrderItem.Type = "Inköpsförslag";
                }

                FillOutStuff(newOrderItem);

                _threadIdToDbContextMap[Thread.CurrentThread.ManagedThreadId].OrderItems.Add(newOrderItem);

                // Save the OrderItem to get an Id
                SaveToDatabase(null, false);

                // Shorten the OrderId and include the NodeId
                newOrderItem.OrderId = orderId.Substring(0, 13) + "-" + newOrderItem.NodeId.ToString();

                // Save
                MaybeSaveToDatabase(doReindex, doSignal ? newOrderItem : null);

                return newOrderItem.NodeId;
            }
            catch (Exception)
            {
                DisposeDatabaseContext(true);
                throw;
            }
            finally
            {
                DisposeDatabaseContext(doReindex);
            }
        }

        public int CreateOrderItemInDbFromOrderItemSeedModel(OrderItemSeedModel model, bool doReindex = true, bool doSignal = true)
        {
            EnsureDatabaseContext();

            try
            {
                // Temporary OrderId with MD5 Hash
                var orderId = "cthb-" + Helpers.CalculateMD5Hash(DateTime.Now.Ticks.ToString());
                var contentName = orderId;

                var newOrderItem = new OrderItemModel();

                // Set properties
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

                FillOutStuff(newOrderItem);

                _threadIdToDbContextMap[Thread.CurrentThread.ManagedThreadId].OrderItems.Add(newOrderItem);

                // Save the OrderItem to get an Id
                SaveToDatabase(null, false);

                // Shorten the OrderId and include the NodeId
                newOrderItem.OrderId = orderId.Substring(0, 13) + "-" + newOrderItem.NodeId.ToString();

                // Save
                MaybeSaveToDatabase(doReindex, doSignal ? newOrderItem : null);

                return newOrderItem.NodeId;
            }
            catch (Exception)
            {
                DisposeDatabaseContext(true);
                throw;
            }
            finally
            {
                DisposeDatabaseContext(doReindex);
            }
        }

        public string GenerateEventId(int type)
        {
            return "event-" + _rand.Next(0, 65535).ToString("X4") + "-" + type.ToString("D2");
        }

        public void RemoveConnectionToMediaItem(int orderNodeId, string mediaNodeId, bool doReindex = true, bool doSignal = true)
        {
            EnsureDatabaseContext();
            try
            {
                var orderItem = GetOrderItemFromEntityFramework(orderNodeId);
                if (orderItem != null)
                {
                    orderItem.AttachmentList.RemoveAll(i => i.MediaItemNodeId == mediaNodeId);
                    MaybeSaveToDatabase(doReindex, doSignal ? orderItem : null);
                }
                else
                {
                    throw new OrderItemNotFoundException("Failed to find order item when trying to remove connection to media item.");
                }
            }
            catch (Exception)
            {
                DisposeDatabaseContext(true);
                throw;
            }
            finally
            {
                DisposeDatabaseContext(doReindex);
            }
        }

        public void SaveWithoutEventsAndWithSynchronousReindexing(int nodeId, bool doReindex = true, bool doSignal = true)
        {
            EnsureDatabaseContext();
            try
            {
                var orderItem = GetOrderItemFromEntityFramework(nodeId);

                FillOutStuff(orderItem);

                MaybeSaveToDatabase(doReindex, doSignal ? orderItem : null);
            }
            finally
            {
                DisposeDatabaseContext(doReindex);
            }
        }

        public void SetBookId(int nodeId, string bookId, string eventId, bool doReindex = true, bool doSignal = true)
        {
            EnsureDatabaseContext();
            try
            {
                var orderItem = GetOrderItemFromEntityFramework(nodeId);
                if (orderItem != null)
                {
                    if (orderItem.BookId != bookId)
                    {
                        orderItem.BookId = bookId;
                        AddLogItem(nodeId, "BOKINFO", "Bok-ID ändrat till " + bookId + ".", eventId, false, false);
                    }
                    MaybeSaveToDatabase(doReindex, doSignal ? orderItem : null);
                }
                else
                {
                    throw new OrderItemNotFoundException("Failed to find order item when trying to set book ID.");
                }
            }
            catch (Exception)
            {
                DisposeDatabaseContext(true);
                throw;
            }
            finally
            {
                DisposeDatabaseContext(doReindex);
            }
        }

        public void SetTitleInformation(int nodeId, string titleInformation, string eventId, bool doReindex = true, bool doSignal = true)
        {
            EnsureDatabaseContext();
            try
            {
                var orderItem = GetOrderItemFromEntityFramework(nodeId);
                if (orderItem != null)
                {
                    if (orderItem.TitleInformation != titleInformation)
                    {
                        orderItem.TitleInformation = titleInformation;
                     //   AddLogItem(nodeId, "BOKINFO", "Bok-ID ändrat till " + bookId + ".", eventId, false, false);
                    }
                    MaybeSaveToDatabase(doReindex, doSignal ? orderItem : null);
                }
                else
                {
                    throw new OrderItemNotFoundException("Failed to find order item when trying to set title information.");
                }
            }
            catch (Exception)
            {
                DisposeDatabaseContext(true);
                throw;
            }
            finally
            {
                DisposeDatabaseContext(doReindex);
            }
        }

        public void SetCancellationReason(int orderNodeId, int cancellationReasonId, string eventId, bool doReindex = true, bool doSignal = true)
        {
            EnsureDatabaseContext();
            try
            {
                var orderItem = GetOrderItemFromEntityFramework(orderNodeId);
                if (orderItem != null)
                {
                    if (orderItem.CancellationReasonId != cancellationReasonId)
                    {
                        orderItem.CancellationReasonId = cancellationReasonId;
                        FillOutStuff(orderItem);
                        AddLogItem(orderNodeId, "ANNULLERINGSORSAK", "Annulleringsorsak ändrad till " + _orderConfig.GetValueById(cancellationReasonId), eventId, false, false);
                    }
                    MaybeSaveToDatabase(doReindex, doSignal ? orderItem : null);
                }
                else
                {
                    throw new OrderItemNotFoundException("Failed to find order item when trying to set cancellation reason.");
                }
            }
            catch (Exception)
            {
                DisposeDatabaseContext(true);
                throw;
            }
            finally
            {
                DisposeDatabaseContext(doReindex);
            }
        }

        public void SetDeliveryLibrary(int orderNodeId, string deliveryLibraryPrevalue, string eventId, bool doReindex = true, bool doSignal = true)
        {
            var deliveryLibraryId = _orderConfig.GetIdByValue("DeliveryLibrary", deliveryLibraryPrevalue);
            SetDeliveryLibrary(orderNodeId, deliveryLibraryId, eventId, doReindex, doSignal);
        }

        public void SetDeliveryLibrary(int orderNodeId, int deliveryLibraryId, string eventId, bool doReindex = true, bool doSignal = true)
        {
            EnsureDatabaseContext();
            try
            {
                var orderItem = GetOrderItemFromEntityFramework(orderNodeId);
                if (orderItem != null)
                {
                    var currentDeliveryLibrary = orderItem.DeliveryLibraryId;
                    if (currentDeliveryLibrary != deliveryLibraryId)
                    {
                        orderItem.DeliveryLibraryId = deliveryLibraryId;
                        var fromLib = currentDeliveryLibrary != -1 ? _orderConfig.GetValueById(currentDeliveryLibrary).Split(':').Last() : "Odefinierad";
                        if (fromLib == "Lindholmenbiblioteket") fromLib = "Kuggen";
                        var toLib = _orderConfig.GetValueById(deliveryLibraryId).Split(':').Last();
                        if (toLib == "Lindholmenbiblioteket") toLib = "Kuggen";
                        AddLogItem(orderNodeId, "BIBLIOTEK", "Leveransbibliotek ändrat från " + fromLib + " till " + toLib, eventId, false, false);
                    }
                    MaybeSaveToDatabase(doReindex, doSignal ? orderItem : null);
                }
                else
                {
                    throw new OrderItemNotFoundException("Failed to find order item when trying to set delivery library.");
                }
            }
            catch (Exception)
            {
                DisposeDatabaseContext(true);
                throw;
            }
            finally
            {
                DisposeDatabaseContext(doReindex);
            }
        }

        public void SetPurchaseLibrary(int orderNodeId, PurchaseLibraries purchaseLibrary, string eventId, bool doReindex = true, bool doSignal = true)
        {
            EnsureDatabaseContext();
            try
            {
                var orderItem = GetOrderItemFromEntityFramework(orderNodeId);
                if (orderItem != null)
                {
                    var currentPurchaseLibrary = orderItem.PurchaseLibrary;
                    if (currentPurchaseLibrary != purchaseLibrary)
                    {
                        orderItem.PurchaseLibrary = purchaseLibrary;
                        AddLogItem(orderNodeId, "BIBLIOTEK", "Inköpsbibliotek ändrat från " + currentPurchaseLibrary.ToString() + " till " + purchaseLibrary.ToString() + ".", eventId, false, false);
                    }
                    MaybeSaveToDatabase(doReindex, doSignal ? orderItem : null);
                }
                else
                {
                    throw new OrderItemNotFoundException("Failed to find order item when trying to set delivery library.");
                }
            }
            catch (Exception)
            {
                DisposeDatabaseContext(true);
                throw;
            }
            finally
            {
                DisposeDatabaseContext(doReindex);
            }
        }

        public void SetDrmWarning(int orderNodeId, bool status, string eventId, bool doReindex = true, bool doSignal = true)
        {
            EnsureDatabaseContext();
            try
            {
                var orderItem = GetOrderItemFromEntityFramework(orderNodeId);
                if (orderItem != null)
                {
                    if (orderItem.DrmWarning != (status ? "1" : "0"))
                    {
                        orderItem.DrmWarning = (status ? "1" : "0");
                        AddLogItem(orderNodeId, "DRM", "Kan innehålla drm-material!", eventId, false, false);
                    }
                    MaybeSaveToDatabase(doReindex, doSignal ? orderItem : null);
                }
                else
                {
                    throw new OrderItemNotFoundException("Failed to find order item when trying to set DRM warning.");
                }
            }
            catch (Exception)
            {
                DisposeDatabaseContext(true);
                throw;
            }
            finally
            {
                DisposeDatabaseContext(doReindex);
            }
        }

        public void SetDrmWarningWithoutLogging(int orderNodeId, bool status, bool doReindex = true, bool doSignal = true)
        {
            EnsureDatabaseContext();
            try
            {
                var orderItem = GetOrderItemFromEntityFramework(orderNodeId);
                if (orderItem != null)
                {
                    if (orderItem.DrmWarning != (status ? "1" : "0"))
                    {
                        orderItem.DrmWarning = (status ? "1" : "0");
                    }
                    MaybeSaveToDatabase(doReindex, doSignal ? orderItem : null);
                }
                else
                {
                    throw new OrderItemNotFoundException("Failed to find order item when trying to set DRM warning without logging.");
                }
            }
            catch (Exception)
            {
                DisposeDatabaseContext(true);
                throw;
            }
            finally
            {
                DisposeDatabaseContext(doReindex);
            }
        }

        public void SetDueDate(int nodeId, DateTime date, string eventId, bool doReindex = true, bool doSignal = true)
        {
            EnsureDatabaseContext();
            try
            {
                var orderItem = GetOrderItemFromEntityFramework(nodeId);
                if (orderItem != null)
                {
                    if (orderItem.DueDate != date)
                    {
                        orderItem.DueDate = date;
                        AddLogItem(nodeId, "DATE", "Återlämnas av låntagare senast " + date.ToString("yyyy-MM-dd HH:mm"), eventId, false, false);
                    }
                    MaybeSaveToDatabase(doReindex, doSignal ? orderItem : null);
                }
                else
                {
                    throw new OrderItemNotFoundException("Failed to find order item when trying to set due date.");
                }
            }
            catch (Exception)
            {
                DisposeDatabaseContext(true);
                throw;
            }
            finally
            {
                DisposeDatabaseContext(doReindex);
            }
        }

        public void SetFollowUpDate(int nodeId, DateTime date, string eventId, bool doReindex = true, bool doSignal = true)
        {
            EnsureDatabaseContext();
            try
            {
                var orderItem = GetOrderItemFromEntityFramework(nodeId);
                if (orderItem != null)
                {
                    if (orderItem.FollowUpDate != date)
                    {
                        orderItem.FollowUpDate = date;
                        FillOutStuff(orderItem);
                        AddLogItem(nodeId, "DATE", "Följs upp senast " + date.ToString("yyyy-MM-dd HH:mm"), eventId, false, false);
                    }
                    MaybeSaveToDatabase(doReindex, doSignal ? orderItem : null);
                }
                else
                {
                    throw new OrderItemNotFoundException("Failed to find order item when trying to set follow up date.");
                }
            }
            catch (Exception)
            {
                DisposeDatabaseContext(true);
                throw;
            }
            finally
            {
                DisposeDatabaseContext(doReindex);
            }
        }

        public void SetFollowUpDateWithoutLogging(int nodeId, DateTime date, bool doReindex = true, bool doSignal = true)
        {
            EnsureDatabaseContext();
            try
            {
                var orderItem = GetOrderItemFromEntityFramework(nodeId);
                if (orderItem != null)
                {
                    if (orderItem.FollowUpDate != date)
                    {
                        orderItem.FollowUpDate = date;
                    }
                    MaybeSaveToDatabase(doReindex, doSignal ? orderItem : null);
                }
                else
                {
                    throw new OrderItemNotFoundException("Failed to find order item when trying to set follow up date without logging.");
                }
            }
            catch (Exception)
            {
                DisposeDatabaseContext(true);
                throw;
            }
            finally
            {
                DisposeDatabaseContext(doReindex);
            }
        }

        public void SetPatronData(int nodeId, string sierraInfo, int sierraPatronRecordId, int pType, string homeLibrary, string aff, bool doReindex = true, bool doSignal = true)
        {
            EnsureDatabaseContext();
            try
            {
                var orderItem = GetOrderItemFromEntityFramework(nodeId);
                if (orderItem != null)
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
                    MaybeSaveToDatabase(doReindex, doSignal ? orderItem : null);
                }
                else
                {
                    throw new OrderItemNotFoundException("Failed to find order item when trying to set patron data.");
                }
            }
            catch (Exception)
            {
                DisposeDatabaseContext(true);
                throw;
            }
            finally
            {
                DisposeDatabaseContext(doReindex);
            }
        }

        public void SetPatronEmail(int nodeId, string email, string eventId, bool doReindex = true, bool doSignal = true)
        {
            EnsureDatabaseContext();
            try
            {
                var orderItem = GetOrderItemFromEntityFramework(nodeId);
                if (orderItem != null)
                {
                    if (orderItem.PatronEmail != email)
                    {
                        orderItem.PatronEmail = email;
                        AddLogItem(nodeId, "MAIL_NOTE", "E-post mot låntagare ändrad till " + email, eventId, false, false);
                    }
                    MaybeSaveToDatabase(doReindex, doSignal ? orderItem : null);
                }
                else
                {
                    throw new OrderItemNotFoundException("Failed to find order item when trying to set patron email.");
                }
            }
            catch (Exception)
            {
                DisposeDatabaseContext(true);
                throw;
            }
            finally
            {
                DisposeDatabaseContext(doReindex);
            }
        }

        public void SetProviderDueDate(int nodeId, DateTime date, string eventId, bool doReindex = true, bool doSignal = true)
        {
            EnsureDatabaseContext();
            try
            {
                var orderItem = GetOrderItemFromEntityFramework(nodeId);
                if (orderItem != null)
                {
                    if (orderItem.ProviderDueDate != date)
                    {
                        orderItem.ProviderDueDate = date;
                        AddLogItem(nodeId, "DATE", "Återlämnas till leverantör senast " + date.ToString("yyyy-MM-dd HH:mm"), eventId, false, false);
                    }
                    MaybeSaveToDatabase(doReindex, doSignal ? orderItem : null);
                }
                else
                {
                    throw new OrderItemNotFoundException("Failed to find order item when trying to set provider due date.");
                }
            }
            catch (Exception)
            {
                DisposeDatabaseContext(true);
                throw;
            }
            finally
            {
                DisposeDatabaseContext(doReindex);
            }
        }


        public void SetDeliveryDateWithoutLogging(int nodeId, DateTime date, string eventId, bool doReindex = true, bool doSignal = true)
        {
            EnsureDatabaseContext();
            try
            {
                var orderItem = GetOrderItemFromEntityFramework(nodeId);
                if (orderItem != null)
                {
                    if (orderItem.DeliveryDate != date)
                    {
                        orderItem.DeliveryDate = date;
                    }
                    MaybeSaveToDatabase(doReindex, doSignal ? orderItem : null);
                }
                else
                {
                    throw new OrderItemNotFoundException("Failed to find order item when trying to set delivery date.");
                }
            }
            catch (Exception)
            {
                DisposeDatabaseContext(true);
                throw;
            }
            finally
            {
                DisposeDatabaseContext(doReindex);
            }
        }

        public void SetProviderInformation(int nodeId, string providerInformation, string eventId, bool doReindex = true, bool doSignal = true)
        {
            EnsureDatabaseContext();
            try
            {
                var orderItem = GetOrderItemFromEntityFramework(nodeId);
                if (orderItem != null)
                {
                    if (orderItem.ProviderInformation != providerInformation)
                    {
                        orderItem.ProviderInformation = providerInformation;
                        AddLogItem(nodeId, "LEVERANTÖR", "Leverantörsinformation ändrad till \"" + providerInformation + "\".", eventId, false, false);
                    }
                    MaybeSaveToDatabase(doReindex, doSignal ? orderItem : null);
                }
                else
                {
                    throw new OrderItemNotFoundException("Failed to find order item when trying to set provider information.");
                }
            }
            catch (Exception)
            {
                DisposeDatabaseContext(true);
                throw;
            }
            finally
            {
                DisposeDatabaseContext(doReindex);
            }
        }

        public void SetProviderName(int nodeId, string providerName, string eventId, bool doReindex = true, bool doSignal = true)
        {
            EnsureDatabaseContext();
            try
            {
                var orderItem = GetOrderItemFromEntityFramework(nodeId);
                if (orderItem != null)
                {
                    if (orderItem.ProviderName != providerName)
                    {
                        orderItem.ProviderName = providerName;
                        AddLogItem(nodeId, "ORDER", "Beställd från " + providerName, eventId, false, false);
                    }
                    MaybeSaveToDatabase(doReindex, doSignal ? orderItem : null);
                }
                else
                {
                    throw new OrderItemNotFoundException("Failed to find order item when trying to set provider name.");
                }
            }
            catch (Exception)
            {
                DisposeDatabaseContext(true);
                throw;
            }
            finally
            {
                DisposeDatabaseContext(doReindex);
            }
        }

        public void SetProviderNameWithoutLogging(int nodeId, string providerName, bool doReindex = true, bool doSignal = true)
        {
            EnsureDatabaseContext();
            try
            {
                var orderItem = GetOrderItemFromEntityFramework(nodeId);
                if (orderItem != null)
                {
                    if (orderItem.ProviderName != providerName)
                    {
                        orderItem.ProviderName = providerName;
                    }
                    MaybeSaveToDatabase(doReindex, doSignal ? orderItem : null);
                }
                else
                {
                    throw new OrderItemNotFoundException("Failed to find order item when trying to set provider name without logging.");
                }
            }
            catch (Exception)
            {
                DisposeDatabaseContext(true);
                throw;
            }
            finally
            {
                DisposeDatabaseContext(doReindex);
            }
        }

        public void SetProviderOrderId(int nodeId, string providerOrderId, string eventId, bool doReindex = true, bool doSignal = true)
        {
            EnsureDatabaseContext();
            try
            {
                var orderItem = GetOrderItemFromEntityFramework(nodeId);
                if (orderItem != null)
                {
                    if (orderItem.ProviderOrderId != providerOrderId)
                    {
                        orderItem.ProviderOrderId = providerOrderId;
                        AddLogItem(nodeId, "ORDER", "Beställningsnr ändrat till " + providerOrderId, eventId, false, false);
                    }
                    MaybeSaveToDatabase(doReindex, doSignal ? orderItem : null);
                }
                else
                {
                    throw new OrderItemNotFoundException("Failed to find order item when trying to set provider order ID.");
                }
            }
            catch (Exception)
            {
                DisposeDatabaseContext(true);
                throw;
            }
            finally
            {
                DisposeDatabaseContext(doReindex);
            }
        }

        public void SetPurchasedMaterial(int orderNodeId, int purchasedMaterialId, string eventId, bool doReindex = true, bool doSignal = true)
        {
            EnsureDatabaseContext();
            try
            {
                var orderItem = GetOrderItemFromEntityFramework(orderNodeId);
                if (orderItem != null)
                {
                    if (orderItem.PurchasedMaterialId != purchasedMaterialId)
                    {
                        orderItem.PurchasedMaterialId = purchasedMaterialId;
                        FillOutStuff(orderItem);
                        AddLogItem(orderNodeId, "MATERIALINKÖP", "Inköpt material ändrat till " + _orderConfig.GetValueById(purchasedMaterialId), eventId, false, false);
                    }
                    MaybeSaveToDatabase(doReindex, doSignal ? orderItem : null);
                }
                else
                {
                    throw new OrderItemNotFoundException("Failed to find order item when trying to set purchased material.");
                }
            }
            catch (Exception)
            {
                DisposeDatabaseContext(true);
                throw;
            }
            finally
            {
                DisposeDatabaseContext(doReindex);
            }
        }

        public void SetReference(int nodeId, string reference, string eventId, bool doReindex = true, bool doSignal = true)
        {
            EnsureDatabaseContext();
            try
            {
                var orderItem = GetOrderItemFromEntityFramework(nodeId);
                if (orderItem != null)
                {
                    if (orderItem.Reference != reference)
                    {
                        orderItem.Reference = reference;
                        AddLogItem(nodeId, "REF", "Referens ändrad", eventId);
                    }
                    MaybeSaveToDatabase(doReindex, doSignal ? orderItem : null);
                }
                else
                {
                    throw new OrderItemNotFoundException("Failed to find order item when trying to set reference.");
                }
            }
            catch (Exception)
            {
                DisposeDatabaseContext(true);
                throw;
            }
            finally
            {
                DisposeDatabaseContext(doReindex);
            }
        }

        /**
         * Manual anonymization.
         */
        public void SilentAnonymization(int nodeId, string reference, IList<LogItem> logs, string eventId, bool doReindex = true, bool doSignal = true)
        {
            EnsureDatabaseContext();
            try
            {
                var orderItem = GetOrderItemFromEntityFramework(nodeId);
                if (orderItem != null)
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

                    FillOutStuff(orderItem);

                    MaybeSaveToDatabase(doReindex, doSignal ? orderItem : null);
                }
                else
                {
                    throw new OrderItemNotFoundException("Failed to find order item when trying to do silent anonymization");
                }
            }
            catch (Exception)
            {
                DisposeDatabaseContext(true);
                throw;
            }
            finally
            {
                DisposeDatabaseContext(doReindex);
            }
        }

        /**
         * Set all anonymization flags to false
         */
        public void ResetAllAnonymizationFlags(int nodeId, string eventId, bool doReindex = true, bool doSignal = true)
        {
            EnsureDatabaseContext();
            try
            {
                var orderItem = GetOrderItemFromEntityFramework(nodeId);
                if (orderItem != null)
                {
                    if (orderItem.IsAnonymized || orderItem.IsAnonymizedAutomatically)
                    {
                        orderItem.IsAnonymized = false;
                        orderItem.IsAnonymizedAutomatically = false;
                        AddLogItem(orderItem.NodeId, "ANONYMISERING", "Anonymiseringsstatus ändrad till ej anonymiserad.", eventId, false, false);
                        AddLogItem(orderItem.NodeId, "ANONYMISERING", "Automatisk anonymiseringsstatus ändrad till ej anonymiserad.", eventId, false, false);
                        MaybeSaveToDatabase(doReindex, doSignal ? orderItem : null);
                    }
                }
                else
                {
                    throw new OrderItemNotFoundException("Failed to find order item when trying to reset all anonymization flags.");
                }
            }
            catch (Exception)
            {
                DisposeDatabaseContext(true);
                throw;
            }
            finally
            {
                DisposeDatabaseContext(doReindex);
            }
        }

        /**
         * Set anonymized flag
         */
        public void SetIsAnonymized(int nodeId, bool isAnonymized, string eventId, bool doReindex = true, bool doSignal = true)
        {
            EnsureDatabaseContext();
            try
            {
                var orderItem = GetOrderItemFromEntityFramework(nodeId);
                if (orderItem != null)
                {
                    if (orderItem.IsAnonymized != isAnonymized)
                    {
                        orderItem.IsAnonymized = isAnonymized;
                        AddLogItem(orderItem.NodeId, "ANONYMISERING", "Anonymiseringsstatus ändrad till " + (orderItem.IsAnonymized ? "anonymiserad" : "ej anonymiserad") + ".", eventId, false, false);
                        MaybeSaveToDatabase(doReindex, doSignal ? orderItem : null);
                    }
                }
                else
                {
                    throw new OrderItemNotFoundException("Failed to find order item when trying to set anonymized flag to " + isAnonymized);
                }
            }
            catch (Exception)
            {
                DisposeDatabaseContext(true);
                throw;
            }
            finally
            {
                DisposeDatabaseContext(doReindex);
            }
        }

        public void SetStatus(int orderNodeId, string statusPrevalue, string eventId, bool doReindex = true, bool doSignal = true)
        {
            var statusId = _orderConfig.GetIdByValue("OrderStatus", statusPrevalue);
            SetStatus(orderNodeId, statusId, eventId, doReindex, doSignal);
        }

        public void SetStatus(int orderNodeId, int statusId, string eventId, bool doReindex = true, bool doSignal = true)
        {
            EnsureDatabaseContext();
            try
            {
                var orderItem = GetOrderItemFromEntityFramework(orderNodeId);
                if (orderItem != null)
                {
                    if (orderItem.StatusId != statusId)
                    {
                        var currentStatus = orderItem.StatusId;
                        orderItem.PreviousStatusId = orderItem.StatusId;
                        orderItem.StatusId = statusId;
                        OnStatusChanged(orderItem, statusId);
                        FillOutStuff(orderItem);
                        AddLogItem(orderNodeId, "STATUS", "Status ändrad från " + (currentStatus != -1 ? _orderConfig.GetValueById(currentStatus).Split(':').Last() : "Odefinierad") + " till " + _orderConfig.GetValueById(statusId).Split(':').Last(), eventId, false, false);
                    }
                    MaybeSaveToDatabase(doReindex, doSignal ? orderItem : null);
                }
                else
                {
                    throw new OrderItemNotFoundException("Failed to find order item when trying to set status.");
                }
            }
            catch (Exception)
            {
                DisposeDatabaseContext(true);
                throw;
            }
            finally
            {
                DisposeDatabaseContext(doReindex);
            }
        }

        public void SetType(int orderNodeId, int typeId, string eventId, bool doReindex = true, bool doSignal = true)
        {
            EnsureDatabaseContext();
            try
            {
                var orderItem = GetOrderItemFromEntityFramework(orderNodeId);
                if (orderItem != null)
                {
                    if (orderItem.TypeId != typeId)
                    {
                        orderItem.TypeId = typeId;
                        OnTypeChanged(orderItem, typeId);
                        FillOutStuff(orderItem);
                        AddLogItem(orderNodeId, "TYP", "Typ ändrad till " + _orderConfig.GetValueById(typeId), eventId, false, false);
                    }
                    MaybeSaveToDatabase(doReindex, doSignal ? orderItem : null);
                }
                else
                {
                    throw new OrderItemNotFoundException("Failed to find order item when trying to set type.");
                }
            }
            catch (Exception)
            {
                DisposeDatabaseContext(true);
                throw;
            }
            finally
            {
                DisposeDatabaseContext(doReindex);
            }
        }

        public void MakeDuplicate(int orderNodeId, string eventId, bool doReindex = true, bool doSignal = true)
        {
            EnsureDatabaseContext();
            try
            {
                var orderItem = _threadIdToDbContextMap[Thread.CurrentThread.ManagedThreadId].OrderItems
                    .AsNoTracking()
                    .Include(x => x.SierraInfo)
                    .Include(x => x.SierraInfo.adress)
                    .Include(x => x.LogItemsList)
                    .Include(x => x.AttachmentList)
                    .FirstOrDefault(x => x.NodeId == orderNodeId);

                var sourceOrderId = orderItem.OrderId;

                if (orderItem != null)
                {
                    // Temporary OrderId with MD5 Hash
                    orderItem.OrderId = "cthb-" + Helpers.CalculateMD5Hash(DateTime.Now.Ticks.ToString());

                    orderItem.Reference = orderItem.Reference + "\n\nKopia av " + sourceOrderId + ".";

                    FillOutStuff(orderItem);

                    _threadIdToDbContextMap[Thread.CurrentThread.ManagedThreadId].OrderItems.Add(orderItem);

                    // Save the OrderItem to get an Id
                    SaveToDatabase(null, false);

                    // Shorten the OrderId and include the NodeId
                    orderItem.OrderId = orderItem.OrderId.Substring(0, 13) + "-" + orderItem.NodeId.ToString();

                    AddLogItem(orderNodeId, "DUPLICERING", "Kopia " + orderItem.OrderId + " skapad med denna order som källa.", eventId, false, false);
                    AddLogItem(orderItem.NodeId, "DUPLICERING", "Skapade denna order som en kopia av " + sourceOrderId + ".", eventId, false, false);

                    MaybeSaveToDatabase(doReindex, doSignal ? orderItem : null);
                }
                else
                {
                    throw new OrderItemNotFoundException("Failed to find order item when trying to set type.");
                }
            }
            catch (Exception)
            {
                DisposeDatabaseContext(true);
                throw;
            }
            finally
            {
                DisposeDatabaseContext(doReindex);
            }
        }

        public void SetEditedByData(int orderNodeId, string memberId, string memberName, bool doReindex = true, bool doSignal = true)
        {
            EnsureDatabaseContext();
            try
            {
                var orderItem = GetOrderItemFromEntityFramework(orderNodeId);
                if (orderItem != null)
                {
                    orderItem.EditedBy = memberId;
                    orderItem.EditedByMemberName = memberName;
                    MaybeSaveToDatabase(doReindex, doSignal ? orderItem : null);
                }
                else
                {
                    throw new OrderItemNotFoundException("Failed to find order item when trying to set type.");
                }
            }
            catch (Exception)
            {
                DisposeDatabaseContext(true);
                throw;
            }
            finally
            {
                DisposeDatabaseContext(doReindex);
            }
        }

        public void SetReadOnlyAtLibrary(int nodeId, bool readOnlyAtLibrary, string eventId, bool doReindex = true, bool doSignal = true)
        {
            EnsureDatabaseContext();
            try
            {
                var orderItem = GetOrderItemFromEntityFramework(nodeId);
                if (orderItem != null)
                {
                    orderItem.ReadOnlyAtLibrary = readOnlyAtLibrary;
                    AddLogItem(nodeId, "LÄSESALSLÅN", "Läsesalslån satt till \"" + readOnlyAtLibrary + "\".", eventId, false, false);
                    MaybeSaveToDatabase(doReindex, doSignal ? orderItem : null);
                }
                else
                {
                    throw new OrderItemNotFoundException("Failed to find order item when trying to set read only at library.");
                }
            }
            catch (Exception)
            {
                DisposeDatabaseContext(true);
                throw;
            }
            finally
            {
                DisposeDatabaseContext(doReindex);
            }
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
                "Svar fr��n",
                "Leverans från",
                "PatronEmail ändrad till"
            };

            EnsureDatabaseContext();
            try
            {
                var orderItem = GetOrderItemFromEntityFramework(nodeId);
                if (orderItem != null)
                {
                    orderItem.OriginalOrder = "ANONYMIZED";
                    orderItem.PatronName = "ANONYMIZED";
                    orderItem.PatronCardNo = "ANONYMIZED";
                    orderItem.PatronEmail = "ANONYMIZED";

                    if (orderItem.LogItemsList != null)
                    {
                        // Modify some log items
                        foreach (var logItem in orderItem.LogItemsList)
                        {
                            // MAIL_NOTE, SIERRA and MAIL are the only one anonymized automatically.
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

                    FillOutStuff(orderItem);

                    MaybeSaveToDatabase(doReindex, doSignal ? orderItem : null);
                }
            }
            catch (Exception)
            {
                DisposeDatabaseContext(true);
                throw;
            }
            finally
            {
                DisposeDatabaseContext(doReindex);
            }
        }

        #region Private methods

        private OrderItemModel GetOrderItemFromEntityFramework(int nodeId)
        {
            var res = _threadIdToDbContextMap[Thread.CurrentThread.ManagedThreadId].OrderItems
                .Where(x => x.NodeId == nodeId)
                .Include(x => x.SierraInfo)
                .Include(x => x.SierraInfo.adress)
                .Include(x => x.LogItemsList)
                .Include(x => x.AttachmentList)
                .FirstOrDefault();

            FillOutStuff(res);

            return res;
        }

        private void OnStatusChanged(OrderItemModel orderItem, int newStatusId)
        {
            UpdateLastDeliveryStatusWhenProper(orderItem, newStatusId);
            UpdateDeliveryDateWhenProper(orderItem, newStatusId);
        }

        private void OnTypeChanged(OrderItemModel orderItem, int newTypeId)
        {
            // NOP
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

        private void SetDeliveryLibraryIfNewTypeIsArtikel(OrderItemModel orderItem, int newTypeId)
        {
            if (newTypeId == _orderConfig.GetIdByValue("OrderType", "Artikel"))
            {
                orderItem.DeliveryLibraryId = _orderConfig.GetIdByValue("DeliveryLibrary", "Huvudbiblioteket");
                orderItem.DeliveryLibrary = "Huvudbiblioteket";
            }
        }

        private bool IsDeliveryLibrarySameAsHomeLibrary(OrderItemModel orderItem)
        {
            return orderItem.SierraInfo.home_library == null ||
                (orderItem.DeliveryLibrary == "Huvudbiblioteket" && orderItem.SierraInfo.home_library.Contains("hbib")) ||
                (orderItem.DeliveryLibrary == "Lindholmenbiblioteket" && orderItem.SierraInfo.home_library.Contains("lbib")) ||
                (orderItem.DeliveryLibrary == "Arkitekturbiblioteket" && orderItem.SierraInfo.home_library.Contains("abib"));
        }

        private void EnsureDatabaseContext()
        {
            OrderItemsDbContext dbContext = null;

            if (!_threadIdToDbContextMap.TryGetValue(Thread.CurrentThread.ManagedThreadId, out dbContext))
            {
                dbContext = new OrderItemsDbContext(_orderItemSearcher);
                dbContext.Configuration.LazyLoadingEnabled = false;
                dbContext.Configuration.ProxyCreationEnabled = false;
                _threadIdToDbContextMap[Thread.CurrentThread.ManagedThreadId] = dbContext;
            }
        }

        private void DisposeDatabaseContext(bool dispose)
        {
            if (dispose)
            {
                OrderItemsDbContext dbContext = null;

                if (_threadIdToDbContextMap.TryGetValue(Thread.CurrentThread.ManagedThreadId, out dbContext))
                {
                    dbContext.Dispose();
                    dbContext = null;
                    _threadIdToDbContextMap.Remove(Thread.CurrentThread.ManagedThreadId);
                }
            }
        }

        private void MaybeSaveToDatabase(bool doSave, OrderItemModel item)
        {
            if (doSave)
            {
                SaveToDatabase(item, true);
            }
        }

        private void SaveToDatabase(OrderItemModel item, bool disposeDbContext)
        {
            OrderItemsDbContext dbContext = null;
            if (_threadIdToDbContextMap.TryGetValue(Thread.CurrentThread.ManagedThreadId, out dbContext))
            {
                dbContext.SaveChanges();
                if (item != null)
                {
                    _notifier.ReportNewOrderItemUpdate(item);
                }
            }
            else
            {
                throw new Exception("Database context was null when trying to save changes to order items.");
            }
        }

        private List<LogItem> GetLogItemsReverse(int nodeId)
        {
            OrderItemsDbContext dbContext = null;
            _threadIdToDbContextMap.TryGetValue(Thread.CurrentThread.ManagedThreadId, out dbContext);
            var contentNode = dbContext.OrderItems.Find(nodeId);

            string oldLogItems;
            var logItems = new List<LogItem>();

            if (!String.IsNullOrEmpty(contentNode.Log))
            {
                oldLogItems = contentNode.Log;
                logItems = JsonConvert.DeserializeObject<List<LogItem>>(oldLogItems);
            }
            return logItems;
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

        private void FillOutStuff(OrderItemModel orderItem)
        {
            orderItem.Log = JsonConvert.SerializeObject(orderItem.LogItemsList);
            orderItem.Attachments = JsonConvert.SerializeObject(orderItem.AttachmentList);

            orderItem.FollowUpDateIsDue = orderItem.FollowUpDate <= DateTime.Now ? true : false;

            // Status — look up string from config; if not found, preserve existing Status from DB
            if (orderItem.StatusId != -1)
            {
                var v = _orderConfig.GetValueById(orderItem.StatusId);
                if (!string.IsNullOrEmpty(v)) orderItem.Status = v;
            }
            orderItem.StatusString = (orderItem.Status ?? "").Split(':').Last();

            // Previous status
            if (orderItem.PreviousStatusId != -1)
            {
                var v = _orderConfig.GetValueById(orderItem.PreviousStatusId);
                if (!string.IsNullOrEmpty(v)) orderItem.PreviousStatus = v;
            }
            orderItem.PreviousStatusString = (orderItem.PreviousStatus ?? "").Split(':').Last();

            // Last delivery status
            if (orderItem.LastDeliveryStatusId != -1)
            {
                var v = _orderConfig.GetValueById(orderItem.LastDeliveryStatusId);
                if (!string.IsNullOrEmpty(v)) orderItem.LastDeliveryStatus = v;
            }
            orderItem.LastDeliveryStatusString = (orderItem.LastDeliveryStatus ?? "").Split(':').Last();

            // Type — look up from config; preserve if not found; default to "" when unset,
            // so an unclassified new order (TypeId == -1) never leaves Type as a C# null
            // (callers like the order list view call Type.ToString() unconditionally).
            if (orderItem.TypeId != -1)
            {
                var v = _orderConfig.GetValueById(orderItem.TypeId);
                if (!string.IsNullOrEmpty(v)) orderItem.Type = v;
            }
            else
            {
                orderItem.Type = "";
            }

            // Delivery Library — look up from config; preserve if not found; default to ""
            if (orderItem.DeliveryLibraryId != -1)
            {
                var v = _orderConfig.GetValueById(orderItem.DeliveryLibraryId);
                if (!string.IsNullOrEmpty(v)) orderItem.DeliveryLibrary = v;
            }
            else
            {
                orderItem.DeliveryLibrary = "";
            }

            // Cancellation reason
            if (orderItem.CancellationReasonId != -1)
            {
                var v = _orderConfig.GetValueById(orderItem.CancellationReasonId);
                if (!string.IsNullOrEmpty(v)) orderItem.CancellationReason = v;
            }
            else
            {
                orderItem.CancellationReason = "";
            }

            // Purchased material
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

        #endregion
    }
}