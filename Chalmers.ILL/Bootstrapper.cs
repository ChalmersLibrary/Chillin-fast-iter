using Chalmers.ILL.Configuration;
using Chalmers.ILL.Connections;
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
        public static void RegisterTypes(IServiceCollection services)
        {
            services.AddSingleton<IChillinConfiguration, DefaultChillinConfiguration>();

            // Unity's imperative container let RegisterTypes resolve earlier registrations while
            // still building up later ones. IServiceCollection has no such resolve-as-you-go API,
            // so an interim provider is built partway through to get the same effect.
            var interim = services.BuildServiceProvider();
            var config = interim.GetRequiredService<IChillinConfiguration>();

            var elasticClientSettings = new ConnectionSettings(new Uri(config.ElasticSearchUrl));
            elasticClientSettings.DefaultIndex(config.ElasticSearchIndex);
            var elasticClient = new ElasticClient(elasticClientSettings);

            services.AddSingleton<IElasticClient>(elasticClient);
            services.AddSingleton(new HttpClient());

            // EWS is gone (fas 8) - Graph is the only mail path now, see designbeslut in CLAUDE.md.
            services.AddTransient<IMailWebApi, MicrosoftGraphMailWebApi>();
            services.AddTransient<ISourceFactory, ChalmersSourceFactory>();
            services.AddTransient<IMediaItemManager, BlobStorageMediaItemManager>();
            services.AddTransient<IOrderItemSearcher, ElasticSearchOrderItemSearcher>();
            services.AddTransient<ITemplateService, ElasticsearchTemplateService>();
            services.AddTransient<IAffiliationDataProvider, PdbAffiliationDataProvider>();

            services.AddTransient<IChillinTextRepository, ChillinTextRepository>();
            services.AddTransient<IJsonService, JsonService>();

            // Comment these to not touch FOLIO
            services.AddSingleton<IFolioConnection>(new FolioConnection()); // Singleton to reuse tokens between calls
            services.AddTransient<IFolioItemService, FolioItemService>();
            services.AddTransient<IFolioRepository, FolioRepository>();
            services.AddTransient<IFolioService, FolioService>();
            services.AddTransient<IFolioInstanceService, FolioInstanceService>();
            services.AddTransient<IFolioHoldingService, FolioHoldingService>();
            services.AddTransient<IFolioCirculationService, FolioCirculationService>();
            services.AddTransient<IFolioUserService, FolioUserService>();

            services.AddTransient<IPersonDataProvider, PdbPersonDataProvider>();

            // Rebuild now that the rest of this method's registrations exist, so the resolves
            // below can construct their dependencies.
            interim = services.BuildServiceProvider();
            var templateService = interim.GetRequiredService<ITemplateService>();
            var affiliationDataProvider = interim.GetRequiredService<IAffiliationDataProvider>();
            var orderItemSearcher = interim.GetRequiredService<IOrderItemSearcher>();
            var mediaItemManager = interim.GetRequiredService<IMediaItemManager>();
            var mailWebApi = interim.GetRequiredService<IMailWebApi>();
            var folioConnection = interim.GetRequiredService<IFolioConnection>();

            var orderConfig = new ChillinOrderConfiguration();
            services.AddSingleton<IChillinOrderConfiguration>(orderConfig);

            // Create all our singleton type instances.
            var mailService = new MailService(mediaItemManager, mailWebApi);
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
            services.AddSingleton<IMemberAdminService>(new MemberAdminService());
            services.AddSingleton<IOrderItemManager>(orderItemManager);
            services.AddSingleton<IAutomaticMailSendingEngine>(new AutomaticMailSendingEngine(orderItemSearcher, templateService, orderItemManager, mailService));
            services.AddSingleton<IMailService>(mailService);
            services.AddSingleton<IProviderService>(providerService);
            services.AddSingleton<IBulkDataManager>(bulkDataManager);
            services.AddSingleton<IPatronDataProvider>(new FolioPatronDataProvider(templateService, affiliationDataProvider, folioConnection));
        }
    }
}
