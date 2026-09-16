using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Chalmers.ILL.Models
{
    // Main Order Item
    public class OrderItemModel
    {
        public const string LIBRARY_Z_PRETTY_STRING = "Huvudbiblioteket";
        public const string LIBRARY_ZL_PRETTY_STRING = "Kuggen";
        public const string LIBRARY_ZA_PRETTY_STRING = "Arkitekturbiblioteket";
        public const string LIBRARY_UNKNOWN_PRETTY_STRING = "Okänt bibliotek";

        public const string LIBRARY_Z_UMBRACO_STRING = "Huvudbiblioteket";
        public const string LIBRARY_ZL_UMBRACO_STRING = "Lindholmenbiblioteket";
        public const string LIBRARY_ZA_UMBRACO_STRING = "Arkitekturbiblioteket";

        public enum PurchaseLibraries { HB, ACE }

        public OrderItemModel()
        {
            StatusId = -1;
            PreviousStatusId = -1;
            LastDeliveryStatusId = -1;
            TypeId = -1;
            Type = "";
            DeliveryLibraryId = -1;
            DeliveryLibrary = "";
            CancellationReasonId = -1;
            CancellationReason = "";
            PurchasedMaterialId = -1;
            PurchasedMaterial = "";
            SierraInfo = new SierraModel();
            PatronAffiliation = "Ej hämtad";
        }

        public string OrderId { get; set; }

        // Was a SQL Server identity column - now allocated by NodeIdGenerator (fas 7). Still an
        // int and still never reused: it's baked into URLs and into already-printed QR codes on
        // physical delivery slips.
        public int NodeId { get; set; }
        public DateTime CreateDate { get; set; }
        public DateTime UpdateDate { get; set; }
        public DateTime FollowUpDate { get; set; }
        public bool FollowUpDateIsDue { get; set; }

        public int ContentVersionsCount { get; set; }

        public string OriginalOrder { get; set; }
        public string Reference { get; set; }

        public string TitleInformation { get; set; }

        [JsonIgnore]
        public string OriginalOrderWithoutReferenceUrl
        {
            get
            {
                var refUrlRegex = new Regex(@"Referens \(radera ej\): https?://[A-Za-z0-9\-\._~:/\?#\[\]@!\$&'\(\)\*\+,;=`%]+.*");
                return refUrlRegex.Replace(OriginalOrder, "");
            }
        }

        public int TypeId { get; set; }
        public string Type { get; set; }

        public int StatusId { get; set; }
        public string Status { get; set; }
        public string StatusString { get; set; }

        public int PreviousStatusId { get; set; }
        public string PreviousStatus { get; set; }
        public string PreviousStatusString { get; set; }

        public int LastDeliveryStatusId { get; set; }
        public string LastDeliveryStatus { get; set; }
        public string LastDeliveryStatusString { get; set; }

        public int DeliveryLibraryId { get; set; }
        public string DeliveryLibrary { get; set; }

        [JsonIgnore]
        public string DeliveryLibraryPrettyName
        {
            get
            {
                if (DeliveryLibrary == LIBRARY_Z_UMBRACO_STRING)
                {
                    return LIBRARY_Z_PRETTY_STRING;
                }
                else if (DeliveryLibrary == LIBRARY_ZL_UMBRACO_STRING)
                {
                    return LIBRARY_ZL_PRETTY_STRING;
                }
                else if (DeliveryLibrary == LIBRARY_ZA_UMBRACO_STRING)
                {
                    return LIBRARY_ZA_PRETTY_STRING;
                }
                return LIBRARY_UNKNOWN_PRETTY_STRING;
            }
        }

        public int CancellationReasonId { get; set; }
        public string CancellationReason { get; set; }

        public int PurchasedMaterialId { get; set; }
        public string PurchasedMaterial { get; set; }
        public PurchaseLibraries PurchaseLibrary { get; set; }

        public string PatronName { get; set; }
        public string PatronEmail { get; set; }
        public string PatronCardNo { get; set; }
        public string PatronAffiliation { get; set; }

        public string ProviderName { get; set; }
        public string ProviderOrderId { get; set; }
        public string ProviderInformation { get; set; }
        public DateTime ProviderDueDate { get; set; }

        public string EditedBy { get; set; }
        public string EditedByMemberName { get; set; }
        public bool EditedByCurrentMember { get; set; }

        public string Log { get; set; }
        public string Attachments { get; set; }
        public string SierraInfoStr { get; set; }

        public string DrmWarning { get; set; }
        public bool DeliveryLibrarySameAsHomeLibrary { get; set; }

        public DateTime DueDate { get; set; }
        public DateTime DeliveryDate { get; set; }
        public string BookId { get; set; }
        public bool ReadOnlyAtLibrary { get; set; }

        public List<LogItem> LogItemsList { get; set; }
        public List<OrderAttachment> AttachmentList { get; set; }
        public SierraModel SierraInfo { get; set; }

        public string SeedId { get; set; }
        public bool IsAnonymizedAutomatically { get; set; }      // Indicates if the order has been anonymized automatically
        public bool IsAnonymized { get; set; }                   // Indicates if the order has been fully anonymized, both automatically and manually.
    }
}