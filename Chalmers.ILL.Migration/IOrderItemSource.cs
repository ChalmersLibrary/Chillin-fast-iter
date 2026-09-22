using System.Collections.Generic;
using Chalmers.ILL.Models;

namespace Chalmers.ILL.Migration
{
    // Abstracts where order aggregates come from, so MigrationRunner's file-writing and
    // verification logic (Chalmers.ILL.Tests/Migration) can be exercised without a real SQL
    // Server - the only implementation that talks to SQL is SqlOrderItemSource.
    public interface IOrderItemSource
    {
        // Full aggregate for every order, in NodeId order. LogItemsList/AttachmentList must never
        // be null (FileOrderItemManager.ApplyReadTimeFixups assumes non-null lists on read), and
        // SierraInfo must never be null (matches OrderItemModel's own constructor default).
        IEnumerable<OrderItemModel> ReadAll();

        // Re-fetches a single order independently of ReadAll's bulk grouping, for
        // MigrationRunner's spot-check verification against the written file. Returns null if the
        // order no longer exists.
        OrderItemModel ReadOne(int nodeId);
    }
}
