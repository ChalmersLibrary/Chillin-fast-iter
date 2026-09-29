using System.Collections.Generic;
using System.Linq;
using Chalmers.ILL.Configuration;
using Chalmers.ILL.Isolated;
using Chalmers.ILL.Models;
using Chalmers.ILL.OrderItems;
using Chalmers.ILL.Tests.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chalmers.ILL.Tests
{
    // Fas 6, isolerat läge steg A: the three "Tester som hör till steg A" that don't need a real
    // HTTP pipeline (the fourth, a WebApplicationFactory<Program> test, is in
    // Controllers/IsolatedModeSmokeTest.cs, now possible for the first time because these fakes
    // exist - Bootstrapper.RegisterTypes no longer needs a single real credential to succeed).
    [TestClass]
    public class BootstrapperIsolationTest
    {
        private static IServiceCollection BuildServices(IDictionary<string, string> configValues)
        {
            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(configValues).Build());
            services.AddSingleton<IWebHostEnvironment>(new StubWebHostEnvironment());
            return services;
        }

        [TestMethod]
        public void RegisterTypes_IsolatedMode_SucceedsWithZeroConfigurationBeyondTheSwitch()
        {
            // The regression the eager ElasticClient/FolioConnection construction used to cause:
            // nothing but Chillin:Isolated and a scratch DataPath is set here.
            var services = BuildServices(new Dictionary<string, string>
            {
                ["Chillin:Isolated"] = "true",
                ["Chillin:DataPath"] = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "chillin-isolation-test-" + System.Guid.NewGuid())
            });

            Bootstrapper.RegisterTypes(services);

            var provider = services.BuildServiceProvider();
            foreach (var seam in IsolationGuard.SeamInterfaces)
            {
                var instance = provider.GetRequiredService(seam);
                Assert.AreEqual(typeof(IsolationGuard).Namespace, instance.GetType().Namespace,
                    $"{seam.Name} resolved to {instance.GetType().FullName}, which isn't a Chalmers.ILL.Isolated type.");
            }
        }

        [TestMethod]
        public void RegisterTypes_LiveMode_RegistersOnlyRealTypes()
        {
            var services = BuildServices(new Dictionary<string, string>
            {
                ["Chillin:Isolated"] = "false",
                ["Chillin:ElasticSearchUrl"] = "http://localhost:9200",
                ["Chillin:ElasticSearchIndex"] = "chillintest",
                ["Chillin:DataPath"] = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "chillin-live-test-" + System.Guid.NewGuid())
            });

            Bootstrapper.RegisterTypes(services);

            var provider = services.BuildServiceProvider();
            foreach (var seam in IsolationGuard.SeamInterfaces)
            {
                var instance = provider.GetRequiredService(seam);
                Assert.AreNotEqual(typeof(IsolationGuard).Namespace, instance.GetType().Namespace,
                    $"{seam.Name} resolved to {instance.GetType().FullName}, a Chalmers.ILL.Isolated fake, in Live mode.");
            }
        }

        [TestMethod]
        public void RegisterTypes_IsolatedModeWithARealLookingSecretFilledIn_ThrowsInsteadOfRunning()
        {
            // The scenario the guard exists for: production's App Settings (a real Graph client
            // secret) accidentally cloned onto an app that also has Chillin:Isolated=true.
            var services = BuildServices(new Dictionary<string, string>
            {
                ["Chillin:Isolated"] = "true",
                ["Chillin:MicrosoftGraphClientSecret"] = "a-real-looking-secret-value",
                ["Chillin:DataPath"] = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "chillin-isolation-test-" + System.Guid.NewGuid())
            });

            Assert.ThrowsException<System.InvalidOperationException>(() => Bootstrapper.RegisterTypes(services));
        }

        [TestMethod]
        public void RegisterTypes_IsolatedModeWithOnlyPlaceholderSecrets_DoesNotThrow()
        {
            // "******"/"xxx" are the established not-a-real-value placeholders (fas 6) - they must
            // not trip the guard, or every fresh checkout's appsettings.json would fail isolated mode.
            var services = BuildServices(new Dictionary<string, string>
            {
                ["Chillin:Isolated"] = "true",
                ["Chillin:MicrosoftGraphClientSecret"] = "******",
                ["Chillin:FolioPassword"] = "xxx",
                ["Chillin:DataPath"] = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "chillin-isolation-test-" + System.Guid.NewGuid())
            });

            Bootstrapper.RegisterTypes(services);
        }

        [TestMethod]
        public void RegisterTypes_IsolatedMode_OrderTypeChangeIsVisibleThroughAnyServiceProviderBuiltFromTheSameCollection()
        {
            // Reproduces a reported bug: after changing an order's type, reloading the order list
            // still showed the old type while the order's own page showed the new one. Root cause:
            // IOrderItemSearcher used to be registered type-based
            // (AddSingleton<IOrderItemSearcher, T>), so a second IServiceProvider built from this
            // same IServiceCollection - exactly what Program.cs's builder.Build() does relative to
            // the "interim"/"final" providers built inside RegisterTypes - got its own, separate
            // InMemoryOrderItemSearcher instance that FileOrderItemManager's writes never reached.
            var services = BuildServices(new Dictionary<string, string>
            {
                ["Chillin:Isolated"] = "true",
                ["Chillin:DataPath"] = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "chillin-isolation-test-" + System.Guid.NewGuid())
            });

            Bootstrapper.RegisterTypes(services);

            // providerB stands in for Program.cs's real runtime ServiceProvider (app.Services) -
            // a second provider built from the same collection, just like builder.Build() is
            // relative to the providers built inside RegisterTypes.
            var providerA = services.BuildServiceProvider();
            var providerB = services.BuildServiceProvider();

            var searcherFromA = providerA.GetRequiredService<IOrderItemSearcher>();
            var searcherFromB = providerB.GetRequiredService<IOrderItemSearcher>();
            Assert.AreSame(searcherFromA, searcherFromB,
                "IOrderItemSearcher must resolve to the same instance from every IServiceProvider built off this IServiceCollection, or writes made through IOrderItemManager never reach whichever instance a controller resolved.");

            // doSignal: false - SetNotifier is only wired up in Program.cs, after the app's real
            // ServiceProvider exists (see the comment in Bootstrapper.RegisterTypes); irrelevant to
            // what this test is pinning down.
            var orderItemManager = providerA.GetRequiredService<IOrderItemManager>();
            var nodeId = orderItemManager.CreateOrderItemInDbFromOrderItemModel(new OrderItemModel
            {
                LogItemsList = new List<LogItem>(),
                AttachmentList = new List<OrderAttachment>(),
                TypeId = 11 // "Artikel"
            }, doSignal: false);

            orderItemManager.SetType(nodeId, 12, "event-1", doSignal: false); // -> "Bok"

            var found = searcherFromB.Search("type:Bok").SingleOrDefault(o => o.NodeId == nodeId);
            Assert.IsNotNull(found,
                "Order list (IOrderItemSearcher, resolved from a separate ServiceProvider) never saw the type change made through IOrderItemManager.");
        }
    }
}
