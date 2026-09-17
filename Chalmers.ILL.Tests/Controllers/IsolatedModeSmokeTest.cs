using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
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
        private WebApplicationFactory<Program> CreateFactory(IDictionary<string, string> extraConfig = null)
        {
            var dataPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "chillin-isolated-smoke-" + System.Guid.NewGuid());

            var config = new Dictionary<string, string>
            {
                ["Chillin:Isolated"] = "true",
                ["Chillin:DataPath"] = dataPath,
                ["Chillin:BaseUrl"] = "http://localhost/"
            };
            if (extraConfig != null)
            {
                foreach (var kvp in extraConfig)
                    config[kvp.Key] = kvp.Value;
            }

            return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configBuilder) =>
                {
                    configBuilder.AddInMemoryCollection(config);
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

        // Fas 10, "Vyerna redirectar till produktion om värdnamnet är okänt": with an unrecognised
        // Host header (neither localhost, testServer nor liveServer), the login page must redirect
        // to liveServer and, just as importantly, must actually stop rendering there - Core's
        // Response.Redirect does not abort execution the way System.Web's did, so without the
        // explicit `return;` fixed in ChalmersILLLoginPage.cshtml the full page would still render
        // (and be sent) after the redirect headers.
        [TestMethod]
        public async Task LoginPage_UnrecognisedHost_RedirectsToLiveServerAndStopsRendering()
        {
            using var factory = CreateFactory(new Dictionary<string, string>
            {
                ["Chillin:TestServer"] = "test.example.com",
                ["Chillin:LiveServer"] = "live.example.com"
            });
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            using var request = new HttpRequestMessage(HttpMethod.Get, "/ChalmersILLLoginPage");
            request.Headers.Host = "attacker.example.com";

            var response = await client.SendAsync(request);

            Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);
            Assert.AreEqual("https://live.example.com/", response.Headers.Location.ToString());
            Assert.AreEqual(string.Empty, await response.Content.ReadAsStringAsync());
        }

        // Fas 10, "Konfigurera ForwardedHeaders för App Service": SystemSurfaceController's
        // cron-server IP check (and any real-client-IP check) relies entirely on
        // ForwardedHeadersMiddleware folding X-Forwarded-For into Connection.RemoteIpAddress
        // (fas 2). On Azure App Service the immediate connecting proxy is neither loopback nor a
        // known/stable address, so the middleware's *default* KnownProxies/KnownNetworks
        // (loopback only) would reject the header - RemoteIpAddress would stay the proxy's own
        // address and the cron server would be silently denied (Update() swallows the failure
        // and returns an empty, still-200-OK result - fas 0a). Tested directly against the
        // middleware rather than through SystemSurfaceController.Update(), because that action's
        // *own* result also depends on ChalmersOrderItemsMailSource.Poll() succeeding, which is
        // unrelated to this fix and not something this test should be coupled to.
        [TestMethod]
        public async Task ForwardedHeaders_XForwardedForWithPort_SetsRemoteIpAddressEvenBehindAnUntrustedProxy()
        {
            using var factory = CreateFactory();

            var context = await factory.Server.SendAsync(c =>
            {
                c.Request.Method = "GET";
                c.Request.Path = "/ChalmersILLLoginPage";
                c.Request.Host = new HostString("cron-caller.example.com");
                // Azure sends X-Forwarded-For as "ip:port" - the middleware's own parsing must
                // strip the port (fas 2's note that this is already relied upon elsewhere).
                c.Request.Headers["X-Forwarded-For"] = "203.0.113.5:54321";
                // TestServer's simulated connection defaults to loopback, which the middleware's
                // *default* KnownNetworks/KnownProxies already trust regardless of this fix -
                // that would make the test pass even without it. Set it to a non-loopback address
                // instead, standing in for Azure App Service's own front-end/load balancer hop,
                // which is exactly what isn't loopback and isn't a known/stable address in
                // production - the scenario this fix is for.
                c.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.4");
            });

            Assert.AreEqual("203.0.113.5", context.Connection.RemoteIpAddress?.ToString());
        }
    }
}
