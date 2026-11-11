using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Chalmers.ILL.Configuration;
using Chalmers.ILL.Models;
using Chalmers.ILL.OrderItems;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

namespace Chalmers.ILL.Isolated
{
    // TEMPORARY - remove once measured ("Verifiera Lucene på Azure Files" in
    // TODO-remove-dotnet-framework.md, which also lists the removal). Measures Lucene on Azure
    // Files at production volume: generates fake orders straight into DataPath/orders on the
    // isolated test server, then rebuilds the search index and times both. Plain GETs so it's just
    // a URL in the browser; administrators only (no [AllowViewer]). Isolated mode only, which is
    // what keeps it inert in production until it's removed.
    //
    //   /LoadTestData/Generate?count=100000   start generating + rebuild (runs in the background)
    //   /LoadTestData/Status                  progress and timings
    //   /LoadTestData/Remove                  delete the generated orders + rebuild
    //   /LoadTestData/ReadOrders              only read every order file (time the SMB reads)
    //   /LoadTestData/LocalRebuild            build an index on local disk, copy it to /home,
    //                                         delete both - the live index is untouched
    //
    // The generated orders get NodeIds from FirstNodeId up, in their own buckets (orders/10000/
    // and up), so the test server's own orders are untouched and Remove can delete exactly them.
    // They aren't in orderid-index.json and don't move next-node-id.txt.
    public class LoadTestDataController : Controller
    {
        public const int FirstNodeId = 10_000_001;

        private static readonly log4net.ILog _log = log4net.LogManager.GetLogger(typeof(LoadTestDataController));
        private static readonly object _jobLock = new object();
        private static LoadTestJob _job;

        private readonly IChillinConfiguration _config;
        private readonly IOrderItemSearcher _orderItemSearcher;

        public LoadTestDataController(IChillinConfiguration config, IOrderItemSearcher orderItemSearcher)
        {
            _config = config;
            _orderItemSearcher = orderItemSearcher;
        }

        public ActionResult Generate(int count = 100000)
        {
            if (!_config.Isolated) return NotFound();
            if (count < 1 || count > 999_000) return BadRequest("count ska vara 1-999000.");

            return Start(job => job.Generate(count));
        }

        public ActionResult Remove()
        {
            if (!_config.Isolated) return NotFound();

            return Start(job => job.Remove());
        }

        public ActionResult ReadOrders()
        {
            if (!_config.Isolated) return NotFound();

            return Start(job => job.ReadOrders(), rebuild: false);
        }

        public ActionResult LocalRebuild()
        {
            if (!_config.Isolated) return NotFound();

            return Start(job => job.LocalRebuild(), rebuild: false);
        }

        public ActionResult Status()
        {
            if (!_config.Isolated) return NotFound();

            lock (_jobLock)
                return Json(_job?.Snapshot() ?? new { phase = "inget körts sedan appen startade" });
        }

        private ActionResult Start(Action<LoadTestJob> work, bool rebuild = true)
        {
            lock (_jobLock)
            {
                if (_job != null && _job.Running)
                    return Conflict(_job.Snapshot());

                var job = new LoadTestJob(_config.DataPath, _orderItemSearcher);
                _job = job;
                Task.Run(() => job.Run(work, rebuild));
                return Json(job.Snapshot());
            }
        }

        private class LoadTestJob
        {
            private readonly string _dataPath;
            private readonly IOrderItemSearcher _searcher;
            private readonly Stopwatch _total = new Stopwatch();
            private volatile string _phase = "startar";
            private int _written;
            private int _target;
            private double? _generateSeconds;
            private double? _removeSeconds;
            private double? _rebuildSeconds;
            private int? _indexed;
            private double? _readSeconds;
            private int? _read;
            private double? _localRebuildSeconds;
            private double? _copySeconds;
            private long? _indexMegabytes;
            private long _bytes;
            private string _error;

            public LoadTestJob(string dataPath, IOrderItemSearcher searcher)
            {
                _dataPath = dataPath;
                _searcher = searcher;
            }

            public bool Running { get; private set; } = true;

            public object Snapshot() => new
            {
                phase = _phase,
                running = Running,
                written = _written,
                target = _target,
                megabytes = Interlocked.Read(ref _bytes) / 1048576,
                generateSeconds = _generateSeconds,
                removeSeconds = _removeSeconds,
                rebuildSeconds = _rebuildSeconds,
                indexed = _indexed,
                readSeconds = _readSeconds,
                read = _read,
                localRebuildSeconds = _localRebuildSeconds,
                copySeconds = _copySeconds,
                indexMegabytes = _indexMegabytes,
                totalSeconds = Math.Round(_total.Elapsed.TotalSeconds, 1),
                error = _error,
            };

            public void Run(Action<LoadTestJob> work, bool rebuild)
            {
                _total.Start();
                try
                {
                    work(this);
                    if (rebuild)
                        Rebuild();
                    _phase = "klart";
                }
                catch (Exception e)
                {
                    _log.Error("Load test data job failed in phase " + _phase + ".", e);
                    _error = e.ToString();
                    _phase = "misslyckades";
                }
                finally
                {
                    _total.Stop();
                    Running = false;
                    _log.Info("Load test data job: " + JsonConvert.SerializeObject(Snapshot()));
                }
            }

            public void Generate(int count)
            {
                _phase = "genererar";
                _target = count;
                var sw = Stopwatch.StartNew();
                Parallel.For(FirstNodeId, FirstNodeId + count, new ParallelOptions { MaxDegreeOfParallelism = 4 }, nodeId =>
                {
                    var json = JsonConvert.SerializeObject(FakeOrders.Make(nodeId, new Random(nodeId * 7919)), Formatting.Indented); // as FileOrderItemManager
                    var path = Path.Combine(_dataPath, "orders", (nodeId / 1000).ToString("D3"), nodeId + ".json");
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    System.IO.File.WriteAllText(path, json);
                    Interlocked.Add(ref _bytes, Encoding.UTF8.GetByteCount(json));
                    Interlocked.Increment(ref _written);
                });
                _generateSeconds = Math.Round(sw.Elapsed.TotalSeconds, 1);
            }

            public void Remove()
            {
                _phase = "tar bort";
                var sw = Stopwatch.StartNew();
                foreach (var bucket in GeneratedBuckets(_dataPath))
                    Directory.Delete(bucket, recursive: true);
                _removeSeconds = Math.Round(sw.Elapsed.TotalSeconds, 1);
            }

            public void ReadOrders()
            {
                _phase = "läser orderfilerna";
                var sw = Stopwatch.StartNew();
                _read = OrderFileReader.ReadAll(_dataPath, _log).Count();
                _readSeconds = Math.Round(sw.Elapsed.TotalSeconds, 1);
            }

            // The proposed fix for the slow rebuild on /home: build on local disk, then copy the
            // finished index over in one pass.
            public void LocalRebuild()
            {
                var scratch = Path.Combine(Path.GetTempPath(), "chillin-loadtest-" + Guid.NewGuid().ToString("N"));
                var copy = Path.Combine(_dataPath, "search-index-copytest");
                try
                {
                    _phase = "bygger index på lokal disk";
                    var sw = Stopwatch.StartNew();
                    using (var local = new LuceneOrderItemSearcher(scratch))
                    {
                        local.EnsureOpen();
                        _indexed = local.Rebuild(OrderFileReader.ReadAll(_dataPath, _log));
                    }
                    _localRebuildSeconds = Math.Round(sw.Elapsed.TotalSeconds, 1);

                    _phase = "kopierar indexet till /home";
                    sw.Restart();
                    var source = LuceneOrderItemSearcher.IndexDirectory(scratch);
                    Directory.CreateDirectory(copy);
                    long bytes = 0;
                    foreach (var file in Directory.EnumerateFiles(source))
                    {
                        System.IO.File.Copy(file, Path.Combine(copy, Path.GetFileName(file)), overwrite: true);
                        bytes += new FileInfo(file).Length;
                    }
                    _copySeconds = Math.Round(sw.Elapsed.TotalSeconds, 1);
                    _indexMegabytes = bytes / 1048576;
                }
                finally
                {
                    _phase = "städar";
                    if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true);
                    if (Directory.Exists(copy)) Directory.Delete(copy, recursive: true);
                }
            }

            private void Rebuild()
            {
                _phase = "bygger om sökindexet";
                var sw = Stopwatch.StartNew();
                _indexed = _searcher.Rebuild(OrderFileReader.ReadAll(_dataPath, _log));
                _rebuildSeconds = Math.Round(sw.Elapsed.TotalSeconds, 1);
            }
        }

        public static IEnumerable<string> GeneratedBuckets(string dataPath)
        {
            var orders = Path.Combine(dataPath, "orders");
            if (!Directory.Exists(orders))
                return Enumerable.Empty<string>();

            return Directory.EnumerateDirectories(orders)
                .Where(d => int.TryParse(Path.GetFileName(d), out var bucket) && bucket >= FirstNodeId / 1000)
                .ToList();
        }
    }

    // The order generator from the 2026-11-09 benchmark ("Ersätt Elasticsearch med Lucene.NET"):
    // roughly an ILL archive - mostly closed orders, ~12 log entries each with a long tail, a fifth
    // of them long mails; ~17 KB per order. Small vocabulary, so the index's term dictionary is
    // smaller than with real data.
    public static class FakeOrders
    {
        private static readonly string[] Words = ("och att det som en på är av för med till den har inte om ett men var jag så " +
            "bok artikel beställning låntagare bibliotek leverans kopia tidskrift kapitel konferens rapport avhandling " +
            "fjärrlån lånetid återlämning förlängning mottagen skickad faktura kostnad betalning upphovsrätt digital " +
            "tryckt upplaga förlag utgivare volym nummer sidor sammanfattning kontakt kurs forskning institution " +
            "maskinteknik arkitektur fysik kemi datavetenskap matematik elektroteknik byggteknik miljö energi").Split(' ');
        private static readonly string[] FirstNames = "Anna Erik Maria Lars Karin Johan Sara Anders Eva Per Lena Nils Åsa Björn Märta Gösta Ingrid Sven Elin Örjan".Split(' ');
        private static readonly string[] LastNames = "Andersson Johansson Karlsson Nilsson Eriksson Larsson Olsson Persson Svensson Gustafsson Öhman Åkesson Lönnqvist Ärlig Söderström".Split(' ');
        private static readonly string[] Providers = "Libris Subito TIB BL Rapid Bibsys DTU KB Ingenta Elsevier".Split(' ');
        private static readonly (string Status, int Weight)[] Statuses =
        {
            ("05:Levererad", 45), ("06:Annullerad", 15), ("10:Återsänd", 20), ("07:Överförd", 3), ("08:Inköpt", 5),
            ("16:Förlorad", 1), ("01:Ny", 1), ("02:Åtgärda", 1), ("03:Beställd", 3), ("04:Väntar", 1), ("09:Mottagen", 1),
            ("11:Utlånad", 2), ("12:Krävd", 1), ("13:Transport", 1), ("14:Infodisk", 1), ("17:FOLIO", 1),
        };

        private static string Text(Random r, int minChars, int maxChars)
        {
            var target = r.Next(minChars, maxChars);
            var sb = new StringBuilder(target + 20);
            while (sb.Length < target)
            {
                sb.Append(Words[r.Next(Words.Length)]);
                sb.Append(r.Next(12) == 0 ? ".\n" : " ");
                if (r.Next(25) == 0) sb.Append(r.Next(100000)).Append(' ');
            }
            return sb.ToString();
        }

        private static string Pick((string Status, int Weight)[] items, Random r)
        {
            var x = r.Next(items.Sum(i => i.Weight));
            foreach (var i in items) { if ((x -= i.Weight) < 0) return i.Status; }
            return items[0].Status;
        }

        public static OrderItemModel Make(int nodeId, Random r)
        {
            var created = new DateTime(2012, 1, 1).AddMinutes(r.Next(0, 60 * 24 * 365 * 14));
            var status = Pick(Statuses, r);
            var type = r.Next(10) < 6 ? "Bok" : r.Next(10) < 8 ? "Artikel" : "Inköpsförslag";
            var first = FirstNames[r.Next(FirstNames.Length)];
            var last = LastNames[r.Next(LastNames.Length)];
            var anonymized = created < new DateTime(2024, 1, 1) && r.Next(10) < 7;

            var logs = new List<LogItem>();
            var logCount = 3 + (int)Math.Round(-Math.Log(1 - r.NextDouble()) * 9);
            var at = created;
            for (var i = 0; i < logCount; i++)
            {
                at = at.AddHours(r.Next(1, 200));
                var mail = r.Next(5) == 0;
                logs.Add(new LogItem
                {
                    Id = Guid.NewGuid(),
                    EventId = $"event-{r.Next(10000)}-{r.Next(40)}",
                    Type = mail ? "MAIL" : new[] { "STATUS", "LOG", "REF", "TYP", "DATE", "LEVERANS" }[r.Next(6)],
                    Message = mail ? Text(r, 400, 2500) : Text(r, 20, 120),
                    MemberName = r.Next(3) == 0 ? "System" : FirstNames[r.Next(FirstNames.Length)] + " " + LastNames[r.Next(LastNames.Length)],
                    CreateDate = at,
                });
            }

            return new OrderItemModel
            {
                OrderId = $"cthb-{Guid.NewGuid().ToString("N").Substring(0, 8)}-{nodeId}",
                NodeId = nodeId,
                CreateDate = created,
                UpdateDate = at,
                FollowUpDate = at.AddDays(r.Next(-30, 30)),
                OriginalOrder = anonymized ? "Anonymiserad" : Text(r, 300, 3000),
                Reference = Text(r, 30, 200),
                TitleInformation = Text(r, 15, 120),
                Type = type,
                TypeId = type.Length,
                Status = status,
                StatusId = int.Parse(status.Substring(0, 2)),
                StatusString = status.Substring(3),
                PreviousStatus = "01:Ny",
                PreviousStatusString = "Ny",
                DeliveryLibrary = new[] { "Huvudbiblioteket", "Lindholmenbiblioteket", "Arkitekturbiblioteket" }[r.Next(3)],
                CancellationReason = status == "06:Annullerad" ? "Finns Z" : "",
                PurchasedMaterial = "",
                PatronName = anonymized ? "" : first + " " + last,
                PatronEmail = anonymized ? "" : $"{first}.{last}@example.invalid".ToLowerInvariant(),
                PatronCardNo = anonymized ? "" : r.Next(100000000, 999999999).ToString(),
                PatronAffiliation = "Student",
                ProviderName = status == "01:Ny" ? null : Providers[r.Next(Providers.Length)],
                ProviderOrderId = r.Next(1000000).ToString(),
                ProviderInformation = Text(r, 0, 150),
                ProviderDueDate = at.AddDays(28),
                EditedBy = "",
                LogItemsList = logs,
                Log = JsonConvert.SerializeObject(logs),
                Attachments = "[]",
                AttachmentList = new List<OrderAttachment>(),
                DueDate = at.AddDays(28),
                DeliveryDate = status == "05:Levererad" ? at : new DateTime(1970, 1, 1),
                BookId = "",
                SierraInfo = new SierraModel { record_id = anonymized ? 0 : r.Next(1000, 99999) },
                IsAnonymized = anonymized,
                IsAnonymizedAutomatically = anonymized,
            };
        }
    }
}
