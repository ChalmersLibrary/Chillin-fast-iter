using Chalmers.ILL.Configuration;
using Chalmers.ILL.Connections;
using Chalmers.ILL.Isolated;
using Chalmers.ILL.Mail;
using Chalmers.ILL.MediaItems;
using Chalmers.ILL.Members;
using Chalmers.ILL.Models;
using Chalmers.ILL.OrderItems;
using Chalmers.ILL.Patron;
using Chalmers.ILL.Providers;
using Chalmers.ILL.Repositories;
using Chalmers.ILL.Services;
using Chalmers.ILL.SignalR;
using Chalmers.ILL.Templates;
using Chalmers.ILL.UmbracoApi;
using Microsoft.Extensions.DependencyInjection;
using Nest;
using System;
using System.IO;
using System.Net.Http;

namespace Chalmers.ILL
{
    // Was a Unity container wired into MVC5's IDependencyResolver. System.Web.Mvc doesn't exist
    // on modern .NET, and Unity 3.0.1304.1 (Microsoft.Practices.Unity) predates netstandard, so
    // this went straight to ASP.NET Core's built-in IServiceCollection rather than bridging Unity
    // across - full DI/config migration is fas 6, but the container itself couldn't survive the
    // TFM switch regardless of phase ordering.
    public static class Bootstrapper
    {
        private static readonly log4net.ILog _log = log4net.LogManager.GetLogger(typeof(Bootstrapper));

        public static void RegisterTypes(IServiceCollection services)
        {
            services.AddSingleton<IChillinConfiguration, DefaultChillinConfiguration>();

            // Unity's imperative container let RegisterTypes resolve earlier registrations while
            // still building up later ones. IServiceCollection has no such resolve-as-you-go API,
            // so an interim provider is built partway through to get the same effect.
            var interim = services.BuildServiceProvider();
            var config = interim.GetRequiredService<IChillinConfiguration>();

            services.AddSingleton(new HttpClient());

            // Isolerat läge (fas 6, isolerat läge steg A): every integration seam is registered by
            // one branch or the other, never both, and never conditionally past this point - see
            // IsolationGuard for the runtime proof. This is also why FolioConnection/ElasticClient
            // (the two real clients that used to be constructed unconditionally right here) moved
            // into RegisterLiveSeams: they must never run at all in isolated mode.
            if (config.Isolated)
            {
                RegisterIsolatedSeams(services);
            }
            else
            {
                RegisterLiveSeams(services, config);
            }

            services.AddTransient<ISourceFactory, ChalmersSourceFactory>();
            services.AddTransient<IJsonService, JsonService>();

            // Rebuild now that the seam registrations above exist, so the resolves below can
            // construct their dependencies.
            interim = services.BuildServiceProvider();
            var templateService = interim.GetRequiredService<ITemplateService>();
            var affiliationDataProvider = interim.GetRequiredService<IAffiliationDataProvider>();
            var orderItemSearcher = interim.GetRequiredService<IOrderItemSearcher>();
            var mediaItemManager = interim.GetRequiredService<IMediaItemManager>();
            var mailWebApi = interim.GetRequiredService<IMailWebApi>();
            var folioConnection = interim.GetRequiredService<IFolioConnection>();

            // chillinPrevalues.json and members.json both live under DataPath now (fas 6, isolerat
            // läge steg A, "Datarot") - not next to the deployed binaries.
            var orderConfig = new ChillinOrderConfiguration(config.DataPath);
            services.AddSingleton<IChillinOrderConfiguration>(orderConfig);

            var membersPath = Path.Combine(config.DataPath, "members.json");
            services.AddSingleton(_ => new FileMembershipProvider(
                () => MemberFileStore.Load(membersPath),
                accounts => MemberFileStore.Save(accounts, membersPath)));
            services.AddSingleton(_ => new FileRoleProvider(() => MemberFileStore.Load(membersPath)));

            // Create all our singleton type instances.
            var mailService = new MailService(mediaItemManager, mailWebApi, config);
            var orderItemManager = new EntityFrameworkOrderItemManager(orderConfig, orderItemSearcher);
            var providerService = new ProviderService(orderItemSearcher);
            var bulkDataManager = new BulkDataManager(orderItemSearcher);

            // Notifier needs IHubContext<NotificationHub>, which only exists once the app's real
            // ServiceProvider is built (after AddSignalR()) - not available here. Registered as a
            // DI-constructed singleton instead of the eager "new" pattern used elsewhere in this
            // method; Program.cs wires orderItemManager.SetNotifier(...) once the app is built.
            services.AddSingleton<INotifier, Notifier>();

            // Hook up more stuff
            services.AddSingleton<IMemberInfoManager>(new MemberInfoManager());
            services.AddSingleton<IMemberAdminService>(new MemberAdminService(
                () => MemberFileStore.Load(membersPath),
                accounts => MemberFileStore.Save(accounts, membersPath)));
            services.AddSingleton<IOrderItemManager>(orderItemManager);
            services.AddSingleton<IAutomaticMailSendingEngine>(new AutomaticMailSendingEngine(orderItemSearcher, templateService, orderItemManager, mailService));
            services.AddSingleton<IMailService>(mailService);
            services.AddSingleton<IProviderService>(providerService);
            services.AddSingleton<IBulkDataManager>(bulkDataManager);

            // Isolated mode's FilePatronDataProvider already backs IAffiliationDataProvider (see
            // RegisterIsolatedSeams) - reuse that same instance rather than wrapping it in a second
            // fake, so all patron-shaped lookups agree on one small table.
            services.AddSingleton<IPatronDataProvider>(config.Isolated
                ? (IPatronDataProvider)affiliationDataProvider
                : new FolioPatronDataProvider(templateService, affiliationDataProvider, folioConnection));

            // Räcke 2 (fas 6, isolerat läge steg A): crash rather than run wrong, against the final
            // registration state.
            var final = services.BuildServiceProvider();
            IsolationGuard.Verify(final, config);

            LogIsolationBanner(config);
        }

        private static void RegisterLiveSeams(IServiceCollection services, IChillinConfiguration config)
        {
            var elasticClientSettings = new ConnectionSettings(new Uri(config.ElasticSearchUrl));
            elasticClientSettings.DefaultIndex(config.ElasticSearchIndex);
            var elasticClient = new ElasticClient(elasticClientSettings);
            services.AddSingleton<IElasticClient>(elasticClient);

            // EWS is gone (fas 8) - Graph is the only mail path now, see designbeslut in CLAUDE.md.
            services.AddTransient<IMailWebApi, MicrosoftGraphMailWebApi>();
            services.AddTransient<IMediaItemManager, BlobStorageMediaItemManager>();
            services.AddTransient<IOrderItemSearcher, ElasticSearchOrderItemSearcher>();
            services.AddTransient<ITemplateService, ElasticsearchTemplateService>();
            services.AddTransient<IAffiliationDataProvider, PdbAffiliationDataProvider>();
            services.AddTransient<IChillinTextRepository, ChillinTextRepository>();
            services.AddTransient<IPersonDataProvider, PdbPersonDataProvider>();

            services.AddSingleton<IFolioConnection>(new FolioConnection(config)); // Singleton to reuse tokens between calls
            services.AddTransient<IFolioItemService, FolioItemService>();
            services.AddTransient<IFolioRepository, FolioRepository>();
            services.AddTransient<IFolioService, FolioService>();
            services.AddTransient<IFolioInstanceService, FolioInstanceService>();
            services.AddTransient<IFolioHoldingService, FolioHoldingService>();
            services.AddTransient<IFolioCirculationService, FolioCirculationService>();
            services.AddTransient<IFolioUserService, FolioUserService>();
        }

        private static void RegisterIsolatedSeams(IServiceCollection services)
        {
            services.AddTransient<IMailWebApi, FileMailWebApi>();
            services.AddTransient<IMediaItemManager, FileMediaItemManager>();
            // Not steg B's real search replacement - see NullOrderItemSearcher. Docker-compose
            // Elasticsearch is still the developer path for the order list itself.
            services.AddTransient<IOrderItemSearcher, NullOrderItemSearcher>();
            services.AddTransient<ITemplateService, FileTemplateService>();
            services.AddTransient<IChillinTextRepository, FileChillinTextRepository>();

            // One shared instance backs all three patron-shaped interfaces (fas 6, isolerat läge
            // steg A) so a lookup gives the same answer regardless of which interface asked.
            var patronDataProvider = new FilePatronDataProvider();
            services.AddSingleton<IAffiliationDataProvider>(patronDataProvider);
            services.AddSingleton<IPersonDataProvider>(patronDataProvider);

            services.AddSingleton<IFolioConnection>(new FakeFolioConnection());
            var fakeFolio = new FakeFolio();
            services.AddSingleton<IFolioItemService>(fakeFolio);
            services.AddSingleton<IFolioRepository>(fakeFolio);
            services.AddSingleton<IFolioService>(fakeFolio);
            services.AddSingleton<IFolioInstanceService>(fakeFolio);
            services.AddSingleton<IFolioHoldingService>(fakeFolio);
            services.AddSingleton<IFolioCirculationService>(fakeFolio);
            services.AddSingleton<IFolioUserService>(fakeFolio);
        }

        // Räcke 3 (fas 6, isolerat läge steg A): frånvaro av bannern ska aldrig vara tvetydig -
        // isolerat läge loggar en WARN-ram, Live loggar en enkel INFO-rad.
        private static void LogIsolationBanner(IChillinConfiguration config)
        {
            if (config.Isolated)
            {
                _log.Warn(
                    "########## ISOLERAT LÄGE (Chillin:Isolated=true) ##########\n" +
                    "Inga riktiga integrationer används. Fejkade sömmar:\n" +
                    " - IMailWebApi -> Isolated.FileMailWebApi\n" +
                    " - IMediaItemManager -> Isolated.FileMediaItemManager\n" +
                    " - IOrderItemSearcher -> Isolated.NullOrderItemSearcher (tom - orderlistan kräver riktig Elasticsearch)\n" +
                    " - ITemplateService -> Isolated.FileTemplateService\n" +
                    " - IChillinTextRepository -> Isolated.FileChillinTextRepository\n" +
                    " - IPatronDataProvider/IAffiliationDataProvider/IPersonDataProvider -> Isolated.FilePatronDataProvider\n" +
                    " - IFolioConnection -> Isolated.FakeFolioConnection\n" +
                    " - IFolio*Service/IFolioRepository -> Isolated.FakeFolio\n" +
                    "############################################################");
            }
            else
            {
                _log.Info("Live-läge (Chillin:Isolated=false): riktiga integrationer registrerade (Elasticsearch, FOLIO, Graph-mail, Azure Blob Storage).");
            }
        }
    }
}
