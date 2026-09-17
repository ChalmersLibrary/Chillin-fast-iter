using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chalmers.ILL.Configuration;
using Chalmers.ILL.Isolated;
using Chalmers.ILL.Models;
using Chalmers.ILL.Tests.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chalmers.ILL.Tests.Isolated
{
    // Isolerat läge steg B, "Bygg den minimala sökersättaren". Table-driven against the exact
    // frågekravlistan in TODO-remove-dotnet-framework.md - every query form actually issued by
    // real call sites (verbatim where practical), plus the analyzer-dependent tokenization cases
    // the TODO specifically calls out as easy to get wrong.
    [TestClass]
    public class InMemoryOrderItemSearcherTest
    {
        private InMemoryOrderItemSearcher CreateSearcher() =>
            new InMemoryOrderItemSearcher(new StubChillinConfiguration
            {
                DataPath = Path.Combine(Path.GetTempPath(), "chillin-inmemorysearcher-test-" + Guid.NewGuid())
            });

        private static OrderItemModel NewOrder(int nodeId, string status = "", string type = "", string providerName = "")
        {
            return new OrderItemModel
            {
                NodeId = nodeId,
                Status = status,
                Type = type,
                ProviderName = providerName,
                CreateDate = new DateTime(2026, 1, 1).AddDays(nodeId)
            };
        }

        // --- Field lookup, tokenization, escaping -----------------------------------------

        [TestMethod]
        public void Search_PlainFieldValue_Matches()
        {
            var searcher = CreateSearcher();
            searcher.Added(NewOrder(1, status: "03:Beställd"));

            Assert.AreEqual(1, searcher.Search("status:Beställd").Count());
        }

        [TestMethod]
        public void Search_EscapedColonInValue_MatchesSameAsPlainWord()
        {
            // status:01\:Ny - the exact form ChalmersOrderItemsMailSource.cs uses.
            var searcher = CreateSearcher();
            searcher.Added(NewOrder(1, status: "01:Ny"));

            Assert.AreEqual(1, searcher.Search(@"status:01\:Ny").Count());
        }

        [TestMethod]
        public void Search_QuotedPhraseWithColon_Matches()
        {
            var searcher = CreateSearcher();
            searcher.Added(NewOrder(1, status: "05:Levererad"));

            Assert.AreEqual(1, searcher.Search("status:\"05:Levererad\"").Count());
        }

        [TestMethod]
        public void Search_EscapedQuestionMark_Matches()
        {
            // status:(... OR Förlorad\?) from ChalmersILLDiskPageController.
            var searcher = CreateSearcher();
            searcher.Added(NewOrder(1, status: "Förlorad?"));

            Assert.AreEqual(1, searcher.Search(@"status:Förlorad\?").Count());
        }

        [TestMethod]
        public void Search_FieldValue_DoesNotMatchDifferentValue()
        {
            var searcher = CreateSearcher();
            searcher.Added(NewOrder(1, status: "01:Ny"));

            Assert.AreEqual(0, searcher.Search("status:Beställd").Count());
        }

        [TestMethod]
        public void Search_NestedDottedField_MatchesSierraInfoRecordId()
        {
            var searcher = CreateSearcher();
            var order = NewOrder(1);
            order.SierraInfo.record_id = 12345;
            searcher.Added(order);

            Assert.AreEqual(1, searcher.Search("sierraInfo.record_id:12345").Count());
            Assert.AreEqual(0, searcher.Search("sierraInfo.record_id:99999").Count());
        }

        // --- Boolean grammar: AND / OR / NOT / parentheses --------------------------------

        [TestMethod]
        public void Search_RepeatedFieldOr_MatchesAnyValue()
        {
            // The statistics page's actual JS-generated form: "status:v1 OR status:v2 OR status:v3".
            var searcher = CreateSearcher();
            searcher.Added(NewOrder(1, status: "Utlånad"));
            searcher.Added(NewOrder(2, status: "Krävd"));
            searcher.Added(NewOrder(3, status: "Mottagen"));

            var results = searcher.Search("status:Utlånad OR status:Krävd OR status:Transport").Select(o => o.NodeId).ToList();

            CollectionAssert.AreEquivalent(new[] { 1, 2 }, results);
        }

        [TestMethod]
        public void Search_FieldGroupedOr_SameAsRepeatedFieldOr()
        {
            var searcher = CreateSearcher();
            searcher.Added(NewOrder(1, type: "Bok", status: "Infodisk"));
            searcher.Added(NewOrder(2, type: "Artikel", status: "Transport"));
            searcher.Added(NewOrder(3, type: "Bok", status: "Beställd"));

            var results = searcher.Search("((type:Bok AND status:(Infodisk OR Utlånad OR Transport OR Krävd)) OR (type:Artikel AND status:Transport))").Select(o => o.NodeId).ToList();

            CollectionAssert.AreEquivalent(new[] { 1, 2 }, results);
        }

        [TestMethod]
        public void Search_NegatedParenthesizedGroup_ExcludesWholeGroup()
        {
            // BulkDataManager's exact form: -(status:Transport AND (previousStatus:X OR previousStatus:Y))
            var searcher = CreateSearcher();
            var excluded = NewOrder(1, status: "Transport");
            excluded.PreviousStatus = "Utlånad";
            var included = NewOrder(2, status: "Transport");
            included.PreviousStatus = "Beställd";
            searcher.Added(excluded);
            searcher.Added(included);

            var results = searcher.Search("status:Transport AND -(status:Transport AND (previousStatus:Utlånad OR previousStatus:Krävd))").Select(o => o.NodeId).ToList();

            CollectionAssert.AreEquivalent(new[] { 2 }, results);
        }

        [TestMethod]
        public void Search_LeadingMinusOnSingleTerm_ExcludesIt()
        {
            var searcher = CreateSearcher();
            searcher.Added(NewOrder(1, status: "Ny"));
            searcher.Added(NewOrder(2, status: "Beställd"));

            var results = searcher.Search("-status:Ny").Select(o => o.NodeId).ToList();

            CollectionAssert.AreEquivalent(new[] { 2 }, results);
        }

        // --- _exists_ / !_exists_ -----------------------------------------------------------

        [TestMethod]
        public void Search_Exists_MatchesWhenFieldPresent()
        {
            var searcher = CreateSearcher();
            searcher.Added(NewOrder(1));

            // Every current-schema document always has every property (see OrderItemDocument's
            // documented divergence), so _exists_ on a real property is always true here.
            Assert.AreEqual(1, searcher.Search("_exists_:isAnonymized").Count());
        }

        [TestMethod]
        public void Search_NegatedExists_NeverMatchesOnCurrentSchemaDocuments()
        {
            var searcher = CreateSearcher();
            searcher.Added(NewOrder(1));

            Assert.AreEqual(0, searcher.Search("!_exists_:isAnonymized").Count());
        }

        [TestMethod]
        public void Search_AnonymizeOldOrderItemsQuery_MatchesOnValueAloneSinceExistsAlwaysTrue()
        {
            // AnonymizeOldOrderItems' exact form (abbreviated: real query also has an updateDate
            // range and a status list, covered by the range/OR tests separately).
            var searcher = CreateSearcher();
            var notAnonymized = NewOrder(1);
            notAnonymized.IsAnonymizedAutomatically = false;
            var alreadyAnonymized = NewOrder(2);
            alreadyAnonymized.IsAnonymizedAutomatically = true;
            searcher.Added(notAnonymized);
            searcher.Added(alreadyAnonymized);

            var results = searcher.Search("(isAnonymizedAutomatically:false OR (!_exists_:isAnonymizedAutomatically))").Select(o => o.NodeId).ToList();

            CollectionAssert.AreEquivalent(new[] { 1 }, results);
        }

        // --- Date ranges ---------------------------------------------------------------------

        [TestMethod]
        public void Search_DateRange_BothFormats_MatchInclusive()
        {
            var searcher = CreateSearcher();
            var inRange = NewOrder(1);
            inRange.FollowUpDate = new DateTime(2026, 6, 15);
            var outOfRange = NewOrder(2);
            outOfRange.FollowUpDate = new DateTime(2027, 6, 1);
            searcher.Added(inRange);
            searcher.Added(outOfRange);

            var isoForm = searcher.Search(@"followUpDate:[1975-01-01T00:00:00.000Z TO 2026-12-31T23:59:59.999Z]").Select(o => o.NodeId).ToList();
            var compactForm = searcher.Search("followUpDate:[19750101 TO 20261231]").Select(o => o.NodeId).ToList();

            CollectionAssert.AreEquivalent(new[] { 1 }, isoForm);
            CollectionAssert.AreEquivalent(new[] { 1 }, compactForm);
        }

        [TestMethod]
        public void Search_DateRange_OpenLowerBound_MatchesEverythingBeforeUpper()
        {
            var searcher = CreateSearcher();
            var old = NewOrder(1);
            old.UpdateDate = new DateTime(2020, 1, 1);
            var recent = NewOrder(2);
            recent.UpdateDate = new DateTime(2026, 6, 1);
            searcher.Added(old);
            searcher.Added(recent);

            var results = searcher.Search("updateDate:[* TO 2025-01-01]").Select(o => o.NodeId).ToList();

            CollectionAssert.AreEquivalent(new[] { 1 }, results);
        }

        // --- Free text (no field prefix) -----------------------------------------------------

        [TestMethod]
        public void Search_BareWord_MatchesAnyDefaultTextField()
        {
            var searcher = CreateSearcher();
            var order = NewOrder(1);
            order.PatronName = "Andersson";
            searcher.Added(order);
            searcher.Added(NewOrder(2));

            var results = searcher.Search("Andersson").Select(o => o.NodeId).ToList();

            CollectionAssert.AreEquivalent(new[] { 1 }, results);
        }

        [TestMethod]
        public void Search_QuotedBarePhrase_MatchesOrderIdLikeReference()
        {
            // ChalmersILLOrderListPageController wraps a recognised order-id pattern in quotes
            // with no field prefix before searching.
            var searcher = CreateSearcher();
            var order = NewOrder(1);
            order.Reference = "cthb-ab12cd34-5";
            searcher.Added(order);
            searcher.Added(NewOrder(2));

            var results = searcher.Search("\"cthb-ab12cd34-5\"").Select(o => o.NodeId).ToList();

            CollectionAssert.AreEquivalent(new[] { 1 }, results);
        }

        [TestMethod]
        public void Search_MatchAllWildcard_ReturnsEverything()
        {
            var searcher = CreateSearcher();
            searcher.Added(NewOrder(1));
            searcher.Added(NewOrder(2));

            Assert.AreEqual(2, searcher.Search("*").Count());
        }

        // --- AggregatedProviders --------------------------------------------------------------

        [TestMethod]
        public void AggregatedProviders_AlwaysStartsWithTibLibrisSubito_ThenRealCountsDescending()
        {
            var searcher = CreateSearcher();
            searcher.Added(NewOrder(1, status: "Beställd", providerName: "Worldcat"));
            searcher.Added(NewOrder(2, status: "Beställd", providerName: "Worldcat"));
            searcher.Added(NewOrder(3, status: "Beställd", providerName: "BLDSC"));
            // Excluded from the real aggregation by status:
            searcher.Added(NewOrder(4, status: "Ny", providerName: "Worldcat"));

            var result = searcher.AggregatedProviders().ToList();

            Assert.AreEqual("TIB", result[0]);
            Assert.AreEqual("Libris", result[1]);
            Assert.AreEqual("Subito", result[2]);
            CollectionAssert.AreEqual(new[] { "Worldcat", "BLDSC" }, result.Skip(3).ToList());
        }

        // --- Added / Modified / Deleted keep the in-memory index in sync ---------------------

        [TestMethod]
        public void AddedModifiedDeleted_KeepIndexInSync()
        {
            var searcher = CreateSearcher();
            var order = NewOrder(1, status: "Ny");
            searcher.Added(order);
            Assert.AreEqual(1, searcher.Search("status:Ny").Count());

            order.Status = "Beställd";
            searcher.Modified(order);
            Assert.AreEqual(0, searcher.Search("status:Ny").Count());
            Assert.AreEqual(1, searcher.Search("status:Beställd").Count());

            searcher.Deleted(order);
            Assert.AreEqual(0, searcher.Search("*").Count());
        }

        // --- Loads existing order files from DataPath/orders/ at construction ----------------

        [TestMethod]
        public void Constructor_LoadsExistingOrderFilesFromDataPath()
        {
            var dataPath = Path.Combine(Path.GetTempPath(), "chillin-inmemorysearcher-load-" + Guid.NewGuid());
            var bucketDir = Path.Combine(dataPath, "orders", "000");
            Directory.CreateDirectory(bucketDir);
            var order = NewOrder(42, status: "Ny");
            File.WriteAllText(Path.Combine(bucketDir, "42.json"), Newtonsoft.Json.JsonConvert.SerializeObject(order));

            var searcher = new InMemoryOrderItemSearcher(new StubChillinConfiguration { DataPath = dataPath });

            Assert.AreEqual(1, searcher.Search("status:Ny").Count());
        }
    }
}
