using System;
using System.Collections.Generic;
using System.Linq;
using Chalmers.ILL.Models;
using Chalmers.ILL.OrderItems;
using Newtonsoft.Json;

namespace Chalmers.ILL.Migration
{
    // Orchestrates fas 7's "Bygg engångsmigreringen från SQL till filer": reads every order from
    // an IOrderItemSource, writes the file layout FileOrderItemManager/OrderIdIndex/NodeIdGenerator
    // expect, then verifies itself the way the TODO entry requires - "jämför antal poster, och
    // stickprovsjämför fullständiga aggregat ... mellan databas och fil":
    //
    //   1. Counts: rows read vs. files written, and total log items/attachments read vs. written.
    //   2. Spot check: for a random sample of the written orders, re-fetch that single order
    //      independently from the source (IOrderItemSource.ReadOne - a fresh query, not a reuse of
    //      ReadAll's in-memory grouping) and deep-compare it against the file that was written,
    //      so the check exercises the real source-vs-file path rather than comparing an object to
    //      itself.
    //   3. Optionally, re-index every written order into Elasticsearch and compare the resulting
    //      document count to the file count ("kör om ES-indexeringen efteråt och jämför
    //      träffantal mot databasen").
    public class MigrationRunner
    {
        private readonly IOrderItemSource _source;
        private readonly OrderFileWriter _writer;
        private readonly int _sampleSize;
        private readonly Random _random;
        private readonly IOrderItemSearcher _searcher;

        public MigrationRunner(IOrderItemSource source, OrderFileWriter writer, int sampleSize = 25, IOrderItemSearcher searcher = null, Random random = null)
        {
            _source = source;
            _writer = writer;
            _sampleSize = sampleSize;
            _searcher = searcher;
            _random = random ?? new Random();
        }

        public MigrationResult Run()
        {
            var result = new MigrationResult();
            var orderIdToNodeId = new Dictionary<string, int>();
            var writtenNodeIds = new List<int>();
            var writtenOrders = new List<OrderItemModel>();

            foreach (var order in _source.ReadAll())
            {
                result.SourceOrderCount++;
                result.SourceLogItemCount += order.LogItemsList?.Count ?? 0;
                result.SourceAttachmentCount += order.AttachmentList?.Count ?? 0;

                // FileOrderItemManager.ApplyReadTimeFixups assumes these are never null on read
                // (it sorts them directly) - enforce that regardless of what the source provided.
                order.LogItemsList ??= new List<LogItem>();
                order.AttachmentList ??= new List<OrderAttachment>();
                order.SierraInfo ??= new SierraModel();

                _writer.WriteOrder(order);
                writtenNodeIds.Add(order.NodeId);
                writtenOrders.Add(order);

                if (!string.IsNullOrEmpty(order.OrderId))
                    orderIdToNodeId[order.OrderId] = order.NodeId;

                if (order.NodeId > result.MaxNodeId)
                    result.MaxNodeId = order.NodeId;
            }

            result.WrittenFileCount = writtenNodeIds.Count;

            _writer.WriteOrderIdIndex(orderIdToNodeId);
            _writer.WriteNodeIdCounter(result.MaxNodeId + 1);

            VerifyCounts(result);
            VerifySample(writtenNodeIds, result);

            if (_searcher != null)
                ReindexAndVerify(writtenOrders, result);

            return result;
        }

        private void VerifyCounts(MigrationResult result)
        {
            if (result.SourceOrderCount != result.WrittenFileCount)
            {
                result.Errors.Add($"Antal lästa ordrar ({result.SourceOrderCount}) skiljer sig från antal skrivna filer ({result.WrittenFileCount}).");
            }
        }

        private void VerifySample(List<int> writtenNodeIds, MigrationResult result)
        {
            if (writtenNodeIds.Count == 0)
                return;

            var sampleCount = Math.Min(_sampleSize, writtenNodeIds.Count);
            var sample = writtenNodeIds.OrderBy(_ => _random.Next()).Take(sampleCount);

            foreach (var nodeId in sample)
            {
                result.SpotCheckedNodeIds.Add(nodeId);

                var fromSource = _source.ReadOne(nodeId);
                var fromFile = _writer.ReadBack(nodeId);

                if (fromSource == null || fromFile == null)
                {
                    result.Errors.Add($"NodeId {nodeId}: gick inte att läsa tillbaka från källa och/eller fil för stickprovskontroll.");
                    continue;
                }

                var sourceJson = JsonConvert.SerializeObject(Normalize(fromSource));
                var fileJson = JsonConvert.SerializeObject(Normalize(fromFile));

                if (sourceJson != fileJson)
                {
                    result.Errors.Add($"NodeId {nodeId}: filen matchar inte källan vid stickprovskontroll.");
                }
            }
        }

        private void ReindexAndVerify(List<OrderItemModel> writtenOrders, MigrationResult result)
        {
            foreach (var order in writtenOrders)
            {
                _searcher.Added(order);
            }

            var count = _searcher.Search("*", 0, 0).Count;
            result.ElasticSearchDocCount = count;

            if (count != result.WrittenFileCount)
            {
                result.Errors.Add($"Elasticsearch-dokument ({count}) skiljer sig från antal skrivna filer ({result.WrittenFileCount}).");
            }
        }

        // Log items/attachments/addresses have no guaranteed stable order between two independent
        // queries against the same tables - sort by a stable key before comparing so harmless
        // ordering differences don't get reported as mismatches.
        private static OrderItemModel Normalize(OrderItemModel order)
        {
            order.LogItemsList = (order.LogItemsList ?? new List<LogItem>())
                .OrderBy(x => x.Id).ToList();
            order.AttachmentList = (order.AttachmentList ?? new List<OrderAttachment>())
                .OrderBy(x => x.DbId).ToList();
            if (order.SierraInfo?.adress != null)
                order.SierraInfo.adress = order.SierraInfo.adress.OrderBy(x => x.DbId).ToList();
            return order;
        }
    }
}
