using System;
using System.Collections.Generic;
using System.IO;
using Chalmers.ILL.Members;
using Chalmers.ILL.Models;
using Chalmers.ILL.OrderItems;
using Chalmers.ILL.UmbracoApi;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Chalmers.ILL.Isolated
{
    // Fas 10/brytpunkten, "Ordna testdata för utvecklingsmiljön" (TODO-remove-dotnet-framework.md).
    // Without content under DataPath the order list is empty and the status/type dropdowns have
    // nothing in them - exactly the state that hid several of the Umbraco-removal's worst
    // regressions (null Type, empty status dropdown, wrong status-prefix handling) until someone
    // happened to open the app with real data loaded. Only ever called when Chillin:Isolated=true
    // (Program.cs) - this must never seed a live DataPath. Never overwrites: each piece is created
    // only when its target file/directory is missing, so a developer's own local data, or a
    // persistent isolated-server dataset, is never touched. "Reset" = delete DataPath and restart.
    public static class DevDataSeeder
    {
        // chillinPrevalues.json's OrderStatus/OrderType/DeliveryLibrary values below are not
        // invented: every one is a literal string the application logic itself branches on
        // (SetStatus/SetType/SetDeliveryLibrary call sites, OrderItemModel's LIBRARY_*_STRING
        // constants), so seeding with anything else would silently exercise a code path that
        // never occurs in production. CancellationReason/PurchasedMaterial have no such
        // constraint - nothing in the codebase branches on their text, they are pure display
        // labels - so those two lists are plausible placeholders, not extracted values.
        public static void SeedConfigFilesIfMissing(string dataPath)
        {
            // A brand new DataPath (a fresh devcontainer, or - as in IsolatedModeSmokeTest - a
            // scratch directory created per test run) doesn't exist as a directory yet; nothing
            // else has had a reason to create it this early in startup.
            Directory.CreateDirectory(dataPath);

            SeedPrevaluesIfMissing(dataPath);
            SeedMembersIfMissing(dataPath);
        }

        private static void SeedPrevaluesIfMissing(string dataPath)
        {
            var path = Path.Combine(dataPath, "chillinPrevalues.json");
            if (File.Exists(path))
                return;

            var lists = new Dictionary<string, List<DropdownOption>>
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
                // Placeholder labels - see the class-level comment. Real production values need to
                // come from the live prevalue table (same caveat as the 19 missing appSettings keys
                // documented in fas 6).
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

            WriteNewFileAtomically(path, JsonConvert.SerializeObject(lists, Formatting.Indented));
        }

        private static void SeedMembersIfMissing(string dataPath)
        {
            var path = Path.Combine(dataPath, "members.json");
            if (File.Exists(path))
                return;

            // Same PasswordHasher<T>/IdentityV2-compatibility setup as FileMembershipProvider -
            // duplicated rather than shared because that hasher is private to the provider and
            // this only ever needs to hash three throwaway dev passwords once.
            var hasher = new PasswordHasher<MemberAccount>(Options.Create(new PasswordHasherOptions
            {
                CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV2
            }));

            MemberAccount Account(string login, string password, params string[] roles)
            {
                var account = new MemberAccount { Login = login, Roles = new List<string>(roles) };
                account.PasswordHash = hasher.HashPassword(account, password);
                return account;
            }

            // One account per role so all three server-side authorization paths (the global
            // [Authorize], the "Desk" redirect in LoginSurfaceController, and
            // [Authorize(Roles="SuperAdmin")] on MemberAdminSurfaceController) can be exercised
            // without hand-editing members.json first. Dev-only passwords, not meant to be secret.
            var accounts = new List<MemberAccount>
            {
                Account("desk", "chillin-dev-desk", "Desk"),
                Account("admin", "chillin-dev-admin", "Desk", "Administrator"),
                Account("superadmin", "chillin-dev-superadmin", "Desk", "Administrator", "SuperAdmin"),
            };

            MemberFileStore.Save(accounts, path);
        }

        // Deliberately not the same atomic-replace helper as MemberFileStore.Save: this path only
        // ever creates a brand new file (guarded by the File.Exists check above, single instance,
        // startup-time only), so there is no concurrent writer to race - but the write is still
        // staged through a temp file so a crash mid-write can't leave a truncated
        // chillinPrevalues.json behind for the next start to trip over.
        private static void WriteNewFileAtomically(string path, string content)
        {
            var tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(tempPath, content);
            File.Move(tempPath, path);
        }

        // Must run after the app is built (needs the real IOrderItemManager/IChillinOrderConfiguration
        // DI singletons - order creation goes through the same SetStatus/SetType/etc. application
        // code every controller uses, not hand-written JSON, so seeded orders can never drift from
        // what a real save produces) but reads DataPath itself rather than taking IChillinConfiguration,
        // so it doesn't need a reference to Chalmers.ILL.Configuration for one field.
        public static void SeedOrdersIfMissing(string dataPath, IChillinOrderConfiguration orderConfig, IOrderItemManager orderItemManager)
        {
            var ordersDirectory = Path.Combine(dataPath, "orders");
            if (Directory.Exists(ordersDirectory))
                return;

            foreach (var order in SeedOrders)
            {
                var seedModel = new OrderItemSeedModel
                {
                    Id = "devseed-" + order.Status,
                    PatronName = order.PatronName,
                    PatronEmail = order.PatronName.ToLowerInvariant().Replace(" ", ".").Replace("-", "") + "@example.invalid",
                    PatronCardNumber = "1234567890",
                    DeliveryLibrarySigel = order.LibrarySigel,
                    Message = order.OriginalOrder,
                    MessagePrefix = "",
                    SierraPatronInfo = new SierraModel(),
                };

                // doReindex/doSignal=false on every intermediate call, then a final
                // SaveWithoutEventsAndWithSynchronousReindexing - the same "batch several mutations,
                // flush once" pattern every controller in the app uses (see FileOrderItemManager's
                // class-level comment), so one seeded order is one disk write, not six.
                var nodeId = orderItemManager.CreateOrderItemInDbFromOrderItemSeedModel(seedModel, doReindex: false, doSignal: false);
                var eventId = orderItemManager.GenerateEventId(0);

                orderItemManager.SetTitleInformation(nodeId, order.TitleInformation, eventId, false, false);
                orderItemManager.SetReference(nodeId, order.Reference, eventId, false, false);

                var typeId = orderConfig.GetIdByValue("OrderType", order.Type);
                orderItemManager.SetType(nodeId, typeId, eventId, false, false);

                if (order.Status != "01:Ny")
                {
                    orderItemManager.SetStatus(nodeId, order.Status, eventId, false, false);
                }

                if (order.CancellationReason != null)
                {
                    var reasonId = orderConfig.GetIdByValue("CancellationReason", order.CancellationReason);
                    orderItemManager.SetCancellationReason(nodeId, reasonId, eventId, false, false);
                }

                if (order.PurchasedMaterial != null)
                {
                    var materialId = orderConfig.GetIdByValue("PurchasedMaterial", order.PurchasedMaterial);
                    orderItemManager.SetPurchasedMaterial(nodeId, materialId, eventId, false, false);
                }

                orderItemManager.SetFollowUpDate(nodeId, order.FollowUpDate, eventId, false, false);

                orderItemManager.SaveWithoutEventsAndWithSynchronousReindexing(nodeId);
            }
        }

        private class SeedOrder
        {
            public string Status;
            public string Type;
            public string LibrarySigel;
            public string PatronName;
            public string TitleInformation;
            public string OriginalOrder;
            public string Reference;
            public DateTime FollowUpDate;
            public string CancellationReason;
            public string PurchasedMaterial;
        }

        // One order per status (all 17), cycling type and delivery library so every combination of
        // the three dropdowns gets exercised at least once. Patron/title text is deliberately full
        // of å/ä/ö and one long title, so the mojibake regression documented in
        // TODO-remove-umbraco.md ("åäö renderas rätt") has something to actually show it if it
        // ever comes back. FollowUpDate alternates past/future to exercise the pending/overdue
        // styling on the order list without needing a separate "overdue" fixture.
        private static readonly SeedOrder[] SeedOrders = new[]
        {
            new SeedOrder { Status = "01:Ny", Type = "Bok", LibrarySigel = "Z", PatronName = "Åsa Öhman", TitleInformation = "Kärlek och kaos i Köpenhamn", OriginalOrder = "Beställning av bok från låntagare Åsa Öhman.", Reference = "ref-ny-001", FollowUpDate = DateTime.Now.AddDays(7) },
            new SeedOrder { Status = "02:Åtgärda", Type = "Artikel", LibrarySigel = "ZL", PatronName = "Björn Ärlig", TitleInformation = "En artikel om ångbåtar på Vänern", OriginalOrder = "Artikelbeställning, ofullständig referens.", Reference = "ref-atgarda-002", FollowUpDate = DateTime.Now.AddDays(-2) },
            new SeedOrder { Status = "03:Beställd", Type = "Inköpsförslag", LibrarySigel = "ZA", PatronName = "Gösta Lindqvist", TitleInformation = "Arkitekturens historia i Göteborg", OriginalOrder = "Inköpsförslag från student.", Reference = "ref-bestalld-003", FollowUpDate = DateTime.Now.AddDays(14) },
            new SeedOrder { Status = "04:Väntar", Type = "Bok", LibrarySigel = "Z", PatronName = "Märta Sjögren", TitleInformation = "Väntans tid - en roman", OriginalOrder = "Väntar på leverans från annat bibliotek.", Reference = "ref-vantar-004", FollowUpDate = DateTime.Now.AddDays(30) },
            new SeedOrder { Status = "05:Levererad", Type = "Artikel", LibrarySigel = "ZL", PatronName = "Astrid Nordqvist", TitleInformation = "Nordiska språkkontakter, en översikt", OriginalOrder = "Levererad artikel, redo för avhämtning.", Reference = "ref-levererad-005", FollowUpDate = DateTime.Now.AddDays(-1) },
            new SeedOrder { Status = "06:Annullerad", Type = "Bok", LibrarySigel = "Z", PatronName = "Örjan Blomqvist", TitleInformation = "En bok som aldrig kom fram", OriginalOrder = "Beställning som annullerades pga. att titeln redan fanns.", Reference = "ref-annullerad-006", FollowUpDate = DateTime.Now.AddDays(-10), CancellationReason = "Titeln redan tillgänglig" },
            new SeedOrder { Status = "07:Överförd", Type = "Inköpsförslag", LibrarySigel = "ZA", PatronName = "Ingrid Åkerlund", TitleInformation = "Överförd till inköpsprocessen", OriginalOrder = "Överförd från fjärrlån till inköp.", Reference = "ref-overford-007", FollowUpDate = DateTime.Now.AddDays(20) },
            new SeedOrder { Status = "08:Inköpt", Type = "Bok", LibrarySigel = "Z", PatronName = "Sven-Åke Dahlström", TitleInformation = "En riktigt lång och krånglig titel med både kolon: bisats och frågetecken?", OriginalOrder = "Inköpt bok, katalogiseras.", Reference = "ref-inkopt-008", FollowUpDate = DateTime.Now.AddDays(5), PurchasedMaterial = "Ny bok" },
            new SeedOrder { Status = "09:Mottagen", Type = "Artikel", LibrarySigel = "ZL", PatronName = "Karin Holmqvist", TitleInformation = "Mottagen artikelkopia", OriginalOrder = "Mottagen via mail, väntar på leverans till låntagare.", Reference = "ref-mottagen-009", FollowUpDate = DateTime.Now.AddDays(-3) },
            new SeedOrder { Status = "10:Återsänd", Type = "Bok", LibrarySigel = "Z", PatronName = "Elin Söderström", TitleInformation = "Återsänd till utlånande bibliotek", OriginalOrder = "Boken har återsänts efter lånetidens slut.", Reference = "ref-atersand-010", FollowUpDate = DateTime.Now.AddDays(-15) },
            new SeedOrder { Status = "11:Utlånad", Type = "Bok", LibrarySigel = "ZA", PatronName = "Anders Wikström", TitleInformation = "Utlånad till låntagare på Arkitekturbiblioteket", OriginalOrder = "Boken är nu utlånad.", Reference = "ref-utlanad-011", FollowUpDate = DateTime.Now.AddDays(21) },
            new SeedOrder { Status = "12:Krävd", Type = "Bok", LibrarySigel = "Z", PatronName = "Cecilia Lönnqvist", TitleInformation = "Krävd bok, förfallen lånetid", OriginalOrder = "Boken är krävd, lånetiden har gått ut.", Reference = "ref-kravd-012", FollowUpDate = DateTime.Now.AddDays(-5) },
            new SeedOrder { Status = "13:Transport", Type = "Bok", LibrarySigel = "ZL", PatronName = "Fredrik Åström", TitleInformation = "Under transport till Kuggen", OriginalOrder = "Skickad internt mellan biblioteken.", Reference = "ref-transport-013", FollowUpDate = DateTime.Now.AddDays(2) },
            new SeedOrder { Status = "14:Infodisk", Type = "Bok", LibrarySigel = "Z", PatronName = "Hanna Öqvist", TitleInformation = "Ligger på infodisken för avhämtning", OriginalOrder = "Klar för avhämtning i disken.", Reference = "ref-infodisk-014", FollowUpDate = DateTime.Now.AddDays(3) },
            new SeedOrder { Status = "15:Förlorad?", Type = "Artikel", LibrarySigel = "ZL", PatronName = "Per-Olof Ängman", TitleInformation = "Eventuellt förlorad artikelkopia", OriginalOrder = "Går inte att hitta, misstänkt förlorad.", Reference = "ref-forlorad-fraga-015", FollowUpDate = DateTime.Now.AddDays(-7) },
            new SeedOrder { Status = "16:Förlorad", Type = "Bok", LibrarySigel = "Z", PatronName = "Ulrika Bergström", TitleInformation = "Bekräftat förlorad bok", OriginalOrder = "Boken är bekräftat förlorad, ersättning krävs.", Reference = "ref-forlorad-016", FollowUpDate = DateTime.Now.AddDays(-20) },
            new SeedOrder { Status = "17:FOLIO", Type = "Bok", LibrarySigel = "ZA", PatronName = "Nils Åkesson", TitleInformation = "Hanterad via FOLIO", OriginalOrder = "Mottagen och registrerad i FOLIO.", Reference = "ref-folio-017", FollowUpDate = DateTime.Now.AddDays(10) },
        };
    }
}
