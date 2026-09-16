using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chalmers.ILL.Configuration;
using Chalmers.ILL.Models;
using Newtonsoft.Json;

namespace Chalmers.ILL.OrderItems
{
    // OrderId -> NodeId lookup for FileOrderItemManager.GetOrderItem(string orderId) (fas 7).
    // OrderId is immutable once an order is created (nothing ever changes it afterwards), so this
    // index only grows - written once per new order, in Register(), never touched on updates.
    // A plain JSON file rather than a search against Elasticsearch: it works even when
    // Elasticsearch is down/faked (isolerat läge), and avoids relying on Lucene query-string
    // escaping for a value that always contains hyphens.
    public class OrderIdIndex
    {
        private readonly string _indexFilePath;
        private readonly string _ordersDirectory;
        private readonly object _lock = new object();
        private Dictionary<string, int> _map;

        public OrderIdIndex(IChillinConfiguration config)
        {
            _ordersDirectory = Path.Combine(config.DataPath, "orders");
            _indexFilePath = Path.Combine(_ordersDirectory, "orderid-index.json");
        }

        public int? Lookup(string orderId)
        {
            lock (_lock)
            {
                EnsureLoaded();
                return _map.TryGetValue(orderId, out var nodeId) ? nodeId : (int?)null;
            }
        }

        public void Register(string orderId, int nodeId)
        {
            lock (_lock)
            {
                EnsureLoaded();
                _map[orderId] = nodeId;
                Persist();
            }
        }

        private void EnsureLoaded()
        {
            if (_map != null)
                return;

            if (File.Exists(_indexFilePath))
            {
                _map = JsonConvert.DeserializeObject<Dictionary<string, int>>(File.ReadAllText(_indexFilePath))
                    ?? new Dictionary<string, int>();
                return;
            }

            // No index yet - rebuild from whatever order files already exist (fresh DataPath, or a
            // migration that didn't seed it explicitly). Cheap for dev/isolated volumes; the
            // migration tool is expected to write the index directly for real volumes.
            _map = new Dictionary<string, int>();
            if (!Directory.Exists(_ordersDirectory))
                return;

            foreach (var file in Directory.EnumerateDirectories(_ordersDirectory).SelectMany(d => Directory.EnumerateFiles(d, "*.json")))
            {
                try
                {
                    var item = JsonConvert.DeserializeObject<OrderItemModel>(File.ReadAllText(file));
                    if (item != null && !string.IsNullOrEmpty(item.OrderId))
                    {
                        _map[item.OrderId] = item.NodeId;
                    }
                }
                catch
                {
                    // Skip unreadable files during a best-effort rebuild - not this index's job to
                    // surface storage corruption.
                }
            }
            Persist();
        }

        private void Persist()
        {
            Directory.CreateDirectory(_ordersDirectory);

            var json = JsonConvert.SerializeObject(_map);
            var tempPath = _indexFilePath + "." + System.Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(tempPath, json);
            if (File.Exists(_indexFilePath))
                File.Replace(tempPath, _indexFilePath, null);
            else
                File.Move(tempPath, _indexFilePath);
        }
    }
}
