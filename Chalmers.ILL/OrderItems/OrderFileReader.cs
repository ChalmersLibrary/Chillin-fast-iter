using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using Chalmers.ILL.Models;
using Newtonsoft.Json;

namespace Chalmers.ILL.OrderItems
{
    // Shared by LuceneOrderItemSearcher (rebuilds its index from here when it's missing) and
    // MaintenanceSurfaceController.RebuildSearchIndex (the "no way to
    // re-populate the search index from the order files" gap flagged in
    // TODO-remove-dotnet-framework.md's testdata-for-dev-environment point) - both need the same
    // "every order file under DataPath/orders/" enumeration, so it lives in one place instead of
    // drifting between two copies.
    public static class OrderFileReader
    {
        public static IEnumerable<OrderItemModel> ReadAll(string dataPath, log4net.ILog log)
        {
            var ordersDirectory = Path.Combine(dataPath, "orders");
            if (!Directory.Exists(ordersDirectory))
                yield break;

            foreach (var file in Directory.EnumerateFiles(ordersDirectory, "*.json", SearchOption.AllDirectories))
            {
                // Only {NodeId}.json files (NodeIdGenerator's own naming convention) are actual
                // order documents. orderid-index.json sits directly under ordersDirectory and also
                // matches "*.json" - deserializing it as an OrderItemModel silently produces a
                // bogus document (NodeId 0, every string property null) instead of throwing, which
                // corrupts the search index with one fake order per real DataPath. Only visible
                // once a real orders/ directory with an actual orderid-index.json exists - see
                // TODO-remove-dotnet-framework.md's testdata-for-dev-environment point.
                if (!int.TryParse(Path.GetFileNameWithoutExtension(file), out _))
                    continue;

                var item = Read(file, log);
                if (item != null)
                    yield return item;
            }
        }

        // Every order file, highest NodeId first - NodeIds only grow, so that's newest first.
        // Lists the bucket directories rather than walking the tree, and never opens a file.
        public static List<string> FilesNewestFirst(string dataPath)
        {
            var ordersDirectory = Path.Combine(dataPath, "orders");
            if (!Directory.Exists(ordersDirectory))
                return new List<string>();

            return Directory.EnumerateDirectories(ordersDirectory)
                .Where(d => int.TryParse(Path.GetFileName(d), out _))
                .SelectMany(d => Directory.EnumerateFiles(d, "*.json"))
                .Select(f => (File: f, NodeId: int.TryParse(Path.GetFileNameWithoutExtension(f), out var id) ? id : (int?)null))
                .Where(f => f.NodeId.HasValue)
                .OrderByDescending(f => f.NodeId.Value)
                .Select(f => f.File)
                .ToList();
        }

        // The files read several at a time, roughly in the files' order. On Azure Files (/home)
        // every file costs a network round trip of ~10 ms whatever its size - one at a time that's
        // 15+ minutes for 100k orders (measured on the test server 2026-11-11), so the waits have
        // to overlap. Bounded: reading outpaces indexing, and an unbounded read-ahead (PLINQ did
        // this) fills memory with orders until the app stops answering. The readers are their own
        // threads, not the thread pool's, which ASP.NET needs for requests.
        public static IEnumerable<OrderItemModel> ReadInParallel(IReadOnlyList<string> files, log4net.ILog log, CancellationToken cancellation = default, int readers = 8, int buffered = 256)
        {
            // Not disposed: the readers may still be finishing a file when the caller is done.
            var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            var queue = Channel.CreateBounded<OrderItemModel>(buffered);
            var next = -1;
            var running = readers;

            for (var r = 0; r < readers; r++)
            {
                new Thread(() =>
                {
                    try
                    {
                        int i;
                        while (!stop.IsCancellationRequested && (i = Interlocked.Increment(ref next)) < files.Count)
                        {
                            var item = Read(files[i], log);
                            if (item != null)
                                queue.Writer.WriteAsync(item, stop.Token).AsTask().GetAwaiter().GetResult();
                        }
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    finally
                    {
                        if (Interlocked.Decrement(ref running) == 0)
                            queue.Writer.TryComplete();
                    }
                }) { IsBackground = true, Name = "order-file-reader" }.Start();
            }

            try
            {
                while (queue.Reader.WaitToReadAsync(cancellation).AsTask().GetAwaiter().GetResult())
                {
                    while (queue.Reader.TryRead(out var item))
                        yield return item;
                }
            }
            finally
            {
                // Also when the caller stops early: the readers mustn't wait on a full queue forever.
                stop.Cancel();
            }
        }

        public static string OrderFilePath(string dataPath, int nodeId) =>
            Path.Combine(dataPath, "orders", (nodeId / 1000).ToString("D3"), nodeId + ".json");

        public static OrderItemModel Read(string file, log4net.ILog log)
        {
            try
            {
                return JsonConvert.DeserializeObject<OrderItemModel>(File.ReadAllText(file));
            }
            catch (Exception e)
            {
                log?.Error("Failed to load order file " + file + ".", e);
                return null;
            }
        }
    }
}
