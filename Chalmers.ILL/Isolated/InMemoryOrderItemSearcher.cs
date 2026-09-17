using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Chalmers.ILL.Configuration;
using Chalmers.ILL.Isolated.Search;
using Chalmers.ILL.Models;
using Chalmers.ILL.OrderItems;

namespace Chalmers.ILL.Isolated
{
    // Isolerat läge steg B, "Bygg den minimala sökersättaren" - replaces steg A's
    // NullOrderItemSearcher (an empty order list was never steg B's goal, just the cheapest thing
    // that let the app boot without Elasticsearch).
    //
    // Reads every order file directly off DataPath/orders/ (the same layout FileOrderItemManager
    // writes - see its BucketDirectory/OrderFilePath) at construction, then keeps the whole set in
    // memory and updated via Added/Modified/Deleted. A full linear scan per query is fine at the
    // isolated test server's data volumes (see the TODO); there is no index to fall out of sync.
    //
    // Registered as a *singleton* (unlike the other isolated-mode seams, which are stateless
    // wrappers over the filesystem and fine as transients) - a new instance per request would
    // reload from disk every time and never see another instance's Added/Modified/Deleted calls.
    //
    // Query evaluation and the "which fields does free text search" decision live in
    // Isolated/Search/ - see OrderItemQueryParser for the supported grammar and its documented
    // divergences from real Elasticsearch behaviour.
    public class InMemoryOrderItemSearcher : IOrderItemSearcher
    {
        private static readonly log4net.ILog _log = log4net.LogManager.GetLogger(typeof(InMemoryOrderItemSearcher));

        private readonly ConcurrentDictionary<int, OrderItemDocument> _byNodeId = new ConcurrentDictionary<int, OrderItemDocument>();

        public InMemoryOrderItemSearcher(IChillinConfiguration config)
        {
            foreach (var item in OrderFileReader.ReadAll(config.DataPath, _log))
            {
                _byNodeId[item.NodeId] = new OrderItemDocument(item);
            }
        }

        public void Added(OrderItemModel item) => _byNodeId[item.NodeId] = new OrderItemDocument(item);

        public void Modified(OrderItemModel item) => _byNodeId[item.NodeId] = new OrderItemDocument(item);

        public void Deleted(OrderItemModel item) => _byNodeId.TryRemove(item.NodeId, out _);

        public IEnumerable<OrderItemModel> Search(string query) => SimpleSearch(query, 0, 10000).Items;

        public SearchResult Search(string query, int start, int size) => SimpleSearch(query, start, size);

        public IEnumerable<OrderItemModel> Search(string query, int size, string[] fields)
        {
            // The `fields` parameter mirrors ElasticSearchOrderItemSearcher's Source-projection
            // (a performance optimisation for a network round-trip) - meaningless for an in-memory
            // scan, so it's not applied here; callers only ever read a couple of properties off the
            // returned model regardless.
            var node = OrderItemQueryParser.Parse(query);
            return MatchingDocumentsSortedByCreateDateDescending(node).Take(size).Select(d => d.Model);
        }

        public IEnumerable<string> AggregatedProviders()
        {
            var res = new List<string> { "TIB", "Libris", "Subito" };

            var filter = OrderItemQueryParser.Parse(
                "NOT status:(Ny OR Annullerad OR Inköpt OR Överförd) AND NOT providerName:(\"libris\" OR \"subito\" OR \"tib\")");

            var aggregated = _byNodeId.Values
                .Where(filter.Matches)
                .Select(d => d.Model.ProviderName)
                .Where(name => !string.IsNullOrEmpty(name))
                .GroupBy(name => name)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key);

            res.AddRange(aggregated);
            return res;
        }

        private SearchResult SimpleSearch(string query, int start, int size)
        {
            var node = OrderItemQueryParser.Parse(query);
            var matches = MatchingDocumentsSortedByCreateDateDescending(node).ToList();

            return new SearchResult
            {
                Count = matches.Count,
                Items = matches.Skip(start).Take(size).Select(d => d.Model)
            };
        }

        private IEnumerable<OrderItemDocument> MatchingDocumentsSortedByCreateDateDescending(IOrderItemQueryNode node) =>
            _byNodeId.Values.Where(node.Matches).OrderByDescending(d => d.Model.CreateDate);
    }
}
