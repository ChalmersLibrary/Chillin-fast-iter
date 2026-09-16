using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chalmers.ILL.Tests.Controllers
{
    // Fas 6, isolerat läge steg A - the fourth "test som hör till steg A": with the fakes in place,
    // Bootstrapper.RegisterTypes no longer needs a single real credential, so a real
    // WebApplicationFactory<Program> pipeline test is possible for the first time (previously it
    // would have needed a live Elasticsearch/FOLIO/Graph to even construct the DI container -
    // RoutingTest.cs's hand-rolled minimal host was the closest thing available). This is the
    // pipeline-level login/authorization test fas 3 deliberately left for "when the isolation
    // registrations exist."
    [TestClass]
    public class IsolatedModeSmokeTest
    {
        private WebApplicationFactory<Program> CreateFactory()
        {
            var dataPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "chillin-isolated-smoke-" + System.Guid.NewGuid());

            return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configBuilder) =>
                {
                    configBuilder.AddInMemoryCollection(new Dictionary<string, string>
                    {
                        ["Chillin:Isolated"] = "true",
                        ["Chillin:DataPath"] = dataPath,
                        ["Chillin:BaseUrl"] = "http://localhost/"
                    });
                });
            });
        }

        [TestMethod]
        public async Task Root_Unauthenticated_RedirectsToLoginPage()
        {
            using var factory = CreateFactory();
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var response = await client.GetAsync("/");

            Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);
            StringAssert.Contains(response.Headers.Location.ToString(), "ChalmersILLLoginPage");
        }

        [TestMethod]
        public async Task LoginPage_Unauthenticated_RendersSuccessfully()
        {
            using var factory = CreateFactory();
            using var client = factory.CreateClient();

            var response = await client.GetAsync("/ChalmersILLLoginPage");

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        }
    }
}
