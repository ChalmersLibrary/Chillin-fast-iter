using System.Collections.Generic;
using Chalmers.ILL.Models;
using Chalmers.ILL.UmbracoApi;

namespace Chalmers.ILL.Isolated
{
    // IChillinOrderConfiguration for isolated mode (fas 6, isolerat läge steg A / fas 10). No
    // file, no seeding, no DataPath ordering to get right - just the same values a developer
    // would otherwise have had to seed into chillinPrevalues.json on every fresh DataPath.
    //
    // OrderStatus/OrderType/DeliveryLibrary are not invented: every one is a literal string the
    // application logic itself branches on (SetStatus/SetType/SetDeliveryLibrary call sites,
    // OrderItemModel's LIBRARY_*_STRING constants - grepped, not guessed), so this can never
    // silently exercise a code path that doesn't occur in production. CancellationReason/
    // PurchasedMaterial have no such constraint - nothing in the codebase branches on their text,
    // they're pure display labels - so those two lists are plausible placeholders, not extracted
    // values; real ones need the live prevalue table (same caveat as the 19 missing appSettings
    // keys documented in fas 6).
    //
    // Deliberately not the real, historical Umbraco prevalue IDs: those only matter for making
    // sense of *existing* production orders (StatusId/TypeId stored on disk), and an isolated
    // instance - local devcontainer or the isolated test server - never has any of those, so any
    // consistent set of IDs works equally well.
    public class HardcodedChillinOrderConfiguration : ChillinOrderConfiguration
    {
        public HardcodedChillinOrderConfiguration() : base(Lists)
        {
        }

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
    }
}
