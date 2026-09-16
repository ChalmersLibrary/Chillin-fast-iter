using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chalmers.ILL.Configuration
{
    // Was IConfiguration. Renamed in fas 6: the app is moving to
    // Microsoft.Extensions.Configuration, whose own IConfiguration would collide with this one on
    // every file that needs both - and worse, would silently resolve to the wrong one in files that
    // only `using Microsoft.Extensions.Configuration`. This is app configuration, not the framework
    // abstraction, so it gets the distinct name.
    //
    // Expanded in fas 6 to cover every key Web.config's <appSettings> used to hold (46 keys) plus
    // the 19 that the code read but the dev App.config bridge never defined - see
    // TODO-remove-dotnet-framework.md, fas 6. Backed by appsettings.json under a "Chillin" section
    // (DefaultChillinConfiguration), not ConfigurationManager.AppSettings.
    public interface IChillinConfiguration
    {
        // Hosts / network
        string BaseUrl { get; }
        string TestServer { get; }
        string LiveServer { get; }
        string CronServerIpAddress { get; }

        // Mail (Microsoft Graph - the active production mail path)
        bool UseMicrosoftGraphMailService { get; }
        string MicrosoftGraphApiUserId { get; }
        string MicrosoftGraphApiEndpoint { get; }
        string MicrosoftGraphAuthority { get; }
        string MicrosoftGraphClientId { get; }
        string MicrosoftGraphClientSecret { get; }

        // Mail (legacy Exchange-shaped fields - still read by IMailWebApi callers even though the
        // Graph implementation ignores the connect credentials)
        string ChalmersIllExchangeLogin { get; }
        string ChalmersIllExchangePassword { get; }
        string ChalmersIllSenderAddress { get; }
        string ChalmersIllForwardingAddress { get; }
        bool ChalmersIllArchiveProcessedMails { get; }
        string ChalmersIllMailSubject { get; }
        string BugFixersMailingList { get; }

        // Storage / search
        string StorageConnectionString { get; }
        string ElasticSearchUrl { get; }
        string ElasticSearchIndex { get; }
        string ElasticSearchTemplatesIndex { get; }

        // Libris (source discontinued 2025-09-08, but the class still compiles - see
        // Providers/LibrisOrderItemsSource.cs)
        string LibrisApiBaseAddress { get; }
        string LibrisApiUserRequestSuffix { get; }
        string LibrisApiKey { get; }
        string LibrarySigel { get; }

        // FOLIO
        string FolioApiBaseAddress { get; }
        string FolioXOkapiTenant { get; }
        string FolioUsername { get; }
        string FolioPassword { get; }
        string FolioSourceId { get; }
        string ServicePointHuvudbiblioteketId { get; }
        string ServicePointLindholmenbiblioteketId { get; }
        string ServicePointArkitekturbiblioteketId { get; }
        string HoldingPermanentLocationId { get; }
        string ChillinStatisticalCodeId { get; }
        string InstanceResourceTypeId { get; }
        string InstanceStatusId { get; }
        string InstanceModesOfIssuance { get; }
        string InstanceIdentifierTypeId { get; }
        string ItemMaterialTypeId { get; }
        string ItemPermanentLoanTypeId { get; }
        string ItemPermanentLoanTypeIdInHouse { get; }

        // Patron cache (Solr - see the dead-code note on SierraCache/SolrLibcdksAffiliationDataProvider
        // in TODO-remove-dotnet-framework.md, fas 6: neither is wired up in Bootstrapper today)
        string PatronCacheSolrQueryUrl { get; }
        string PatronAffiliationSolrQueryUrl { get; }
        string PatronCacheSolrBasicAuthUsername { get; }
        string PatronCacheSolrBasicAuthPassword { get; }

        // LibPSearch (PDB) - the active patron/affiliation data source
        string LibPSearchUrl { get; }
        string LibPSearchApiKey { get; }

        // Misc
        string OrderListPageUrl { get; }
        string StatusEditingAtMailSendInfo { get; }
        string StatisticsUrl { get; }
        bool ShowManualMailFetchingTools { get; }
        string ManualAnonymizationImplementationDate { get; }
        bool CheckForPendingDatabaseMigrations { get; }
    }
}
