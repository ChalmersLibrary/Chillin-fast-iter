using Chalmers.ILL.Configuration;

namespace Chalmers.ILL.Tests.Configuration
{
    // Shared across tests that construct classes taking IChillinConfiguration (fas 6 expanded it to
    // 50 members, too many to hand-roll per test file the way smaller interfaces are stubbed
    // elsewhere in this project). All properties default to null/false, matching an unconfigured
    // appsettings.json - tests override only the properties they care about via object initializers.
    public class StubChillinConfiguration : IChillinConfiguration
    {
        public bool Isolated { get; set; }
        public string DataPath { get; set; }

        public string BaseUrl { get; set; }
        public string TestServer { get; set; }
        public string LiveServer { get; set; }
        public string CronServerIpAddress { get; set; }

        public bool UseMicrosoftGraphMailService { get; set; }
        public string MicrosoftGraphApiUserId { get; set; }
        public string MicrosoftGraphApiEndpoint { get; set; }
        public string MicrosoftGraphAuthority { get; set; }
        public string MicrosoftGraphClientId { get; set; }
        public string MicrosoftGraphClientSecret { get; set; }

        public string ChalmersIllExchangeLogin { get; set; }
        public string ChalmersIllExchangePassword { get; set; }
        public string ChalmersIllSenderAddress { get; set; }
        public string ChalmersIllForwardingAddress { get; set; }
        public bool ChalmersIllArchiveProcessedMails { get; set; }
        public string ChalmersIllMailSubject { get; set; }
        public string BugFixersMailingList { get; set; }

        public string StorageConnectionString { get; set; }
        public string ElasticSearchUrl { get; set; }
        public string ElasticSearchIndex { get; set; }
        public string ElasticSearchTemplatesIndex { get; set; }

        public string LibrisApiBaseAddress { get; set; }
        public string LibrisApiUserRequestSuffix { get; set; }
        public string LibrisApiKey { get; set; }
        public string LibrarySigel { get; set; }

        public string FolioApiBaseAddress { get; set; }
        public string FolioXOkapiTenant { get; set; }
        public string FolioUsername { get; set; }
        public string FolioPassword { get; set; }
        public string FolioSourceId { get; set; }
        public string ServicePointHuvudbiblioteketId { get; set; }
        public string ServicePointLindholmenbiblioteketId { get; set; }
        public string ServicePointArkitekturbiblioteketId { get; set; }
        public string HoldingPermanentLocationId { get; set; }
        public string ChillinStatisticalCodeId { get; set; }
        public string InstanceResourceTypeId { get; set; }
        public string InstanceStatusId { get; set; }
        public string InstanceModesOfIssuance { get; set; }
        public string InstanceIdentifierTypeId { get; set; }
        public string ItemMaterialTypeId { get; set; }
        public string ItemPermanentLoanTypeId { get; set; }
        public string ItemPermanentLoanTypeIdInHouse { get; set; }

        public string PatronCacheSolrQueryUrl { get; set; }
        public string PatronAffiliationSolrQueryUrl { get; set; }
        public string PatronCacheSolrBasicAuthUsername { get; set; }
        public string PatronCacheSolrBasicAuthPassword { get; set; }

        public string LibPSearchUrl { get; set; }
        public string LibPSearchApiKey { get; set; }

        public string OrderListPageUrl { get; set; }
        public string StatusEditingAtMailSendInfo { get; set; }
        public string StatisticsUrl { get; set; }
        public bool ShowManualMailFetchingTools { get; set; }
        public string ManualAnonymizationImplementationDate { get; set; }
    }
}
