using System;
using System.IO;
using Chalmers.ILL.Models;
using Chalmers.ILL.OrderItems;
using Chalmers.ILL.UmbracoApi;
using Newtonsoft.Json;

namespace Chalmers.ILL.Isolated
{
    // Fas 10/brytpunkten, "Ordna testdata för utvecklingsmiljön" (TODO-remove-dotnet-framework.md).
    // Without content under DataPath the order list is empty and several core actions can't even
    // render - exactly the state that hid several of the Umbraco-removal's worst regressions
    // (null Type, empty status dropdown, wrong status-prefix handling) until someone happened to
    // open the app with real data loaded. Only ever called when Chillin:Isolated=true (Program.cs)
    // - this must never touch a live DataPath. Never overwrites: each piece is created only when
    // its target file/directory is missing, so a developer's own local data, or a persistent
    // isolated-server dataset, is never touched. "Reset" = delete DataPath and restart.
    //
    // chillinPrevalues.json and members.json are deliberately NOT seeded here, unlike an earlier
    // version of this class:
    // - chillinPrevalues.json doesn't exist at all any more (avstämt 2026-09-18) -
    //   IChillinOrderConfiguration (UmbracoApi/ChillinOrderConfiguration.cs) is hardcoded directly
    //   in code instead, the same values for Live and isolated, so there's nothing left to seed.
    // - members.json needs a real account to actually log in with, and there's no chicken-and-egg
    //   way to create the first one through the UI - creating it is a one-time manual step (see
    //   README.md), not something to regenerate on every fresh DataPath.
    public static class DevDataSeeder
    {
        // Found by exercising every order-action endpoint against a seeded order (fas 10,
        // "Använd brytpunkten löpande"): ClaimBookMailTemplate, SignatureTemplate,
        // ReturnDateChangedMailTemplate and BookAvailableMailTemplate all threw
        // TemplateServiceException ("Hittade ingen mall med nodnamn=...") - ITemplateService.
        // GetTemplateData(string nodeName) treats a missing named template as a hard error, not an
        // empty-state fallback (unlike GetTemplateData(int, OrderItemModel), which has one). These
        // 13 node names (every literal passed to GetTemplateData(string, ...) anywhere in the
        // codebase - grepped, not guessed) are exactly as load-bearing as chillinPrevalues.json's
        // OrderStatus/OrderType values: the app can't render several core actions without them, in
        // isolated mode or in a real fresh deployment. Content is a placeholder - unlike the
        // status/type prevalues, nothing branches on template *text*, only on NodeName existing at
        // all, so a placeholder body is functionally complete, just not the real production wording.
        private static readonly string[] RequiredSystemTemplateNodeNames = new[]
        {
            "CourtesyNoticeMailTemplate",
            "LoanPeriodOverMailTemplate",
            "LoanPeriodReallyOverMailTemplate",
            "LoanPeriodReallyReallyOverMailTemplate",
            "ArticleAvailableInInfodiskMailTemplate",
            "ArticleDeliveryByMailTemplate",
            "ArticleDeliveryByPostTemplate",
            "ArticleDeliveryByInternpostTemplate",
            "BookAvailableMailTemplate",
            "BookAvailableForReadingAtLibraryMailTemplate",
            "ClaimBookMailTemplate",
            "SignatureTemplate",
            "ReturnDateChangedMailTemplate",
        };

        public static void SeedSystemTemplatesIfMissing(string dataPath)
        {
            // Written directly as Isolated.FileTemplateService's own one-file-per-template format
            // (DataPath/templates/{Id}.json) rather than through ITemplateService.CreateTemplate -
            // that method assigns its own auto-generated NodeName and offers no way to set the
            // exact NodeName these lookups require.
            var templatesDirectory = Path.Combine(dataPath, "templates");
            if (Directory.Exists(templatesDirectory))
                return;

            Directory.CreateDirectory(templatesDirectory);

            var id = 1;
            foreach (var nodeName in RequiredSystemTemplateNodeNames)
            {
                var template = new Template
                {
                    Id = id,
                    NodeName = nodeName,
                    CreateDate = DateTime.Now,
                    UpdateDate = DateTime.Now,
                    NodeTypeAlias = "ChalmersILLTemplate",
                    Description = nodeName,
                    Data = "[Platshållartext för " + nodeName + " - ersätt med riktig mallText.]",
                    Automatic = true,
                    Acquisition = false,
                };
                WriteNewFileAtomically(Path.Combine(templatesDirectory, id + ".json"), JsonConvert.SerializeObject(template, Formatting.Indented));
                id++;
            }
        }

        // Deliberately not the same atomic-replace helper as MemberFileStore.Save: this path only
        // ever creates a brand new file (guarded by the Directory.Exists check above, single
        // instance, startup-time only), so there is no concurrent writer to race - but the write
        // is still staged through a temp file so a crash mid-write can't leave a truncated
        // template file behind for the next start to trip over.
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

                // Event type 20 ("Order skapad från maildata" - see
                // Models/PartialPage/ChalmersILLOrderItemModel.EventIdToEventNameMapping), the
                // closest existing fit for an order created from structured data rather than a
                // user action. Any of the mapping's ~30 numeric keys must be used here - the
                // order detail view (Chalmers.ILL.OrderItem.cshtml) looks up
                // EventIdToEventNameMapping[eventType] to label the log group and throws
                // KeyNotFoundException for anything else (caught while verifying this seeder:
                // GenerateEventId(0) produced "-00", which isn't a key in that dictionary).
                var eventId = orderItemManager.GenerateEventId(20);

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
        //
        // 02:Åtgärda is deliberately Artikel+Z (Huvudbiblioteket), not the ZL used by the other
        // Artikel orders below: Chalmers.ILL.Action.Delivery.cshtml only offers the
        // ArticleInInfodisk delivery-type view for Artikel+Huvudbiblioteket (every other
        // library/type combination falls through to ArticleInTransit), and ZL is still exercised
        // by 05/09/13/15 below, so this doesn't lose any other coverage.
        private static readonly SeedOrder[] SeedOrders = new[]
        {
            new SeedOrder { Status = "01:Ny", Type = "Bok", LibrarySigel = "Z", PatronName = "Åsa Öhman", TitleInformation = "Kärlek och kaos i Köpenhamn", OriginalOrder = "Beställning av bok från låntagare Åsa Öhman.", Reference = "ref-ny-001", FollowUpDate = DateTime.Now.AddDays(7) },
            new SeedOrder { Status = "02:Åtgärda", Type = "Artikel", LibrarySigel = "Z", PatronName = "Björn Ärlig", TitleInformation = "En artikel om ångbåtar på Vänern", OriginalOrder = "Artikelbeställning, ofullständig referens.", Reference = "ref-atgarda-002", FollowUpDate = DateTime.Now.AddDays(-2) },
            new SeedOrder { Status = "03:Beställd", Type = "Inköpsförslag", LibrarySigel = "ZA", PatronName = "Gösta Lindqvist", TitleInformation = "Arkitekturens historia i Göteborg", OriginalOrder = "Inköpsförslag från student.", Reference = "ref-bestalld-003", FollowUpDate = DateTime.Now.AddDays(14) },
            new SeedOrder { Status = "04:Väntar", Type = "Bok", LibrarySigel = "Z", PatronName = "Märta Sjögren", TitleInformation = "Väntans tid - en roman", OriginalOrder = "Väntar på leverans från annat bibliotek.", Reference = "ref-vantar-004", FollowUpDate = DateTime.Now.AddDays(30) },
            new SeedOrder { Status = "05:Levererad", Type = "Artikel", LibrarySigel = "ZL", PatronName = "Astrid Nordqvist", TitleInformation = "Nordiska språkkontakter, en översikt", OriginalOrder = "Levererad artikel, redo för avhämtning.", Reference = "ref-levererad-005", FollowUpDate = DateTime.Now.AddDays(-1) },
            new SeedOrder { Status = "06:Annullerad", Type = "Bok", LibrarySigel = "Z", PatronName = "Örjan Blomqvist", TitleInformation = "En bok som aldrig kom fram", OriginalOrder = "Beställning som annullerades pga. att titeln redan fanns.", Reference = "ref-annullerad-006", FollowUpDate = DateTime.Now.AddDays(-10), CancellationReason = "Finns Z" },
            new SeedOrder { Status = "07:Överförd", Type = "Inköpsförslag", LibrarySigel = "ZA", PatronName = "Ingrid Åkerlund", TitleInformation = "Överförd till inköpsprocessen", OriginalOrder = "Överförd från fjärrlån till inköp.", Reference = "ref-overford-007", FollowUpDate = DateTime.Now.AddDays(20) },
            new SeedOrder { Status = "08:Inköpt", Type = "Bok", LibrarySigel = "Z", PatronName = "Sven-Åke Dahlström", TitleInformation = "En riktigt lång och krånglig titel med både kolon: bisats och frågetecken?", OriginalOrder = "Inköpt bok, katalogiseras.", Reference = "ref-inkopt-008", FollowUpDate = DateTime.Now.AddDays(5), PurchasedMaterial = "Bok" },
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
