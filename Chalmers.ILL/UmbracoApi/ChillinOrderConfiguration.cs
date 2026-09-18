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
    // tables used, or every existing production order's Status/Type/etc. will resolve to "" the
    // next time it's saved. [PLACEHOLDER: fill in the real historical Ids and the real
    // CancellationReason/PurchasedMaterial values from prod - see TODO-remove-dotnet-framework.md,
    // fas 10, "Ordna testdata för utvecklingsmiljön".]
    public class ChillinOrderConfiguration : IChillinOrderConfiguration
    {
        private static readonly Dictionary<string, List<DropdownOption>> Lists = new Dictionary<string, List<DropdownOption>>
        {
            ["OrderStatus"] = new List<DropdownOption>
            {
                new DropdownOption { Id = 1, Order = 1, Value = "01:Ny" },
                new DropdownOption { Id = 2, Order = 2, Value = "02:Åtgärda" },
                new DropdownOption { Id = 3, Order = 3, Value = "03:Beställd" },
                new DropdownOption { Id = 4, Order = 4, Value = "04:Väntar" },
                new DropdownOption { Id = 5, Order = 5, Value = "05:Levererad" },
                new DropdownOption { Id = 6, Order = 6, Value = "06:Annullerad" },
                new DropdownOption { Id = 7, Order = 7, Value = "07:Överförd" },
                new DropdownOption { Id = 8, Order = 8, Value = "08:Inköpt" },
                new DropdownOption { Id = 9, Order = 9, Value = "09:Mottagen" },
                new DropdownOption { Id = 10, Order = 10, Value = "10:Återsänd" },
                new DropdownOption { Id = 11, Order = 11, Value = "11:Utlånad" },
                new DropdownOption { Id = 12, Order = 12, Value = "12:Krävd" },
                new DropdownOption { Id = 13, Order = 13, Value = "13:Transport" },
                new DropdownOption { Id = 14, Order = 14, Value = "14:Infodisk" },
                new DropdownOption { Id = 15, Order = 15, Value = "15:Förlorad?" },
                new DropdownOption { Id = 16, Order = 16, Value = "16:Förlorad" },
                new DropdownOption { Id = 17, Order = 17, Value = "17:FOLIO" },
            },
            ["OrderType"] = new List<DropdownOption>
            {
                new DropdownOption { Id = 101, Order = 1, Value = "Bok" },
                new DropdownOption { Id = 102, Order = 2, Value = "Artikel" },
                new DropdownOption { Id = 103, Order = 3, Value = "Inköpsförslag" },
            },
            ["DeliveryLibrary"] = new List<DropdownOption>
            {
                new DropdownOption { Id = 201, Order = 1, Value = OrderItemModel.LIBRARY_Z_UMBRACO_STRING },
                new DropdownOption { Id = 202, Order = 2, Value = OrderItemModel.LIBRARY_ZL_UMBRACO_STRING },
                new DropdownOption { Id = 203, Order = 3, Value = OrderItemModel.LIBRARY_ZA_UMBRACO_STRING },
            },
            // Placeholder labels - nothing in the codebase branches on their text (unlike the
            // three lists above), so these are plausible but not the real production list.
            ["CancellationReason"] = new List<DropdownOption>
            {
                new DropdownOption { Id = 301, Order = 1, Value = "Titeln redan tillgänglig" },
                new DropdownOption { Id = 302, Order = 2, Value = "Återkallad av låntagaren" },
                new DropdownOption { Id = 303, Order = 3, Value = "Går ej att anskaffa" },
                new DropdownOption { Id = 304, Order = 4, Value = "Dubblettbeställning" },
            },
            ["PurchasedMaterial"] = new List<DropdownOption>
            {
                new DropdownOption { Id = 401, Order = 1, Value = "Ny bok" },
                new DropdownOption { Id = 402, Order = 2, Value = "Artikel/kopia" },
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
