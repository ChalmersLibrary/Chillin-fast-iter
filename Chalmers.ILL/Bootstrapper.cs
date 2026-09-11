using Chalmers.ILL.Configuration;
using Chalmers.ILL.Connections;
using Chalmers.ILL.DependencyResolution;
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
using Microsoft.Practices.Unity;
using Nest;
using System;
using System.Net.Http;
using System.Web.Http;
using System.Web.Mvc;

namespace Chalmers.ILL
{
    public static class Bootstrapper
    {
        public static IUnityContainer Initialise()
        {
            var container = BuildUnityContainer();
            GlobalConfiguration.Configuration.DependencyResolver = new UnityWebApiDependencyResolver(container);
            DependencyResolver.SetResolver(new UnityMvcDependencyResolver(container));

            return container;
        }

        private static IUnityContainer BuildUnityContainer()
        {
            var container = new UnityContainer();

            RegisterTypes(container);

            return container;
        }

        public static void RegisterTypes(IUnityContainer container)
        {
            container.RegisterType<IConfiguration, DefaultChillinConfiguration>();
            var config = container.Resolve<IConfiguration>();

            var elasticClientSettings = new ConnectionSettings(new System.Uri(config.ElasticSearchUrl));
            elasticClientSettings.DefaultIndex(config.ElasticSearchIndex);
            var elasticClient = new ElasticClient(elasticClientSettings);

            container.RegisterInstance<IElasticClient>(elasticClient);
            container.RegisterInstance(new HttpClient());

            if (config.UseMicrosoftGraphMailService)
            {
                container.RegisterType<IExchangeMailWebApi, MicrosoftGraphMailWebApi>();
            }
            else
            {
                container.RegisterType<IExchangeMailWebApi, ExchangeMailWebApi>();
            }
            container.RegisterType<ISourceFactory, ChalmersSourceFactory>();
            container.RegisterType<IMediaItemManager, BlobStorageMediaItemManager>();
            container.RegisterType<IOrderItemSearcher, ElasticSearchOrderItemSearcher>();
            container.RegisterType<ITemplateService, ElasticsearchTemplateService>();
            container.RegisterType<IAffiliationDataProvider, PdbAffiliationDataProvider>();

            container.RegisterType<IChillinTextRepository, ChillinTextRepository>();
            container.RegisterType<IJsonService, JsonService>();

            // Comment these to not touch FOLIO
            container.RegisterInstance<IFolioConnection>(new FolioConnection()); // Singleton to reuse tokens between calls
            container.RegisterType<IFolioItemService, FolioItemService>();
            container.RegisterType<IFolioRepository, FolioRepository>();
            container.RegisterType<IFolioService, FolioService>();
            container.RegisterType<IFolioInstanceService, FolioInstanceService>();
            container.RegisterType<IFolioHoldingService, FolioHoldingService>();
            container.RegisterType<IFolioCirculationService, FolioCirculationService>();
            container.RegisterType<IFolioUserService, FolioUserService>();

            container.RegisterType<IPersonDataProvider, PdbPersonDataProvider>();

            var templateService = container.Resolve<ITemplateService>();
            var affiliationDataProvider = container.Resolve<IAffiliationDataProvider>();

            var orderConfig = new ChillinOrderConfiguration();
            container.RegisterInstance(typeof(IChillinOrderConfiguration), orderConfig);

            // Create all our singleton type instances.
            var mailService = new MailService(container.Resolve<IMediaItemManager>(), container.Resolve<IExchangeMailWebApi>());
            var notifier = new Notifier();
            var orderItemManager = new EntityFrameworkOrderItemManager(orderConfig, container.Resolve<IOrderItemSearcher>());
            var providerService = new ProviderService(container.Resolve<IOrderItemSearcher>());
            var bulkDataManager = new BulkDataManager(container.Resolve<IOrderItemSearcher>());

            // Connect instances that depend on eachother.
            orderItemManager.SetNotifier(notifier);

            // Hook up more stuff
            container.RegisterInstance(typeof(IMemberInfoManager), new MemberInfoManager());
            container.RegisterInstance(typeof(IMemberAdminService), new MemberAdminService());
            container.RegisterInstance(typeof(INotifier), notifier);
            container.RegisterInstance(typeof(IOrderItemManager), orderItemManager);
            container.RegisterInstance(typeof(IAutomaticMailSendingEngine), new AutomaticMailSendingEngine(container.Resolve<IOrderItemSearcher>(), templateService, orderItemManager, mailService));
            container.RegisterInstance(typeof(IMailService), mailService);
            container.RegisterInstance(typeof(IProviderService), providerService);
            container.RegisterInstance(typeof(IBulkDataManager), bulkDataManager);
            container.RegisterInstance<IPatronDataProvider>(new FolioPatronDataProvider(templateService, affiliationDataProvider, container.Resolve<IFolioConnection>()));
        }
    }
}