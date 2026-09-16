using System.Collections.Generic;
using System.Linq;
using Chalmers.ILL.Models;
using Chalmers.ILL.OrderItems;

namespace Chalmers.ILL.Isolated
{
    // NOT steg B's real search replacement (see TODO-remove-dotnet-framework.md, "Isolerat läge,
    // steg B" - fas 7 has since landed, but steg B itself is still open). Exists only so
    // Bootstrapper.RegisterTypes never has to construct a real ElasticClient in isolated mode -
    // FileOrderItemManager and friends still need *some* IOrderItemSearcher at Bootstrap time.
    // The order list is empty and AggregatedProviders() yields nothing in isolated mode until
    // steg B lands; Added/Modified/Deleted are no-ops.
    public class NullOrderItemSearcher : IOrderItemSearcher
    {
        public IEnumerable<OrderItemModel> Search(string query) => Enumerable.Empty<OrderItemModel>();

        public SearchResult Search(string query, int start, int size) => new SearchResult
        {
            Count = 0,
            Items = Enumerable.Empty<OrderItemModel>()
        };

        public IEnumerable<OrderItemModel> Search(string query, int size, string[] fields) => Enumerable.Empty<OrderItemModel>();

        public IEnumerable<string> AggregatedProviders() => Enumerable.Empty<string>();

        public void Added(OrderItemModel item) { }

        public void Modified(OrderItemModel item) { }

        public void Deleted(OrderItemModel item) { }
    }
}
