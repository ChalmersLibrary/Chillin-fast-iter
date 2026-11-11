using Chalmers.ILL.Models;
using System.Collections.Generic;

namespace Chalmers.ILL.OrderItems
{
    public interface IOrderItemSearcher
    {
        IEnumerable<OrderItemModel> Search(string query);
        SearchResult Search(string query, int start, int size);
        IEnumerable<OrderItemModel> Search(string query, int size, string[] fields);
        IEnumerable<string> AggregatedProviders();

        void Added(OrderItemModel item);
        void Modified(OrderItemModel item);
        void Deleted(OrderItemModel item);

        // Replaces the whole index with these orders; returns how many were indexed.
        int Rebuild(IEnumerable<OrderItemModel> items);

        // Whether the index is still being built from the order files in the background - until
        // then searches only see part of the orders, newest first.
        SearchIndexStatus Status => SearchIndexStatus.Ready;
    }

    public class SearchIndexStatus
    {
        public static readonly SearchIndexStatus Ready = new SearchIndexStatus();

        public bool Rebuilding { get; init; }
        public bool Failed { get; init; }
        public int Indexed { get; init; }
        public int Total { get; init; }
    }
}
