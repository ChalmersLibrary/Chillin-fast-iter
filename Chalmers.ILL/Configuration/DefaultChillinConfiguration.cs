using Microsoft.Extensions.Configuration;

namespace Chalmers.ILL.Configuration
{
    // Backed by Microsoft.Extensions.Configuration (appsettings.json + environment variables) since
    // fas 6, not System.Configuration.ConfigurationManager. All values live under the "Chillin"
    // section, with JSON keys matching these property names exactly - see appsettings.json.
    public class DefaultChillinConfiguration : IChillinConfiguration
    {
        private readonly IConfiguration _configuration;

        public DefaultChillinConfiguration(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        private string Get(string key) => _configuration["Chillin:" + key];
        private bool GetBool(string key) => _configuration.GetValue<bool>("Chillin:" + key);

        public string BaseUrl => Get(nameof(BaseUrl));
        public string TestServer => Get(nameof(TestServer));
        public string LiveServer => Get(nameof(LiveServer));
        public string CronServerIpAddress => Get(nameof(CronServerIpAddress));

        public bool UseMicrosoftGraphMailService => GetBool(nameof(UseMicrosoftGraphMailService));
        public string MicrosoftGraphApiUserId => Get(nameof(MicrosoftGraphApiUserId));
        public string MicrosoftGraphApiEndpoint => Get(nameof(MicrosoftGraphApiEndpoint));
        public string MicrosoftGraphAuthority => Get(nameof(MicrosoftGraphAuthority));
        public string MicrosoftGraphClientId => Get(nameof(MicrosoftGraphClientId));
        public string MicrosoftGraphClientSecret => Get(nameof(MicrosoftGraphClientSecret));

        public string ChalmersIllExchangeLogin => Get(nameof(ChalmersIllExchangeLogin));
        public string ChalmersIllExchangePassword => Get(nameof(ChalmersIllExchangePassword));
        public string ChalmersIllSenderAddress => Get(nameof(ChalmersIllSenderAddress));
        public string ChalmersIllForwardingAddress => Get(nameof(ChalmersIllForwardingAddress));
        public bool ChalmersIllArchiveProcessedMails => GetBool(nameof(ChalmersIllArchiveProcessedMails));
        public string ChalmersIllMailSubject => Get(nameof(ChalmersIllMailSubject));
        public string BugFixersMailingList => Get(nameof(BugFixersMailingList));

        public string StorageConnectionString => Get(nameof(StorageConnectionString));
        public string ElasticSearchUrl => Get(nameof(ElasticSearchUrl));
        public string ElasticSearchIndex => Get(nameof(ElasticSearchIndex));
        public string ElasticSearchTemplatesIndex => Get(nameof(ElasticSearchTemplatesIndex));

        public string LibrisApiBaseAddress => Get(nameof(LibrisApiBaseAddress));
        public string LibrisApiUserRequestSuffix => Get(nameof(LibrisApiUserRequestSuffix));
        public string LibrisApiKey => Get(nameof(LibrisApiKey));
        public string LibrarySigel => Get(nameof(LibrarySigel));

        public string FolioApiBaseAddress => Get(nameof(FolioApiBaseAddress));
        public string FolioXOkapiTenant => Get(nameof(FolioXOkapiTenant));
        public string FolioUsername => Get(nameof(FolioUsername));
        public string FolioPassword => Get(nameof(FolioPassword));
        public string FolioSourceId => Get(nameof(FolioSourceId));
        public string ServicePointHuvudbiblioteketId => Get(nameof(ServicePointHuvudbiblioteketId));
        public string ServicePointLindholmenbiblioteketId => Get(nameof(ServicePointLindholmenbiblioteketId));
        public string ServicePointArkitekturbiblioteketId => Get(nameof(ServicePointArkitekturbiblioteketId));
        public string HoldingPermanentLocationId => Get(nameof(HoldingPermanentLocationId));
        public string ChillinStatisticalCodeId => Get(nameof(ChillinStatisticalCodeId));
        public string InstanceResourceTypeId => Get(nameof(InstanceResourceTypeId));
        public string InstanceStatusId => Get(nameof(InstanceStatusId));
        public string InstanceModesOfIssuance => Get(nameof(InstanceModesOfIssuance));
        public string InstanceIdentifierTypeId => Get(nameof(InstanceIdentifierTypeId));
        public string ItemMaterialTypeId => Get(nameof(ItemMaterialTypeId));
        public string ItemPermanentLoanTypeId => Get(nameof(ItemPermanentLoanTypeId));
        public string ItemPermanentLoanTypeIdInHouse => Get(nameof(ItemPermanentLoanTypeIdInHouse));

        public string PatronCacheSolrQueryUrl => Get(nameof(PatronCacheSolrQueryUrl));
        public string PatronAffiliationSolrQueryUrl => Get(nameof(PatronAffiliationSolrQueryUrl));
        public string PatronCacheSolrBasicAuthUsername => Get(nameof(PatronCacheSolrBasicAuthUsername));
        public string PatronCacheSolrBasicAuthPassword => Get(nameof(PatronCacheSolrBasicAuthPassword));

        public string LibPSearchUrl => Get(nameof(LibPSearchUrl));
        public string LibPSearchApiKey => Get(nameof(LibPSearchApiKey));

        public string OrderListPageUrl => Get(nameof(OrderListPageUrl));
        public string StatusEditingAtMailSendInfo => Get(nameof(StatusEditingAtMailSendInfo));
        public string StatisticsUrl => Get(nameof(StatisticsUrl));
        public bool ShowManualMailFetchingTools => GetBool(nameof(ShowManualMailFetchingTools));
        public string ManualAnonymizationImplementationDate => Get(nameof(ManualAnonymizationImplementationDate));
        public bool CheckForPendingDatabaseMigrations => GetBool(nameof(CheckForPendingDatabaseMigrations));
    }
}
