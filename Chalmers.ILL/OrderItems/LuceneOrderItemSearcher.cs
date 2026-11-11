using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Chalmers.ILL.Configuration;
using Chalmers.ILL.Models;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Core;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Chalmers.ILL.OrderItems
{
    // The search index, embedded: a Lucene index replacing Elasticsearch (and the isolated mode's
    // in-memory fake) - see TODO-remove-dotnet-framework.md, "Ersätt Elasticsearch med
    // Lucene.NET". Lucene is the engine Elasticsearch is built on, and ES's query_string syntax is
    // Lucene's classic query syntax, so every query string the callers already build works
    // unchanged. The few things ES adds on top are added in EsLikeQueryParser.
    //
    // Each order is one document: every leaf value of the order's camelCase JSON (the field names
    // NEST gave the ES documents) as its own field, an "_all" field with all the string values for
    // free text without a field name, and the whole order as stored JSON ("_source", like ES), so a
    // search never has to read the order files.
    //
    // Where it lives (measured on the test server 2026-11-10/11, see the TODO): the index works on
    // local disk, because building it on /home (Azure Files) took an hour for 100k orders. A copy
    // is kept in DataPath/search-index, since local disk doesn't survive a restart:
    //   - Startup copies the copy back (~30 s for 1.5 GB) instead of reading the order files.
    //   - Every change is committed locally, and SyncToBackup copies the commit to DataPath ~30 s
    //     after the first unsynced change, and at shutdown. Lucene never changes a file once
    //     written, so a sync only copies the files that are new since the last one.
    //   - Crash safety: every change first appends its NodeId to DataPath/search-index-pending.txt,
    //     and a sync clears what it covered. Startup re-indexes whatever is listed there from the
    //     order files - the few orders changed after the last sync.
    //   - No copy, or one built by an older SchemaVersion: the index is rebuilt from the order
    //     files, in the background in the app (newest first, Status shows progress, searches see
    //     the orders indexed so far) or synchronously (the migration tool, tests).
    //
    // One instance per process, and only one process at a time: it holds Lucene's write.lock on
    // DataPath/search-index for its whole life, so an overlapping restart waits for the previous
    // process's final sync before restoring.
    public sealed class LuceneOrderItemSearcher : IOrderItemSearcher, IDisposable
    {
        private static readonly log4net.ILog _log = log4net.LogManager.GetLogger(typeof(LuceneOrderItemSearcher));

        private const LuceneVersion Version = LuceneVersion.LUCENE_48;

        // Bump whenever ToDocument or the analyzer changes - an index built by another version is
        // rebuilt from the order files at startup.
        private const string SchemaVersion = "1";
        private const string SchemaVersionKey = "chillinSchemaVersion";

        // Elasticsearch's index.max_result_window - Search(query) has always meant "at most this many".
        private const int MaxResults = 10000;

        private const string IdField = "_id";
        private const string SourceField = "_source";
        private const string AllField = "_all";
        private const string ExistsField = "_exists_";
        private const string CreateDateSortField = "_createDate";
        private const string ProviderNameKeywordField = "providerName.keyword";

        private static readonly TimeSpan SyncDelay = TimeSpan.FromSeconds(30);

        private static readonly JsonSerializer CamelCase = JsonSerializer.Create(new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        });

        private static readonly Regex ArrayIndex = new Regex(@"\[\d+\]", RegexOptions.Compiled);

        private static readonly Sort CreateDateDescending = new Sort(new SortField(CreateDateSortField, SortFieldType.INT64, true));

        private readonly string _dataPath;
        private readonly string _localPath;
        private readonly string _backupPath;
        private readonly string _pendingPath;
        private readonly bool _rebuildInBackground;
        private readonly object _writeLock = new object();
        private readonly object _syncLock = new object();
        private readonly Lazy<(IndexWriter Writer, SearcherManager Searchers)> _index;
        private readonly Timer _syncTimer;
        private readonly CancellationTokenSource _closing = new CancellationTokenSource();

        private Lucene.Net.Store.Lock _backupLock;
        private SnapshotDeletionPolicy _snapshots;
        // Set as soon as Open has them - a rebuild inside Open already syncs.
        private IndexWriter _writer;
        private bool _closed;
        private int _syncScheduled;

        // Background rebuild: orders changed while it runs, which it must not overwrite with the
        // older version it may have read from the file before the change.
        private volatile bool _rebuilding;
        private volatile bool _rebuildFailed;
        private int _rebuildIndexed;
        private int _rebuildTotal;
        private readonly ConcurrentDictionary<string, bool> _changedDuringRebuild = new ConcurrentDictionary<string, bool>();
        private Task _rebuildTask = Task.CompletedTask;

        public LuceneOrderItemSearcher(IChillinConfiguration config) : this(config.DataPath, rebuildInBackground: true)
        {
        }

        public LuceneOrderItemSearcher(string dataPath, string localPath = null, bool rebuildInBackground = false)
        {
            _dataPath = dataPath;
            _localPath = localPath ?? DefaultLocalPath(dataPath);
            _backupPath = IndexDirectory(dataPath);
            _pendingPath = Path.Combine(dataPath, "search-index-pending.txt");
            _rebuildInBackground = rebuildInBackground;
            _index = new Lazy<(IndexWriter, SearcherManager)>(Open);
            _syncTimer = new Timer(_ => SyncSafely(), null, Timeout.Infinite, Timeout.Infinite);
        }

        // The copy on DataPath. Also where the index itself lived before it moved to local disk,
        // so an index from then is restored like any copy.
        public static string IndexDirectory(string dataPath) => Path.Combine(dataPath, "search-index");

        // Under the temp directory (/tmp on App Service: the container's own disk), one per
        // DataPath so two apps on one machine don't share it.
        private static string DefaultLocalPath(string dataPath)
        {
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(dataPath)))).Substring(0, 12);
            return Path.Combine(Path.GetTempPath(), "chillin-search-index-" + hash.ToLowerInvariant());
        }

        private IndexWriter Writer => _index.Value.Writer;
        private SearcherManager Searchers => _index.Value.Searchers;

        // Opens the index now instead of on the first search, so the restore happens at startup.
        public void EnsureOpen() => _ = _index.Value;

        public SearchIndexStatus Status => _rebuilding
            ? new SearchIndexStatus { Rebuilding = true, Failed = _rebuildFailed, Indexed = _rebuildIndexed, Total = _rebuildTotal }
            : SearchIndexStatus.Ready;

        // Waits for a background rebuild to finish.
        public void WaitForRebuild() => _rebuildTask.GetAwaiter().GetResult();

        private (IndexWriter, SearcherManager) Open()
        {
            System.IO.Directory.CreateDirectory(_backupPath);
            _backupLock = FSDirectory.Open(_backupPath).MakeLock(IndexWriter.WRITE_LOCK_NAME);
            // A restart can overlap the previous process for a moment - wait for its final sync.
            if (!_backupLock.Obtain(60000))
                throw new LockObtainFailedException("Sökindexet på " + _backupPath + " är låst av en annan process.");

            if (System.IO.Directory.Exists(_localPath))
                System.IO.Directory.Delete(_localPath, recursive: true);
            System.IO.Directory.CreateDirectory(_localPath);

            var restored = Restore();

            _snapshots = new SnapshotDeletionPolicy(new KeepOnlyLastCommitDeletionPolicy());
            var config = new IndexWriterConfig(Version, new OrderItemAnalyzer())
            {
                OpenMode = OpenMode.CREATE_OR_APPEND,
                IndexDeletionPolicy = _snapshots
            };
            var writer = _writer = new IndexWriter(FSDirectory.Open(_localPath), config);
            var searchers = new SearcherManager(writer, true, null);

            if (restored)
            {
                CatchUp(writer);
                searchers.MaybeRefreshBlocking();
            }
            else if (_rebuildInBackground)
            {
                _rebuilding = true;
                _rebuildTask = Task.Run(() => RebuildFromFiles(writer, searchers));
            }
            else
            {
                RebuildFromFiles(writer, searchers);
            }

            return (writer, searchers);
        }

        // Copies the last commit in DataPath/search-index to local disk, if it's there and built
        // by this SchemaVersion.
        private bool Restore()
        {
            using var backup = FSDirectory.Open(_backupPath);
            if (!DirectoryReader.IndexExists(backup))
            {
                _log.Info("Ingen kopia av sökindexet i " + _backupPath + " - bygger om från orderfilerna.");
                return false;
            }

            var commit = new SegmentInfos();
            commit.Read(backup);
            commit.UserData.TryGetValue(SchemaVersionKey, out var builtBy);
            if (builtBy != SchemaVersion)
            {
                _log.Info($"Kopian av sökindexet är byggd av schemaversion '{builtBy}' - bygger om från orderfilerna.");
                return false;
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            foreach (var file in commit.GetFiles(backup, true))
                File.Copy(Path.Combine(_backupPath, file), Path.Combine(_localPath, file));
            _log.Info($"Sökindexet återställt från {_backupPath} på {sw.Elapsed.TotalSeconds:F1} s.");
            return true;
        }

        // The orders changed after the last sync before the previous process stopped - normally
        // none, since the final sync at shutdown clears the list.
        private void CatchUp(IndexWriter writer)
        {
            var nodeIds = PendingNodeIds();
            if (nodeIds.Count == 0)
                return;

            foreach (var nodeId in nodeIds)
            {
                var term = new Term(IdField, nodeId.ToString(CultureInfo.InvariantCulture));
                var file = OrderFileReader.OrderFilePath(_dataPath, nodeId);
                var item = File.Exists(file) ? OrderFileReader.Read(file, _log) : null;
                if (item != null)
                    writer.UpdateDocument(term, ToDocument(item));
                else
                    writer.DeleteDocuments(term);
            }
            Commit(writer);
            _log.Info($"Sökindexet ikapp med {nodeIds.Count} ordrar ändrade efter den senaste kopieringen.");
            ScheduleSync();
        }

        private List<int> PendingNodeIds()
        {
            if (!File.Exists(_pendingPath))
                return new List<int>();
            return File.ReadAllLines(_pendingPath)
                .Select(line => int.TryParse(line, out var id) ? id : (int?)null)
                .Where(id => id.HasValue)
                .Select(id => id.Value)
                .Distinct()
                .ToList();
        }

        private void RebuildFromFiles(IndexWriter writer, SearcherManager searchers)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var files = OrderFileReader.FilesNewestFirst(_dataPath);
                _rebuildTotal = files.Count;
                _log.Info($"Bygger sökindexet från {files.Count} orderfiler.");

                foreach (var item in OrderFileReader.ReadInParallel(files, _log, _closing.Token))
                {
                    var id = item.NodeId.ToString(CultureInfo.InvariantCulture);
                    var doc = ToDocument(item);
                    lock (_writeLock)
                    {
                        if (!_changedDuringRebuild.ContainsKey(id))
                            writer.UpdateDocument(new Term(IdField, id), doc);
                    }
                    if (Interlocked.Increment(ref _rebuildIndexed) % 1000 == 0)
                        searchers.MaybeRefresh();
                }

                lock (_writeLock)
                {
                    Commit(writer);
                    _rebuilding = false;
                    _changedDuringRebuild.Clear();
                }
                searchers.MaybeRefreshBlocking();
                _log.Info($"Sökindexet byggt: {_rebuildIndexed} ordrar på {sw.Elapsed.TotalSeconds:F0} s.");
                SyncToBackup();
            }
            catch (OperationCanceledException) when (_closing.IsCancellationRequested)
            {
                _log.Info("Ombyggnaden av sökindexet avbröts när appen stängdes.");
            }
            catch (Exception e) when (_rebuildInBackground)
            {
                // Searches keep the orders indexed so far; the next start rebuilds again, since
                // no copy was written.
                _rebuildFailed = true;
                _log.Error("Ombyggnaden av sökindexet misslyckades.", e);
            }
        }

        public void Added(OrderItemModel item) => Write(item.NodeId, (w, term) => w.UpdateDocument(term, ToDocument(item)));

        public void Modified(OrderItemModel item) => Added(item);

        public void Deleted(OrderItemModel item) => Write(item.NodeId, (w, term) => w.DeleteDocuments(term));

        // Replaces the whole index with these orders, and syncs the copy. Searches keep seeing the
        // old index until the new one is committed.
        public int Rebuild(IEnumerable<OrderItemModel> items)
        {
            EnsureOpen();
            if (_rebuilding)
                throw new InvalidOperationException("Sökindexet byggs redan om.");

            var count = 0;
            lock (_writeLock)
            {
                try
                {
                    Writer.DeleteAll();
                    foreach (var item in items)
                    {
                        Writer.AddDocument(ToDocument(item));
                        count++;
                    }
                    Commit(Writer);
                }
                catch (Exception e)
                {
                    throw new OrderItemSearchIndexingException("Failed to rebuild the search index.", e);
                }
                Searchers.MaybeRefreshBlocking();
            }
            SyncToBackup();
            return count;
        }

        private void Write(int nodeId, Action<IndexWriter, Term> change)
        {
            EnsureOpen();
            var id = nodeId.ToString(CultureInfo.InvariantCulture);
            lock (_writeLock)
            {
                try
                {
                    // Before the change, so a crash right after it still leaves the order listed.
                    File.AppendAllText(_pendingPath, id + "\n");
                    change(Writer, new Term(IdField, id));
                    if (_rebuilding)
                        _changedDuringRebuild[id] = true; // the rebuild's final commit includes it
                    else
                        Commit(Writer);
                }
                catch (Exception e)
                {
                    throw new OrderItemSearchIndexingException("Failed to update the search index.", e);
                }
                Searchers.MaybeRefreshBlocking();
            }
            if (!_rebuilding)
                ScheduleSync();
        }

        private static void Commit(IndexWriter writer)
        {
            writer.SetCommitData(new Dictionary<string, string> { [SchemaVersionKey] = SchemaVersion });
            writer.Commit();
        }

        // SyncDelay after the first unsynced change, so a burst of changes is one sync.
        private void ScheduleSync()
        {
            if (Interlocked.CompareExchange(ref _syncScheduled, 1, 0) == 0)
                _syncTimer.Change(SyncDelay, Timeout.InfiniteTimeSpan);
        }

        private void SyncSafely()
        {
            try
            {
                SyncToBackup();
            }
            catch (Exception e)
            {
                // The pending list still has every change since the last good sync; try again.
                _log.Error("Kunde inte kopiera sökindexet till " + _backupPath + ".", e);
                ScheduleSync();
            }
        }

        // Copies the last local commit to DataPath/search-index: the files it doesn't have yet,
        // then the commit's segments_N, then removes what that commit no longer uses. A reader of
        // the copy only ever sees complete commits - the segments_N names files already there.
        public void SyncToBackup()
        {
            lock (_syncLock)
            {
                Interlocked.Exchange(ref _syncScheduled, 0);
                if (_writer == null || _rebuilding || _closed)
                    return;

                IndexCommit commit;
                long pendingCovered;
                lock (_writeLock)
                {
                    commit = _snapshots.Snapshot();
                    pendingCovered = File.Exists(_pendingPath) ? new FileInfo(_pendingPath).Length : 0;
                }

                try
                {
                    var copied = 0;
                    var present = new DirectoryInfo(_backupPath).GetFiles().ToDictionary(f => f.Name, f => f.Length);
                    foreach (var file in commit.FileNames.Where(f => f != commit.SegmentsFileName).Append(commit.SegmentsFileName))
                    {
                        var source = Path.Combine(_localPath, file);
                        if (present.TryGetValue(file, out var length) && length == new FileInfo(source).Length)
                            continue;
                        var target = Path.Combine(_backupPath, file);
                        File.Copy(source, target + ".copying", overwrite: true);
                        File.Move(target + ".copying", target, overwrite: true);
                        copied++;
                    }

                    var keep = new HashSet<string>(commit.FileNames) { IndexWriter.WRITE_LOCK_NAME };
                    foreach (var stale in present.Keys.Where(f => !keep.Contains(f)))
                        File.Delete(Path.Combine(_backupPath, stale));

                    _log.Debug($"Sökindexet kopierat till {_backupPath}: {copied} nya filer.");
                }
                finally
                {
                    lock (_writeLock)
                    {
                        _snapshots.Release(commit);
                        _writer.DeleteUnusedFiles();
                    }
                }

                // Drop the NodeIds the copy now covers; keep any appended since the snapshot.
                lock (_writeLock)
                {
                    if (File.Exists(_pendingPath))
                    {
                        var all = File.ReadAllBytes(_pendingPath);
                        File.WriteAllBytes(_pendingPath, all.Skip((int)Math.Min(pendingCovered, all.Length)).ToArray());
                    }
                }
            }
        }

        public IEnumerable<OrderItemModel> Search(string query) => Run(query, 0, MaxResults).Items;

        public SearchResult Search(string query, int start, int size)
        {
            var (count, items) = Run(query, start, size);
            return new SearchResult { Count = count, Items = items };
        }

        // `fields` was an Elasticsearch source filter to save network traffic - the whole order is
        // returned here, which callers only read a couple of properties from anyway.
        public IEnumerable<OrderItemModel> Search(string query, int size, string[] fields) => Run(query, 0, size).Items;

        public IEnumerable<string> AggregatedProviders()
        {
            var res = new List<string> { "TIB", "Libris", "Subito" };

            var query = Parse("NOT status:(Ny OR Annullerad OR Inköpt OR Överförd) AND NOT providerName:(\"libris\" OR \"subito\" OR \"tib\")");
            var counts = new ProviderNameCounter();
            var searcher = Searchers.Acquire();
            try
            {
                searcher.Search(query, counts);
            }
            finally
            {
                Searchers.Release(searcher);
            }

            res.AddRange(counts.Counts.OrderByDescending(c => c.Value).Select(c => c.Key));
            return res;
        }

        private (int Count, List<OrderItemModel> Items) Run(string queryString, int start, int size)
        {
            var query = Parse(queryString);
            if (query == null)
                return (0, new List<OrderItemModel>());

            var searcher = Searchers.Acquire();
            try
            {
                var top = searcher.Search(query, null, Math.Max(1, start + size), CreateDateDescending);
                var items = top.ScoreDocs
                    .Skip(start)
                    .Take(size)
                    .Select(d => JsonConvert.DeserializeObject<OrderItemModel>(searcher.Doc(d.Doc).Get(SourceField)))
                    .ToList();
                return (top.TotalHits, items);
            }
            finally
            {
                Searchers.Release(searcher);
            }
        }

        // A query Elasticsearch would have rejected gives no hits, like the ES client did (NEST
        // returned an invalid response with no hits rather than throwing).
        private static Query Parse(string queryString)
        {
            try
            {
                return new EsLikeQueryParser().ParseEs(queryString);
            }
            catch (Exception e) when (e is ParseException || e is BooleanQuery.TooManyClausesException)
            {
                _log.Warn("Ogiltig sökfråga: " + queryString, e);
                return null;
            }
        }

        private static Document ToDocument(OrderItemModel item)
        {
            var doc = new Document
            {
                new StringField(IdField, item.NodeId.ToString(CultureInfo.InvariantCulture), Field.Store.YES),
                new StoredField(SourceField, JsonConvert.SerializeObject(item)),
                new NumericDocValuesField(CreateDateSortField, item.CreateDate.Ticks),
            };

            if (!string.IsNullOrEmpty(item.ProviderName))
                doc.Add(new SortedDocValuesField(ProviderNameKeywordField, new BytesRef(item.ProviderName)));

            foreach (var value in JObject.FromObject(item, CamelCase).Descendants().OfType<JValue>())
            {
                if (value.Type == JTokenType.Null)
                    continue;

                // ES flattens arrays: logItemsList[3].type is indexed as logItemsList.type.
                var field = ArrayIndex.Replace(value.Path, "");
                doc.Add(new StringField(ExistsField, field, Field.Store.NO));

                if (value.Type == JTokenType.Date)
                {
                    // The clock time as written, whatever its Kind - the range bounds are read the
                    // same way (see EsLikeQueryParser.ParseDate), and that's how ES compared them too.
                    doc.Add(new Int64Field(field, ((DateTime)value.Value).Ticks, Field.Store.NO));
                    continue;
                }

                var text = value.Type == JTokenType.Boolean
                    ? ((bool)value.Value ? "true" : "false")
                    : Convert.ToString(value.Value, CultureInfo.InvariantCulture);
                doc.Add(new TextField(field, text, Field.Store.NO));
                if (value.Type == JTokenType.String)
                    doc.Add(new TextField(AllField, text, Field.Store.NO));
            }

            return doc;
        }

        public void Dispose()
        {
            _syncTimer.Dispose();
            if (!_index.IsValueCreated)
                return;

            _closing.Cancel();
            try
            {
                _rebuildTask.Wait();
            }
            catch (AggregateException)
            {
                // Logged by the rebuild itself.
            }

            lock (_syncLock)
            {
                try
                {
                    SyncToBackup();
                }
                catch (Exception e)
                {
                    _log.Error("Kunde inte kopiera sökindexet till " + _backupPath + " vid avstängning - nästa start indexerar om de ändrade ordrarna.", e);
                }
                _closed = true; // a sync the timer had already queued finds nothing to do
            }

            lock (_writeLock)
            {
                Searchers.Dispose();
                Writer.Dispose();
            }
            _backupLock.Dispose();
            try
            {
                System.IO.Directory.Delete(_localPath, recursive: true);
            }
            catch (IOException)
            {
                // Wiped at the next start anyway.
            }
        }

        // Elasticsearch's standard analyzer: Unicode word splitting and lowercasing, no stopwords.
        // The position gap keeps a phrase from matching across two values of the same field (two
        // log entries, or two fields in _all) - 100 like ES.
        private sealed class OrderItemAnalyzer : Analyzer
        {
            protected override TokenStreamComponents CreateComponents(string fieldName, TextReader reader)
            {
                var source = new StandardTokenizer(Version, reader);
                TokenStream result = new StandardFilter(Version, source);
                result = new LowerCaseFilter(Version, result);
                return new TokenStreamComponents(source, result);
            }

            public override int GetPositionIncrementGap(string fieldName) => 100;
        }

        // The parts of Elasticsearch's query_string that aren't in Lucene's classic parser.
        private sealed class EsLikeQueryParser : QueryParser
        {
            public EsLikeQueryParser() : base(Version, AllField, new OrderItemAnalyzer())
            {
                DefaultOperator = OR_OPERATOR;
                AllowLeadingWildcard = false;
                // A multi-word value (status:01\:Ny -> "01 ny") means those words in a row.
                AutoGeneratePhraseQueries = true;
                // Range bounds keep their case (dates); wildcards are lowercased below instead.
                LowercaseExpandedTerms = false;
            }

            public Query ParseEs(string query)
            {
                if (string.IsNullOrWhiteSpace(query) || query.Trim() == "*")
                    return new MatchAllDocsQuery();
                return WithNegationsMeaningAllExcept(Parse(query));
            }

            // ES: a clause made of nothing but negations (-status:Ny) means "everything except".
            private static Query WithNegationsMeaningAllExcept(Query query)
            {
                if (query is BooleanQuery boolean)
                {
                    foreach (var clause in boolean.Clauses)
                        clause.Query = WithNegationsMeaningAllExcept(clause.Query);
                    if (boolean.Clauses.Count > 0 && boolean.Clauses.All(c => c.Occur == Occur.MUST_NOT))
                        boolean.Add(new MatchAllDocsQuery(), Occur.MUST);
                }
                return query;
            }

            protected override Query GetFieldQuery(string field, string queryText, bool quoted)
            {
                if (field == ExistsField)
                    return new TermQuery(new Term(ExistsField, queryText));
                return base.GetFieldQuery(field, queryText, quoted);
            }

            protected override Query GetPrefixQuery(string field, string termStr) => base.GetPrefixQuery(field, termStr.ToLowerInvariant());

            protected override Query GetWildcardQuery(string field, string termStr) => base.GetWildcardQuery(field, termStr.ToLowerInvariant());

            protected override Query GetRangeQuery(string field, string part1, string part2, bool startInclusive, bool endInclusive)
            {
                var lower = ParseDate(part1);
                var upper = ParseDate(part2);
                if ((lower.HasValue || IsOpen(part1)) && (upper.HasValue || IsOpen(part2)))
                    return NumericRangeQuery.NewInt64Range(field, lower?.Ticks, upper?.Ticks, startInclusive, endInclusive);
                return base.GetRangeQuery(field, part1?.ToLowerInvariant(), part2?.ToLowerInvariant(), startInclusive, endInclusive);
            }

            private static bool IsOpen(string bound) => string.IsNullOrEmpty(bound) || bound == "*";

            private static readonly string[] DateFormats = { "yyyy-MM-ddTHH:mm:ss.fffZ", "yyyy-MM-ddTHH:mm:ssZ", "yyyy-MM-dd", "yyyyMMdd" };

            private static DateTime? ParseDate(string text)
            {
                if (IsOpen(text))
                    return null;
                const DateTimeStyles styles = DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal;
                if (DateTime.TryParseExact(text, DateFormats, CultureInfo.InvariantCulture, styles, out var exact))
                    return exact;
                return null;
            }
        }

        // The ES terms aggregation on providerName.keyword: how many matching orders per provider.
        private sealed class ProviderNameCounter : ICollector
        {
            public Dictionary<string, int> Counts { get; } = new Dictionary<string, int>();
            private SortedDocValues _values;
            private readonly BytesRef _scratch = new BytesRef();

            public bool AcceptsDocsOutOfOrder => true;

            public void SetScorer(Scorer scorer)
            {
            }

            public void SetNextReader(AtomicReaderContext context) => _values = context.AtomicReader.GetSortedDocValues(ProviderNameKeywordField);

            public void Collect(int doc)
            {
                var ord = _values?.GetOrd(doc) ?? -1;
                if (ord < 0)
                    return;
                _values.LookupOrd(ord, _scratch);
                var name = _scratch.Utf8ToString();
                Counts[name] = Counts.TryGetValue(name, out var n) ? n + 1 : 1;
            }
        }
    }
}
