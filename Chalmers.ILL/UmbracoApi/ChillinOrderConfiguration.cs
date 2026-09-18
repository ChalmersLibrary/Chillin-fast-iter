using Chalmers.ILL.Models;
using System.Collections.Generic;
using System.Linq;

namespace Chalmers.ILL.UmbracoApi
{
    // Was backed by chillinPrevalues.json, a file meant to hold a real, manually-uploaded export
    // of Umbraco's old prevalue tables (see "Fastställda designbeslut" i
    // TODO-remove-dotnet-framework.md) - one per environment (Live and, briefly, isolated mode
    // too, via Isolated.HardcodedChillinOrderConfiguration). Per avstämning 2026-09-18: these
    // values aren't really per-environment configuration at all - the string labels are already
    // hardcoded into the application logic itself (SetStatus/SetType/SetDeliveryLibrary call
    // sites, OrderItemModel's LIBRARY_*_STRING constants - the same values below are grepped from
    // those call sites, not invented), so a JSON file (whether manually uploaded or checked into
    // git) was just an indirection around data that's really part of the codebase. Hardcoded here
    // instead, one implementation for both Live and Isolated - no more file, no DataPath, no
    // Live/Isolated split for this seam.
    //
    // The Id numbers matter for one reason: OrderItemModel.StatusId/TypeId/etc. (int) are what's
    // actually persisted on each order, and FillOutStuff re-resolves the string from that Id via
    // GetValueById on every save - so these must be exactly the same Ids Umbraco's old prevalue
    // tables used, or every existing production order's Status/Type/etc. would resolve to "" the
    // next time it's saved. Filled in from the real prod export 2026-09-18 (Lars) - one shared Id
    // sequence (5-31) across all five lists, matching how Umbraco's own prevalue tables assigned
    // them.
    public class ChillinOrderConfiguration : IChillinOrderConfiguration
    {
        private static readonly Dictionary<string, List<DropdownOption>> Lists = new Dictionary<string, List<DropdownOption>>
        {
            ["OrderStatus"] = new List<DropdownOption>
            {
                new DropdownOption { Id = 5, Order = 0, Value = "01:Ny" },
                new DropdownOption { Id = 6, Order = 1, Value = "02:Åtgärda" },
                new DropdownOption { Id = 7, Order = 2, Value = "03:Beställd" },
                new DropdownOption { Id = 8, Order = 3, Value = "04:Väntar" },
                new DropdownOption { Id = 9, Order = 4, Value = "05:Levererad" },
                new DropdownOption { Id = 10, Order = 5, Value = "06:Annullerad" },
                new DropdownOption { Id = 13, Order = 6, Value = "07:Överförd" },
                new DropdownOption { Id = 14, Order = 7, Value = "08:Inköpt" },
                new DropdownOption { Id = 16, Order = 8, Value = "09:Mottagen" },
                new DropdownOption { Id = 24, Order = 9, Value = "10:Återsänd" },
                new DropdownOption { Id = 25, Order = 10, Value = "11:Utlånad" },
                new DropdownOption { Id = 26, Order = 11, Value = "12:Krävd" },
                new DropdownOption { Id = 27, Order = 12, Value = "13:Transport" },
                new DropdownOption { Id = 28, Order = 13, Value = "14:Infodisk" },
                new DropdownOption { Id = 29, Order = 14, Value = "15:Förlorad?" },
                new DropdownOption { Id = 30, Order = 15, Value = "16:Förlorad" },
                new DropdownOption { Id = 31, Order = 16, Value = "17:FOLIO" },
            },
            ["OrderType"] = new List<DropdownOption>
            {
                new DropdownOption { Id = 11, Order = 0, Value = "Artikel" },
                new DropdownOption { Id = 12, Order = 1, Value = "Bok" },
                new DropdownOption { Id = 15, Order = 2, Value = "Inköpsförslag" }
            },
            ["DeliveryLibrary"] = new List<DropdownOption>
            {
                new DropdownOption { Id = 17, Order = 0, Value = OrderItemModel.LIBRARY_Z_UMBRACO_STRING },
                new DropdownOption { Id = 18, Order = 1, Value = OrderItemModel.LIBRARY_ZL_UMBRACO_STRING },
                new DropdownOption { Id = 19, Order = 2, Value = OrderItemModel.LIBRARY_ZA_UMBRACO_STRING }
            },
            ["CancellationReason"] = new List<DropdownOption>
            {
                new DropdownOption { Id = 20, Order = 0, Value = "Finns Z" },
                new DropdownOption { Id = 21, Order = 1, Value = "Annan" }
            },
            ["PurchasedMaterial"] = new List<DropdownOption>
            {
                new DropdownOption { Id = 22, Order = 0, Value = "Bok" },
                new DropdownOption { Id = 23, Order = 1, Value = "E-bok" }
            },
        };

        private static readonly Dictionary<int, string> IdToValue = Lists.Values
            .SelectMany(l => l)
            .GroupBy(o => o.Id)
            .ToDictionary(g => g.Key, g => g.First().Value);

        public List<DropdownOption> GetAvailableTypes() => Get("OrderType");
        public List<DropdownOption> GetAvailableStatuses() => Get("OrderStatus");
        public List<DropdownOption> GetAvailableDeliveryLibraries() => Get("DeliveryLibrary");
        public List<DropdownOption> GetAvailableCancellationReasons() => Get("CancellationReason");
        public List<DropdownOption> GetAvailablePurchasedMaterials() => Get("PurchasedMaterial");

        public string GetValueById(int id)
        {
            if (id == -1) return "";
            IdToValue.TryGetValue(id, out var value);
            return value ?? "";
        }

        public int GetIdByValue(string listKey, string value)
        {
            var list = Get(listKey);
            var match = list.FirstOrDefault(o => o.Value == value);
            return match?.Id ?? -1;
        }

        public void PopulateModelWithAvailableValues(OrderItemPageModelBase model)
        {
            model.AvailableTypes = GetAvailableTypes();
            model.AvailableStatuses = GetAvailableStatuses();
            model.AvailableDeliveryLibraries = GetAvailableDeliveryLibraries();
            model.AvailableCancellationReasons = GetAvailableCancellationReasons();
            model.AvailablePurchasedMaterials = GetAvailablePurchasedMaterials();
        }

        private static List<DropdownOption> Get(string key)
        {
            Lists.TryGetValue(key, out var list);
            return list ?? new List<DropdownOption>();
        }
    }
}
