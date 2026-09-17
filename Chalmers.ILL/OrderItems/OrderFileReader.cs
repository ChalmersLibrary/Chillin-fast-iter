using System;
using System.Collections.Generic;
using System.IO;
using Chalmers.ILL.Models;
using Newtonsoft.Json;

namespace Chalmers.ILL.OrderItems
{
    // Shared by Isolated.InMemoryOrderItemSearcher (loads its whole in-memory index from here at
    // construction) and MaintenanceSurfaceController.RebuildSearchIndex (the "no way to
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
                OrderItemModel item = null;
                try
                {
                    item = JsonConvert.DeserializeObject<OrderItemModel>(File.ReadAllText(file));
                }
                catch (Exception e)
                {
                    log?.Error("Failed to load order file " + file + ".", e);
                }

                if (item != null)
                    yield return item;
            }
        }
    }
}
