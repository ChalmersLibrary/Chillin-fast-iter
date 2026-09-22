using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using Chalmers.ILL.Models;

namespace Chalmers.ILL.Migration
{
    // Reads the old EF6 code-first schema directly via ADO.NET - no EF6 dependency, since the
    // schema was never touched by Fluent API/[Column]/[Table] mapping (verified by reading every
    // one of the 19 migrations in Chalmers.ILL/Migrations at commit dc9b998^, the commit before
    // fas 7 deleted them: table/column names are pure EF6 code-first convention, and no
    // RenameTable/RenameColumn ever ran). That makes the schema fully recoverable from the final
    // (pre-deletion) shape of OrderItemModel/LogItem/OrderAttachment/SierraModel/SierraAddressModel
    // plus the FK/shadow-column choices EF6 made in the very first migration
    // (201606291112079_InitialDatabaseModel), which no later migration altered:
    //
    //   dbo.OrderItemModels   PK NodeId       (+ shadow FK SierraInfo_DbId -> SierraModels.DbId)
    //   dbo.LogItems          PK Id           FK NodeId -> OrderItemModels.NodeId (cascade)
    //   dbo.OrderAttachments  PK DbId         shadow FK OrderItemModel_NodeId -> OrderItemModels.NodeId
    //   dbo.SierraModels      PK DbId
    //   dbo.SierraAddressModels PK DbId       shadow FK SierraModel_DbId -> SierraModels.DbId
    //
    // IMPORTANT: point the connection string at a locally restored copy of an exported dump
    // (e.g. a bacpac imported into a throwaway SQL Server), never at the live database - see the
    // TODO entry this class implements.
    public class SqlOrderItemSource : IOrderItemSource
    {
        private readonly string _connectionString;

        public SqlOrderItemSource(string connectionString)
        {
            _connectionString = connectionString;
        }

        public IEnumerable<OrderItemModel> ReadAll()
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                connection.Open();

                var addressesByParent = ReadSierraAddresses(connection);
                var sierraModelsById = ReadSierraModels(connection, addressesByParent);
                var logItemsByOrder = ReadLogItems(connection);
                var attachmentsByOrder = ReadAttachments(connection);

                using (var command = new SqlCommand(OrderItemSelectSql + " ORDER BY NodeId", connection))
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var order = ReadOrderItem(reader);

                        order.LogItemsList = logItemsByOrder.TryGetValue(order.NodeId, out var logItems)
                            ? logItems : new List<LogItem>();
                        order.AttachmentList = attachmentsByOrder.TryGetValue(order.NodeId, out var attachments)
                            ? attachments : new List<OrderAttachment>();

                        var sierraInfoDbId = GetNullableGuid(reader, "SierraInfo_DbId");
                        order.SierraInfo = sierraInfoDbId.HasValue && sierraModelsById.TryGetValue(sierraInfoDbId.Value, out var sierraInfo)
                            ? sierraInfo : new SierraModel();

                        yield return order;
                    }
                }
            }
        }

        public OrderItemModel ReadOne(int nodeId)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                connection.Open();

                OrderItemModel order = null;
                using (var command = new SqlCommand(OrderItemSelectSql + " WHERE NodeId = @NodeId", connection))
                {
                    command.Parameters.AddWithValue("@NodeId", nodeId);
                    using (var reader = command.ExecuteReader())
                    {
                        if (!reader.Read())
                            return null;

                        order = ReadOrderItem(reader);
                    }
                }

                order.LogItemsList = new List<LogItem>();
                using (var command = new SqlCommand(
                    "SELECT Id, OrderItemNodeId, NodeId, EventId, Type, Message, MemberName, CreateDate FROM dbo.LogItems WHERE NodeId = @NodeId",
                    connection))
                {
                    command.Parameters.AddWithValue("@NodeId", nodeId);
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                            order.LogItemsList.Add(ReadLogItem(reader));
                    }
                }

                order.AttachmentList = new List<OrderAttachment>();
                using (var command = new SqlCommand(
                    "SELECT DbId, MediaItemNodeId, Title, Link FROM dbo.OrderAttachments WHERE OrderItemModel_NodeId = @NodeId",
                    connection))
                {
                    command.Parameters.AddWithValue("@NodeId", nodeId);
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                            order.AttachmentList.Add(ReadAttachment(reader));
                    }
                }

                order.SierraInfo = new SierraModel();
                using (var command = new SqlCommand(
                    SierraModelSelectSql + " WHERE DbId = (SELECT SierraInfo_DbId FROM dbo.OrderItemModels WHERE NodeId = @NodeId)",
                    connection))
                {
                    command.Parameters.AddWithValue("@NodeId", nodeId);
                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            var sierraModel = ReadSierraModel(reader);
                            var dbId = sierraModel.DbId;

                            using (var addrCommand = new SqlCommand(
                                SierraAddressSelectSql + " WHERE SierraModel_DbId = @DbId", connection))
                            {
                                addrCommand.Parameters.AddWithValue("@DbId", dbId);
                                using (var addrReader = addrCommand.ExecuteReader())
                                {
                                    while (addrReader.Read())
                                        sierraModel.adress.Add(ReadSierraAddress(addrReader));
                                }
                            }

                            order.SierraInfo = sierraModel;
                        }
                    }
                }

                return order;
            }
        }

        #region Bulk reads (grouped in memory, used by ReadAll)

        private Dictionary<Guid, List<SierraAddressModel>> ReadSierraAddresses(SqlConnection connection)
        {
            var result = new Dictionary<Guid, List<SierraAddressModel>>();
            using (var command = new SqlCommand(SierraAddressBulkSelectSql, connection))
            using (var reader = command.ExecuteReader())
            {
                var parentOrdinal = reader.GetOrdinal("SierraModel_DbId");
                while (reader.Read())
                {
                    if (reader.IsDBNull(parentOrdinal))
                        continue;

                    var parentId = reader.GetGuid(parentOrdinal);
                    if (!result.TryGetValue(parentId, out var list))
                    {
                        list = new List<SierraAddressModel>();
                        result[parentId] = list;
                    }
                    list.Add(ReadSierraAddress(reader));
                }
            }
            return result;
        }

        private Dictionary<Guid, SierraModel> ReadSierraModels(SqlConnection connection, Dictionary<Guid, List<SierraAddressModel>> addressesByParent)
        {
            var result = new Dictionary<Guid, SierraModel>();
            using (var command = new SqlCommand(SierraModelSelectSql, connection))
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    var sierraModel = ReadSierraModel(reader);
                    if (addressesByParent.TryGetValue(sierraModel.DbId, out var addresses))
                        sierraModel.adress = addresses;
                    result[sierraModel.DbId] = sierraModel;
                }
            }
            return result;
        }

        private Dictionary<int, List<LogItem>> ReadLogItems(SqlConnection connection)
        {
            var result = new Dictionary<int, List<LogItem>>();
            using (var command = new SqlCommand(
                "SELECT Id, OrderItemNodeId, NodeId, EventId, Type, Message, MemberName, CreateDate FROM dbo.LogItems", connection))
            using (var reader = command.ExecuteReader())
            {
                var parentOrdinal = reader.GetOrdinal("NodeId");
                while (reader.Read())
                {
                    var parentId = reader.GetInt32(parentOrdinal);
                    if (!result.TryGetValue(parentId, out var list))
                    {
                        list = new List<LogItem>();
                        result[parentId] = list;
                    }
                    list.Add(ReadLogItem(reader));
                }
            }
            return result;
        }

        private Dictionary<int, List<OrderAttachment>> ReadAttachments(SqlConnection connection)
        {
            var result = new Dictionary<int, List<OrderAttachment>>();
            using (var command = new SqlCommand(
                "SELECT DbId, MediaItemNodeId, Title, Link, OrderItemModel_NodeId FROM dbo.OrderAttachments", connection))
            using (var reader = command.ExecuteReader())
            {
                var parentOrdinal = reader.GetOrdinal("OrderItemModel_NodeId");
                while (reader.Read())
                {
                    if (reader.IsDBNull(parentOrdinal))
                        continue;

                    var parentId = reader.GetInt32(parentOrdinal);
                    if (!result.TryGetValue(parentId, out var list))
                    {
                        list = new List<OrderAttachment>();
                        result[parentId] = list;
                    }
                    list.Add(ReadAttachment(reader));
                }
            }
            return result;
        }

        #endregion

        #region Row mapping

        private const string OrderItemSelectSql = @"
SELECT NodeId, OrderId, CreateDate, UpdateDate, FollowUpDate, FollowUpDateIsDue, ContentVersionsCount,
       OriginalOrder, Reference, TitleInformation,
       TypeId, Type, StatusId, Status, StatusString,
       PreviousStatusId, PreviousStatus, PreviousStatusString,
       LastDeliveryStatusId, LastDeliveryStatus, LastDeliveryStatusString,
       DeliveryLibraryId, DeliveryLibrary, CancellationReasonId, CancellationReason,
       PurchasedMaterialId, PurchasedMaterial, PurchaseLibrary,
       PatronName, PatronEmail, PatronCardNo, PatronAffiliation,
       ProviderName, ProviderOrderId, ProviderInformation, ProviderDueDate,
       EditedBy, EditedByMemberName, EditedByCurrentMember,
       Log, Attachments, SierraInfoStr, DrmWarning, DeliveryLibrarySameAsHomeLibrary,
       DueDate, DeliveryDate, BookId, ReadOnlyAtLibrary, SeedId,
       IsAnonymizedAutomatically, IsAnonymized, SierraInfo_DbId
FROM dbo.OrderItemModels";

        private const string SierraModelSelectSql = @"
SELECT DbId, id, barcode, pnum, ptype, email, first_name, last_name, mblock, home_library,
       expdate, home_library_pretty_name, record_id, aff, active, e_resource_access, cid
FROM dbo.SierraModels";

        private const string SierraAddressSelectSql = @"
SELECT DbId, addresscount, addr1, addr2, addr3, village, city, region, postal_code, country
FROM dbo.SierraAddressModels";

        // Same columns as SierraAddressSelectSql plus the shadow FK, used only by the bulk
        // (ReadAll) path to group addresses by parent SierraModel in memory.
        private const string SierraAddressBulkSelectSql = @"
SELECT DbId, addresscount, addr1, addr2, addr3, village, city, region, postal_code, country, SierraModel_DbId
FROM dbo.SierraAddressModels";

        private static OrderItemModel ReadOrderItem(SqlDataReader reader)
        {
            return new OrderItemModel
            {
                NodeId = (int)reader["NodeId"],
                OrderId = GetString(reader, "OrderId"),
                CreateDate = (DateTime)reader["CreateDate"],
                UpdateDate = (DateTime)reader["UpdateDate"],
                FollowUpDate = (DateTime)reader["FollowUpDate"],
                FollowUpDateIsDue = (bool)reader["FollowUpDateIsDue"],
                ContentVersionsCount = (int)reader["ContentVersionsCount"],
                OriginalOrder = GetString(reader, "OriginalOrder"),
                Reference = GetString(reader, "Reference"),
                TitleInformation = GetString(reader, "TitleInformation"),
                TypeId = (int)reader["TypeId"],
                Type = GetString(reader, "Type"),
                StatusId = (int)reader["StatusId"],
                Status = GetString(reader, "Status"),
                StatusString = GetString(reader, "StatusString"),
                PreviousStatusId = (int)reader["PreviousStatusId"],
                PreviousStatus = GetString(reader, "PreviousStatus"),
                PreviousStatusString = GetString(reader, "PreviousStatusString"),
                LastDeliveryStatusId = (int)reader["LastDeliveryStatusId"],
                LastDeliveryStatus = GetString(reader, "LastDeliveryStatus"),
                LastDeliveryStatusString = GetString(reader, "LastDeliveryStatusString"),
                DeliveryLibraryId = (int)reader["DeliveryLibraryId"],
                DeliveryLibrary = GetString(reader, "DeliveryLibrary"),
                CancellationReasonId = (int)reader["CancellationReasonId"],
                CancellationReason = GetString(reader, "CancellationReason"),
                PurchasedMaterialId = (int)reader["PurchasedMaterialId"],
                PurchasedMaterial = GetString(reader, "PurchasedMaterial"),
                PurchaseLibrary = (OrderItemModel.PurchaseLibraries)(int)reader["PurchaseLibrary"],
                PatronName = GetString(reader, "PatronName"),
                PatronEmail = GetString(reader, "PatronEmail"),
                PatronCardNo = GetString(reader, "PatronCardNo"),
                PatronAffiliation = GetString(reader, "PatronAffiliation"),
                ProviderName = GetString(reader, "ProviderName"),
                ProviderOrderId = GetString(reader, "ProviderOrderId"),
                ProviderInformation = GetString(reader, "ProviderInformation"),
                ProviderDueDate = (DateTime)reader["ProviderDueDate"],
                EditedBy = GetString(reader, "EditedBy"),
                EditedByMemberName = GetString(reader, "EditedByMemberName"),
                EditedByCurrentMember = (bool)reader["EditedByCurrentMember"],
                Log = GetString(reader, "Log"),
                Attachments = GetString(reader, "Attachments"),
                SierraInfoStr = GetString(reader, "SierraInfoStr"),
                DrmWarning = GetString(reader, "DrmWarning"),
                DeliveryLibrarySameAsHomeLibrary = (bool)reader["DeliveryLibrarySameAsHomeLibrary"],
                DueDate = (DateTime)reader["DueDate"],
                DeliveryDate = (DateTime)reader["DeliveryDate"],
                BookId = GetString(reader, "BookId"),
                ReadOnlyAtLibrary = (bool)reader["ReadOnlyAtLibrary"],
                SeedId = GetString(reader, "SeedId"),
                IsAnonymizedAutomatically = (bool)reader["IsAnonymizedAutomatically"],
                IsAnonymized = (bool)reader["IsAnonymized"],
            };
        }

        private static LogItem ReadLogItem(SqlDataReader reader)
        {
            return new LogItem
            {
                Id = (Guid)reader["Id"],
                OrderItemNodeId = (int)reader["OrderItemNodeId"],
                NodeId = (int)reader["NodeId"],
                EventId = GetString(reader, "EventId"),
                Type = GetString(reader, "Type"),
                Message = GetString(reader, "Message"),
                MemberName = GetString(reader, "MemberName"),
                CreateDate = (DateTime)reader["CreateDate"],
            };
        }

        private static OrderAttachment ReadAttachment(SqlDataReader reader)
        {
            return new OrderAttachment
            {
                DbId = (Guid)reader["DbId"],
                MediaItemNodeId = GetString(reader, "MediaItemNodeId"),
                Title = GetString(reader, "Title"),
                Link = GetString(reader, "Link"),
            };
        }

        private static SierraModel ReadSierraModel(SqlDataReader reader)
        {
            return new SierraModel
            {
                DbId = (Guid)reader["DbId"],
                id = GetString(reader, "id"),
                barcode = GetString(reader, "barcode"),
                pnum = GetString(reader, "pnum"),
                ptype = (int)reader["ptype"],
                email = GetString(reader, "email"),
                first_name = GetString(reader, "first_name"),
                last_name = GetString(reader, "last_name"),
                mblock = GetString(reader, "mblock"),
                home_library = GetString(reader, "home_library"),
                expdate = GetString(reader, "expdate"),
                home_library_pretty_name = GetString(reader, "home_library_pretty_name"),
                record_id = (int)reader["record_id"],
                aff = GetString(reader, "aff"),
                adress = new List<SierraAddressModel>(),
                active = GetNullableBool(reader, "active"),
                e_resource_access = GetNullableBool(reader, "e_resource_access"),
                cid = GetString(reader, "cid"),
            };
        }

        private static SierraAddressModel ReadSierraAddress(SqlDataReader reader)
        {
            return new SierraAddressModel
            {
                DbId = (Guid)reader["DbId"],
                addresscount = GetString(reader, "addresscount"),
                addr1 = GetString(reader, "addr1"),
                addr2 = GetString(reader, "addr2"),
                addr3 = GetString(reader, "addr3"),
                village = GetString(reader, "village"),
                city = GetString(reader, "city"),
                region = GetString(reader, "region"),
                postal_code = GetString(reader, "postal_code"),
                country = GetString(reader, "country"),
            };
        }

        private static string GetString(SqlDataReader reader, string column)
        {
            var value = reader[column];
            return value == DBNull.Value ? null : (string)value;
        }

        private static bool? GetNullableBool(SqlDataReader reader, string column)
        {
            var value = reader[column];
            return value == DBNull.Value ? (bool?)null : (bool)value;
        }

        private static Guid? GetNullableGuid(SqlDataReader reader, string column)
        {
            var value = reader[column];
            return value == DBNull.Value ? (Guid?)null : (Guid)value;
        }

        #endregion
    }
}
