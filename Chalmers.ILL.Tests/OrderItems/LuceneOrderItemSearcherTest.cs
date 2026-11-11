using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chalmers.ILL.Models;
using Chalmers.ILL.OrderItems;
using Chalmers.ILL.Tests.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chalmers.ILL.Tests.OrderItems
{
    // Table-driven against the frågekravlista in TODO-remove-dotnet-framework.md (written for the
    // isolated mode's in-memory searcher, which this replaced together with Elasticsearch) - every
    // query form actually issued by real call sites (verbatim where practical), plus the
    // analyzer-dependent tokenization cases the TODO specifically calls out as easy to get wrong.
    [TestClass]
    public class LuceneOrderItemSearcherTest
    {
        private readonly List<LuceneOrderItemSearcher> _opened = new List<LuceneOrderItemSearcher>();

        [TestCleanup]
        public void CloseIndexes() => _opened.ForEach(s => s.Dispose());

        private static string NewDataPath() => Path.Combine(Path.GetTempPath(), "chillin-lucenesearcher-test-" + Guid.NewGuid());

        private LuceneOrderItemSearcher CreateSearcher(string dataPath = null, bool rebuildInBackground = false)
        {
            var searcher = new LuceneOrderItemSearcher(dataPath ?? NewDataPath(), rebuildInBackground: rebuildInBackground);
            _opened.Add(searcher);
            return searcher;
        }

        private static void WriteOrderFile(string dataPath, OrderItemModel order)
        {
            var bucketDir = Path.Combine(dataPath, "orders", (order.NodeId / 1000).ToString("D3"));
            Directory.CreateDirectory(bucketDir);
            File.WriteAllText(Path.Combine(bucketDir, order.NodeId + ".json"), Newtonsoft.Json.JsonConvert.SerializeObject(order));
        }

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
            // A bool is never null, so _exists_ on one is always true.
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

        // --- Added / Modified / Deleted keep the index in sync --------------------------------

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

        // --- Beyond the in-memory fake: what Elasticsearch did and the fake didn't ------------

        [TestMethod]
        public void Search_TrailingWildcard_Truncates()
        {
            // The search tips promise truncation; the in-memory fake never supported it.
            var searcher = CreateSearcher();
            var order = NewOrder(1);
            order.TitleInformation = "En avhandling om ångbåtar";
            searcher.Added(order);
            searcher.Added(NewOrder(2));

            CollectionAssert.AreEquivalent(new[] { 1 }, searcher.Search("avhandl*").Select(o => o.NodeId).ToList());
            CollectionAssert.AreEquivalent(new[] { 1 }, searcher.Search("titleInformation:Avhandl*").Select(o => o.NodeId).ToList());
        }

        [TestMethod]
        public void Search_BareWord_MatchesLogEntries()
        {
            var searcher = CreateSearcher();
            var order = NewOrder(1);
            order.LogItemsList = new List<LogItem> { new LogItem { Type = "MAIL", Message = "Skickat till Lönnqvist" } };
            searcher.Added(order);
            searcher.Added(NewOrder(2));

            CollectionAssert.AreEquivalent(new[] { 1 }, searcher.Search("lönnqvist").Select(o => o.NodeId).ToList());
            CollectionAssert.AreEquivalent(new[] { 1 }, searcher.Search("logItemsList.type:MAIL").Select(o => o.NodeId).ToList());
        }

        [TestMethod]
        public void Search_Phrase_DoesNotMatchAcrossTwoFields()
        {
            var searcher = CreateSearcher();
            var order = NewOrder(1);
            order.TitleInformation = "Om upplaga";
            order.PatronName = "förlag";
            searcher.Added(order);

            Assert.AreEqual(0, searcher.Search("\"upplaga förlag\"").Count());
            Assert.AreEqual(1, searcher.Search("\"om upplaga\"").Count());
        }

        [TestMethod]
        public void Search_InvalidQuery_GivesNoHitsInsteadOfThrowing()
        {
            var searcher = CreateSearcher();
            searcher.Added(NewOrder(1, status: "Ny"));

            Assert.AreEqual(0, searcher.Search("status:(Ny").Count());
            Assert.AreEqual(0, searcher.Search("status:(Ny", 0, 50).Count);
        }

        [TestMethod]
        public void Search_Paged_CountsAllHitsAndSortsNewestFirst()
        {
            var searcher = CreateSearcher();
            for (var i = 1; i <= 5; i++)
                searcher.Added(NewOrder(i, status: "Ny"));

            var page = searcher.Search("status:Ny", 1, 2);

            Assert.AreEqual(5, page.Count);
            CollectionAssert.AreEqual(new[] { 4, 3 }, page.Items.Select(o => o.NodeId).ToList());
        }

        [TestMethod]
        public void Search_ReturnsTheWholeStoredOrder()
        {
            var searcher = CreateSearcher();
            var order = NewOrder(1, status: "Ny");
            order.LogItemsList = new List<LogItem> { new LogItem { Type = "LOG", Message = "Hej" } };
            order.SierraInfo.record_id = 4242;
            searcher.Added(order);

            var found = searcher.Search("status:Ny").Single();

            Assert.AreEqual("Hej", found.LogItemsList.Single().Message);
            Assert.AreEqual(4242, found.SierraInfo.record_id);
            Assert.AreEqual(order.CreateDate, found.CreateDate);
        }

        // --- The index on disk ----------------------------------------------------------------

        [TestMethod]
        public void Open_BuildsAMissingIndexFromTheOrderFiles()
        {
            var dataPath = NewDataPath();
            WriteOrderFile(dataPath, NewOrder(42, status: "Ny"));
            WriteOrderFile(dataPath, NewOrder(1042, status: "Beställd"));

            var searcher = CreateSearcher(dataPath);

            Assert.AreEqual(1, searcher.Search("status:Ny").Count());
            Assert.AreEqual(2, searcher.Search("*").Count());
        }

        [TestMethod]
        public void Open_ReusesAnExistingIndexInsteadOfRebuilding()
        {
            var dataPath = NewDataPath();
            var first = CreateSearcher(dataPath);
            // Only in the index, no order file - a rebuild would lose it.
            first.Added(NewOrder(7, status: "Ny"));
            first.Dispose();
            _opened.Remove(first);

            var reopened = CreateSearcher(dataPath);

            Assert.AreEqual(1, reopened.Search("status:Ny").Count());
        }

        [TestMethod]
        public void Rebuild_ReplacesTheWholeIndex()
        {
            var searcher = CreateSearcher();
            searcher.Added(NewOrder(1, status: "Ny"));

            var count = searcher.Rebuild(new[] { NewOrder(2, status: "Beställd"), NewOrder(3, status: "Beställd") });

            Assert.AreEqual(2, count);
            CollectionAssert.AreEquivalent(new[] { 2, 3 }, searcher.Search("*").Select(o => o.NodeId).ToList());
        }

        // --- Local index, copy on DataPath -----------------------------------------------------

        private static string BackupPath(string dataPath) => LuceneOrderItemSearcher.IndexDirectory(dataPath);
        private static string PendingPath(string dataPath) => Path.Combine(dataPath, "search-index-pending.txt");

        // What's on DataPath at this moment, as a new DataPath - what the next process would find
        // if this one died right now, without its final sync.
        private static string CopyAsIfCrashedNow(string dataPath)
        {
            var copy = NewDataPath();
            foreach (var file in Directory.EnumerateFiles(dataPath, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(copy, Path.GetRelativePath(dataPath, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(file, target);
            }
            return copy;
        }

        [TestMethod]
        public void Restart_RestoresTheCopy_WithoutReadingTheOrderFiles()
        {
            var dataPath = NewDataPath();
            WriteOrderFile(dataPath, NewOrder(42, status: "Ny"));
            WriteOrderFile(dataPath, NewOrder(1042, status: "Beställd"));
            var first = CreateSearcher(dataPath);
            first.EnsureOpen();
            first.Dispose();
            _opened.Remove(first);
            Directory.Delete(Path.Combine(dataPath, "orders"), recursive: true);

            var reopened = CreateSearcher(dataPath);

            Assert.AreEqual(2, reopened.Search("*").Count());
        }

        [TestMethod]
        public void Crash_ChangesAfterTheLastSyncAreReindexedFromTheOrderFiles()
        {
            var dataPath = NewDataPath();
            var gone = NewOrder(3, status: "Levererad");
            WriteOrderFile(dataPath, gone);
            var searcher = CreateSearcher(dataPath);
            searcher.EnsureOpen();
            searcher.SyncToBackup();

            var added = NewOrder(5, status: "Ny");
            WriteOrderFile(dataPath, added);
            searcher.Added(added);
            File.Delete(Path.Combine(dataPath, "orders", "000", "3.json"));
            searcher.Deleted(gone);

            var afterCrash = CreateSearcher(CopyAsIfCrashedNow(dataPath));

            CollectionAssert.AreEqual(new[] { 5 }, afterCrash.Search("*").Select(o => o.NodeId).ToList());
        }

        [TestMethod]
        public void Sync_ClearsThePendingList_AndTheCopyStaysComplete()
        {
            var dataPath = NewDataPath();
            var searcher = CreateSearcher(dataPath);
            for (var i = 1; i <= 30; i++)
            {
                searcher.Added(NewOrder(i, status: "Ny"));
                if (i % 10 == 0)
                    searcher.SyncToBackup();
            }
            searcher.Rebuild(new[] { NewOrder(100, status: "Beställd") });
            searcher.Added(NewOrder(101, status: "Beställd"));
            searcher.SyncToBackup();

            Assert.AreEqual(0, new FileInfo(PendingPath(dataPath)).Length);
            // No order files and an empty pending list: everything comes from the copy.
            var restored = CreateSearcher(CopyAsIfCrashedNow(dataPath));
            CollectionAssert.AreEquivalent(new[] { 100, 101 }, restored.Search("*").Select(o => o.NodeId).ToList());
        }

        [TestMethod]
        public void Sync_KeepsChangesItDidNotCoverInThePendingList()
        {
            var dataPath = NewDataPath();
            var searcher = CreateSearcher(dataPath);
            searcher.Added(NewOrder(1, status: "Ny"));
            searcher.SyncToBackup();
            searcher.Added(NewOrder(2, status: "Ny"));

            CollectionAssert.AreEqual(new[] { "2" }, File.ReadAllLines(PendingPath(dataPath)));
        }

        [TestMethod]
        public void BackgroundRebuild_IndexesEverything_AndKeepsChangesMadeMeanwhile()
        {
            var dataPath = NewDataPath();
            for (var i = 1; i <= 3000; i++)
                WriteOrderFile(dataPath, NewOrder(i, status: "Gammal"));
            var searcher = CreateSearcher(dataPath, rebuildInBackground: true);

            searcher.EnsureOpen();
            var during = searcher.Status;
            // Order 1 is the oldest, so the rebuild reads its (now stale) file last.
            searcher.Modified(NewOrder(1, status: "Ny"));
            searcher.WaitForRebuild();

            Assert.IsTrue(during.Rebuilding);
            Assert.IsFalse(searcher.Status.Rebuilding);
            Assert.AreEqual(3000, searcher.Search("*").Count());
            CollectionAssert.AreEqual(new[] { 1 }, searcher.Search("status:Ny").Select(o => o.NodeId).ToList());
            Assert.IsTrue(Directory.EnumerateFiles(BackupPath(dataPath), "segments_*").Any(), "Kopian skrivs när ombyggnaden är klar.");
        }

        [TestMethod]
        public void FilesNewestFirst_OrdersByNodeIdDescendingAcrossBuckets()
        {
            var dataPath = NewDataPath();
            foreach (var id in new[] { 5, 1500, 999, 2001, 1000 })
                WriteOrderFile(dataPath, NewOrder(id));
            File.WriteAllText(Path.Combine(dataPath, "orders", "orderid-index.json"), "{}");

            var ids = OrderFileReader.FilesNewestFirst(dataPath).Select(f => int.Parse(Path.GetFileNameWithoutExtension(f))).ToList();

            CollectionAssert.AreEqual(new[] { 2001, 1500, 1000, 999, 5 }, ids);
        }

        [TestMethod]
        public void ReadInParallel_ReadsEveryFile_SkippingCorruptOnes()
        {
            var dataPath = NewDataPath();
            for (var i = 1; i <= 500; i++)
                WriteOrderFile(dataPath, NewOrder(i));
            File.WriteAllText(Path.Combine(dataPath, "orders", "000", "7.json"), "{ trasig");

            var ids = OrderFileReader.ReadInParallel(OrderFileReader.FilesNewestFirst(dataPath), null, readers: 4, buffered: 8)
                .Select(o => o.NodeId).ToList();

            Assert.AreEqual(499, ids.Count);
            CollectionAssert.AllItemsAreUnique(ids);
            Assert.IsFalse(ids.Contains(7));
        }

        [TestMethod]
        public void ReadInParallel_StoppingEarly_DoesNotHang()
        {
            var dataPath = NewDataPath();
            for (var i = 1; i <= 500; i++)
                WriteOrderFile(dataPath, NewOrder(i));

            var task = System.Threading.Tasks.Task.Run(() =>
                OrderFileReader.ReadInParallel(OrderFileReader.FilesNewestFirst(dataPath), null, readers: 4, buffered: 2).Take(3).Count());

            Assert.IsTrue(task.Wait(TimeSpan.FromSeconds(10)), "Läsarna väntar på en full kö som ingen tömmer.");
            Assert.AreEqual(3, task.Result);
        }
    }
}
