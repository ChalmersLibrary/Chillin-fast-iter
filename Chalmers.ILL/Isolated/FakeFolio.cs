using System;
using Chalmers.ILL.Models;
using Chalmers.ILL.Repositories;
using Chalmers.ILL.Services;

namespace Chalmers.ILL.Isolated
{
    // Was Services/FakeFolio.cs - moved here as part of isolerat läge steg A (fas 6). Already
    // implemented all seven FOLIO interfaces; finished off rather than rewritten: Console.WriteLine
    // replaced with log4net (Console disappears in App Service), and the four Post overloads that
    // used to return null now build a fake object from the input so callers that read the result
    // (e.g. the new item's Id/Barcode) don't immediately null-reference.
    public class FakeFolio : IFolioItemService, IFolioRepository, IFolioService, IFolioInstanceService,
        IFolioHoldingService, IFolioCirculationService, IFolioUserService
    {
        private static readonly log4net.ILog _log = log4net.LogManager.GetLogger(typeof(FakeFolio));

        public ItemQuery ByQuery(string query)
        {
            return new ItemQuery
            {
                Items = new Item[0],
                TotalRecords = 0
            };
        }

        public FolioUser ByUserName(string userName)
        {
            return new FolioUser
            {
                Id = "7312031232",
                Personal = new Personal
                {
                    FirstName = "John",
                    LastName = "Doe"
                }
            };
        }

        public void InitFolio(string title, string orderId, string barcode, string pickUpServicePoint, bool readOnlyAtLibrary, string patronCardNumber)
        {
            _log.Info("[isolated] InitFolio: " + title + " (order " + orderId + ", barcode " + barcode + ")");
        }

        public Item Post(ItemBasic item, bool readOnlyAtLibrary)
        {
            _log.Info("[isolated] POST FOLIO item, barcode " + item.Barcode);

            return new Item
            {
                Id = Guid.NewGuid().ToString(),
                Barcode = item.Barcode,
                HoldingsRecordId = item.HoldingsRecordId,
                Status = item.Status,
                MaterialTypeId = item.MaterialTypeId,
                PermanentLoanTypeId = item.PermanentLoanTypeId,
                StatisticalCodeIds = item.StatisticalCodeIds
            };
        }

        public string Post(string path, string body)
        {
            _log.Info("[isolated] POST FOLIO " + path);

            return "{}";
        }

        public Instance Post(InstanceBasic item)
        {
            _log.Info("[isolated] POST FOLIO instance, title " + item.Title);

            return new Instance
            {
                Id = Guid.NewGuid().ToString(),
                Source = item.Source,
                Title = item.Title,
                Identifiers = item.Identifiers,
                InstanceTypeId = item.InstanceTypeId,
                StatusId = item.StatusId,
                DiscoverySuppress = item.DiscoverySuppress,
                StatisticalCodeIds = item.StatisticalCodeIds
            };
        }

        public Holding Post(HoldingBasic item)
        {
            _log.Info("[isolated] POST FOLIO holding, instance " + item.InstanceId);

            return new Holding
            {
                Id = Guid.NewGuid().ToString(),
                InstanceId = item.InstanceId,
                PermanentLocationId = item.PermanentLocationId,
                StatisticalCodeIds = item.StatisticalCodeIds
            };
        }

        public Circulation Post(CirculationBasic item)
        {
            _log.Info("[isolated] POST FOLIO circulation request, item " + item.ItemId);

            return new Circulation
            {
                Id = Guid.NewGuid().ToString(),
                RequestType = item.RequestType,
                RequestDate = item.RequestDate,
                RequesterId = item.RequesterId,
                ItemId = item.ItemId,
                Status = "Open - Not yet filled",
                FulfillmentPreference = item.FulfillmentPreference,
                PickupServicePointId = item.PickupServicePointId
            };
        }

        public void Put(Item item)
        {
            _log.Info("[isolated] PUT FOLIO item " + item.Id);
        }

        public string Put(string path, string body)
        {
            _log.Info("[isolated] PUT FOLIO " + path);

            return "{}";
        }

        public void SetItemToWithdrawn(string id)
        {
            _log.Info("[isolated] FOLIO set item to withdrawn " + id);
        }

        string IFolioRepository.ByQuery(string path)
        {
            return "{}";
        }
    }
}
