using System;
using System.Collections.Generic;
using System.IO;
using Chalmers.ILL.Models;
using Newtonsoft.Json;

namespace Chalmers.ILL.Migration
{
    // Writes to the exact same on-disk layout FileOrderItemManager/OrderIdIndex/NodeIdGenerator
    // read (fas 7) - DataPath/orders/{NodeId/1000:D3}/{NodeId}.json, orders/orderid-index.json,
    // orders/next-node-id.txt. Duplicated rather than shared with those classes: this tool runs
    // once, from a separate assembly, and the format is small enough (bucket path + atomic
    // temp-file write) that reaching into their private helpers isn't worth the coupling.
    public class OrderFileWriter
    {
        private readonly string _ordersDirectory;

        public OrderFileWriter(string ordersDirectory)
        {
            _ordersDirectory = ordersDirectory;
        }

        public string BucketDirectory(int nodeId) => Path.Combine(_ordersDirectory, (nodeId / 1000).ToString("D3"));
        public string OrderFilePath(int nodeId) => Path.Combine(BucketDirectory(nodeId), nodeId + ".json");

        public void WriteOrder(OrderItemModel orderItem)
        {
            var directory = BucketDirectory(orderItem.NodeId);
            Directory.CreateDirectory(directory);

            var path = OrderFilePath(orderItem.NodeId);
            var json = JsonConvert.SerializeObject(orderItem, Formatting.Indented);
            AtomicWrite(path, json);
        }

        public void WriteOrderIdIndex(Dictionary<string, int> orderIdToNodeId)
        {
            Directory.CreateDirectory(_ordersDirectory);
            var path = Path.Combine(_ordersDirectory, "orderid-index.json");
            AtomicWrite(path, JsonConvert.SerializeObject(orderIdToNodeId));
        }

        public void WriteNodeIdCounter(int nextNodeId)
        {
            Directory.CreateDirectory(_ordersDirectory);
            var path = Path.Combine(_ordersDirectory, "next-node-id.txt");
            AtomicWrite(path, nextNodeId.ToString());
        }

        public OrderItemModel ReadBack(int nodeId)
        {
            var path = OrderFilePath(nodeId);
            if (!File.Exists(path))
                return null;

            return JsonConvert.DeserializeObject<OrderItemModel>(File.ReadAllText(path));
        }

        private static void AtomicWrite(string path, string content)
        {
            var tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(tempPath, content);
            if (File.Exists(path))
                File.Replace(tempPath, path, null);
            else
                File.Move(tempPath, path);
        }
    }
}
