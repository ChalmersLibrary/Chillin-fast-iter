using Chalmers.ILL.Configuration;
using Chalmers.ILL.Exceptions;
using Chalmers.ILL.Models;
using System;
using System.Collections.Generic;

namespace Chalmers.ILL.Services
{
    public class FolioService : IFolioService
    {
        private readonly IFolioItemService _folioItemService;
        private readonly IFolioInstanceService _folioInstanceService;
        private readonly IFolioHoldingService _folioHoldingService;
        private readonly IFolioCirculationService _folioCirculationService;
        private readonly IChillinConfiguration _config;

        public FolioService
        (
            IFolioItemService folioItemService,
            IFolioInstanceService folioInstanceService,
            IFolioHoldingService folioHoldingService,
            IFolioCirculationService folioCirculationService,
            IChillinConfiguration config
        )
        {
            _folioItemService = folioItemService;
            _folioInstanceService = folioInstanceService;
            _folioHoldingService = folioHoldingService;
            _folioCirculationService = folioCirculationService;
            _config = config;
        }

        public void SetItemToWithdrawn(string barcode)
        {
            var response = _folioItemService.ByQuery($"barcode={barcode}");
            if (response.TotalRecords > 0 && response.Items[0].Barcode == barcode)
            {
                response.Items[0].Status.Name = "Withdrawn";
                _folioItemService.Put(response.Items[0]);
            } 
            else
            {
                throw new ItemNotFoundException("Hittade inte boken i FOLIO");
            }
        }

        public void InitFolio(string title, string orderId, string barcode, string pickUpServicePoint, bool readOnlyAtLibrary, string folioUserId)
        {
            VerifyBarCode(barcode);
            var resInstance = CreateInstance(title, orderId);
            var resHolding = CreateHolding(resInstance.Id);
            var resItem = CreateItem(resHolding.Id, barcode, readOnlyAtLibrary);
            var resCiruclation = CreateCirculation(resItem.Id, folioUserId, pickUpServicePoint, resInstance.Id, resHolding.Id);
        }

        private void VerifyBarCode(string barcode)
        {
            if (string.IsNullOrEmpty(barcode))
            {
                throw new ArgumentNullException(nameof(barcode));
            }

            var response = _folioItemService.ByQuery($"barcode={barcode}");

            if (response.TotalRecords > 0)
            {
                throw new BarCodeException("Streckkoden finns redan i FOLIO");
            }
        }

        private Instance CreateInstance(string title, string orderId) =>
            _folioInstanceService.Post(new InstanceBasic(title, orderId, _config.InstanceResourceTypeId, _config.InstanceStatusId, _config.InstanceModesOfIssuance, _config.InstanceIdentifierTypeId, _config.ChillinStatisticalCodeId));

        private Holding CreateHolding(string instanceId) =>
            _folioHoldingService.Post(new HoldingBasic(instanceId, _config.FolioSourceId, _config.HoldingPermanentLocationId, _config.ChillinStatisticalCodeId));

        private Item CreateItem(string holdingId, string barCode, bool readOnlyAtLibrary) =>
            _folioItemService.Post(new ItemBasic(barCode, holdingId, readOnlyAtLibrary, _config.ItemMaterialTypeId, _config.ChillinStatisticalCodeId, _config.ItemPermanentLoanTypeId, _config.ItemPermanentLoanTypeIdInHouse), readOnlyAtLibrary);

        private Circulation CreateCirculation(string itemId, string requesterId, string pickupServicePoint, string instanceId, string holdingId) =>
            _folioCirculationService.Post(new CirculationBasic(itemId, requesterId, ServicePoints()[pickupServicePoint], instanceId, holdingId));

        private Dictionary<string, string> ServicePoints() =>
            new Dictionary<string, string>()
            {
                { "Huvudbiblioteket", _config.ServicePointHuvudbiblioteketId },
                { "Lindholmenbiblioteket", _config.ServicePointLindholmenbiblioteketId },
                { "Arkitekturbiblioteket", _config.ServicePointArkitekturbiblioteketId }
            };
    }
}