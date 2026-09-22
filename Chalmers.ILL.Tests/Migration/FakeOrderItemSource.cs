using System;
using System.Collections.Generic;
using System.Linq;
using Chalmers.ILL.Migration;
using Chalmers.ILL.Models;

namespace Chalmers.ILL.Tests.Migration
{
    // In-memory stand-in for SqlOrderItemSource so MigrationRunner's file-writing and
    // verification logic can be tested without a real SQL Server - see
    // Chalmers.ILL.Migration/IOrderItemSource.cs.
    public class FakeOrderItemSource : IOrderItemSource
    {
        private readonly List<OrderItemModel> _orders;

        // Lets a test simulate the source and the written file disagreeing for one order, to
        // prove MigrationRunner's spot check actually catches a mismatch instead of always
        // trivially passing.
        public Func<int, OrderItemModel> ReadOneOverride { get; set; }

        public FakeOrderItemSource(List<OrderItemModel> orders)
        {
            _orders = orders;
        }

        public IEnumerable<OrderItemModel> ReadAll() => _orders;

        public OrderItemModel ReadOne(int nodeId)
        {
            if (ReadOneOverride != null)
                return ReadOneOverride(nodeId);

            return _orders.FirstOrDefault(o => o.NodeId == nodeId);
        }
    }
}
