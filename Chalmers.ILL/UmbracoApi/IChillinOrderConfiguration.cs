using Chalmers.ILL.Models;
using System.Collections.Generic;

namespace Chalmers.ILL.UmbracoApi
{
    public interface IChillinOrderConfiguration
    {
        List<DropdownOption> GetAvailableTypes();
        List<DropdownOption> GetAvailableStatuses();
        List<DropdownOption> GetAvailableDeliveryLibraries();
        List<DropdownOption> GetAvailableCancellationReasons();
        List<DropdownOption> GetAvailablePurchasedMaterials();

        // Maps integer prevalue ID (persisted on each order, e.g. OrderItemModel.StatusId) to the
        // string value, e.g. 1042 -> "01:Ny"
        string GetValueById(int id);

        // Maps a list + string value to the integer prevalue ID, e.g. "OrderStatus"/"01:Ny" -> 1042
        int GetIdByValue(string listKey, string value);

        void PopulateModelWithAvailableValues(OrderItemPageModelBase model);
    }
}
