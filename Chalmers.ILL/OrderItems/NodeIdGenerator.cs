using System;
using System.IO;
using System.Linq;
using Chalmers.ILL.Configuration;

namespace Chalmers.ILL.OrderItems
{
    // NodeId used to be a SQL Server identity column. It's baked into URLs and into physical,
    // already-printed QR codes on delivery slips (see RouteConfig's LegacyUmbracoSurfaceAlias), so
    // it must stay an int and can never be reused or collide - fas 7, "Lös NodeId-genereringen".
    // A single persisted counter file is enough because the app is single-instance (fastställda
    // designbeslut, "Skalning").
    public class NodeIdGenerator
    {
        private readonly string _counterFilePath;
        private readonly string _ordersDirectory;
        private readonly object _lock = new object();
        private int? _next;

        public NodeIdGenerator(IChillinConfiguration config)
        {
            _ordersDirectory = Path.Combine(config.DataPath, "orders");
            _counterFilePath = Path.Combine(_ordersDirectory, "next-node-id.txt");
        }

        public int Next()
        {
            lock (_lock)
            {
                if (_next == null)
                {
                    _next = LoadOrBootstrap();
                }

                var allocated = _next.Value;
                _next = allocated + 1;
                Persist(_next.Value);
                return allocated;
            }
        }

        private int LoadOrBootstrap()
        {
            if (File.Exists(_counterFilePath))
            {
                return int.Parse(File.ReadAllText(_counterFilePath).Trim());
            }

            // No counter file yet - either a fresh DataPath (dev/isolated) or a migration that
            // hasn't seeded it. "Sätt startvärdet till högsta befintliga NodeId + 1 vid
            // migreringen" (fas 7) - the migration tool is expected to write this file explicitly
            // for real volumes; scanning here is just a safe, cheap fallback for a small/empty
            // orders directory.
            return ScanHighestExistingNodeId() + 1;
        }

        private int ScanHighestExistingNodeId()
        {
            if (!Directory.Exists(_ordersDirectory))
                return 0;

            return Directory.EnumerateDirectories(_ordersDirectory)
                .SelectMany(bucket => Directory.EnumerateFiles(bucket, "*.json"))
                .Select(f => int.TryParse(Path.GetFileNameWithoutExtension(f), out var id) ? id : 0)
                .DefaultIfEmpty(0)
                .Max();
        }

        private void Persist(int next)
        {
            Directory.CreateDirectory(_ordersDirectory);

            var tempPath = _counterFilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(tempPath, next.ToString());
            if (File.Exists(_counterFilePath))
                File.Replace(tempPath, _counterFilePath, null);
            else
                File.Move(tempPath, _counterFilePath);
        }
    }
}
