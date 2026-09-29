using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Chalmers.ILL.Members;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

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
        // Fas 10 discovery (2026-09-16): Bootstrapper.RegisterTypes reads IChillinConfiguration
        // (config.Isolated, config.DataPath, ...) to decide which DI seams to register, and it does
        // that *before* builder.Build() runs. WebApplicationFactory<Program>.WithWebHostBuilder's
        // ConfigureAppConfiguration customization is only spliced into the builder as part of
        // Build() itself for a minimal-hosting Program.cs - too late for that early read. It was
        // silently seeing only the process's real appsettings/env vars all along: every test below
        // that predates this comment was actually exercising RegisterLiveSeams against whatever
        // ambient config this machine happens to have, not RegisterIsolatedSeams, and nothing
        // caught it because the live seams don't crash just from being constructed (NEST/FOLIO
        // clients are lazy). Environment variables, unlike ConfigureAppConfiguration, *are* part of
        // what WebApplication.CreateBuilder(args) composes immediately - so setting them (which is
        // also literally how App Settings reach the app in Azure) is what actually threads
        // overrides through to that early read.
        // members.json isn't seeded by the app itself (DevDataSeeder deliberately doesn't - see its
        // class comment: creating the first account is a one-time manual step, not something to
        // regenerate on every fresh DataPath). Tests that need to log in ask for one here instead,
        // which is the test-only equivalent of that manual step.
        private WebApplicationFactory<Program> CreateFactory(IDictionary<string, string> extraConfig = null, bool seedSuperAdminAccount = false, bool seedViewerAccount = false, bool seedRolelessAccount = false)
        {
            var dataPath = Path.Combine(Path.GetTempPath(), "chillin-isolated-smoke-" + Guid.NewGuid());

            if (seedSuperAdminAccount)
            {
                SeedSuperAdminAccount(dataPath);
            }

            if (seedViewerAccount)
            {
                SeedViewerAccount(dataPath);
            }

            if (seedRolelessAccount)
            {
                SeedRolelessAccount(dataPath);
            }

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

            var previousValues = new Dictionary<string, string>();
            foreach (var kvp in config)
            {
                var envKey = kvp.Key.Replace(":", "__");
                previousValues[envKey] = Environment.GetEnvironmentVariable(envKey);
                Environment.SetEnvironmentVariable(envKey, kvp.Value);
            }

            try
            {
                var factory = new WebApplicationFactory<Program>();
                _ = factory.Server; // force the host (and Bootstrapper.RegisterTypes) to build now, while the env vars above are set
                return factory;
            }
            finally
            {
                foreach (var kvp in previousValues)
                    Environment.SetEnvironmentVariable(kvp.Key, kvp.Value);
            }
        }

        // Same PasswordHasher<T>/IdentityV2-compatibility setup as FileMembershipProvider itself
        // (and as FileMembershipProvider_ResolvedFromDI_ReadsAccountsFromConfiguredDataPath below).
        private static void SeedSuperAdminAccount(string dataPath)
        {
            Directory.CreateDirectory(dataPath);

            var hasher = new PasswordHasher<MemberAccount>(Options.Create(new PasswordHasherOptions
            {
                CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV2
            }));
            var account = new MemberAccount { Login = "superadmin", Roles = new List<string> { "Desk", "Administrator", "SuperAdmin" } };
            account.PasswordHash = hasher.HashPassword(account, "chillin-dev-superadmin");

            MemberFileStore.Save(new List<MemberAccount> { account }, Path.Combine(dataPath, "members.json"));
        }

        // An account explicitly assigned only the "Viewer" role - read-only, see
        // ViewerReadOnlyFilter. Mutually exclusive with the other Seed* helpers here - they all
        // write the same members.json and would clobber each other, and no current test needs more
        // than one seeded account.
        private static void SeedViewerAccount(string dataPath)
        {
            Directory.CreateDirectory(dataPath);

            var hasher = new PasswordHasher<MemberAccount>(Options.Create(new PasswordHasherOptions
            {
                CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV2
            }));
            var account = new MemberAccount { Login = "viewer", Roles = new List<string> { "Viewer" } };
            account.PasswordHash = hasher.HashPassword(account, "chillin-dev-viewer");

            MemberFileStore.Save(new List<MemberAccount> { account }, Path.Combine(dataPath, "members.json"));
        }

        // An account with NO roles at all - the exact scenario reported by the user ("jag kan se
        // ordrar även om jag helt saknar roles"). Stricter than Viewer: blocked from everything,
        // per Lars 2026-09-29 ("saknar man roles så får man inte se något").
        private static void SeedRolelessAccount(string dataPath)
        {
            Directory.CreateDirectory(dataPath);

            var hasher = new PasswordHasher<MemberAccount>(Options.Create(new PasswordHasherOptions
            {
                CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV2
            }));
            var account = new MemberAccount { Login = "roleless", Roles = new List<string>() };
            account.PasswordHash = hasher.HashPassword(account, "chillin-dev-roleless");

            MemberFileStore.Save(new List<MemberAccount> { account }, Path.Combine(dataPath, "members.json"));
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

        // Found via a real Puppeteer pass (fas 11 verification, 2026-09-21): a wrong password
        // produced a browser network-error page, not the "fel login/lösenord" message. Root cause
        // was LoginSurfaceController.HandleLogin redirecting to Request.Path.Value + "?error=...",
        // i.e. back at this same [HttpPost]-only action (the real POST-to URL is
        // /umbraco/surface/LoginSurface/HandleLogin), which 405'd on the browser's follow-up GET.
        // LoginSurfaceControllerTest's unit test never caught this because it constructs the
        // controller directly and hardcodes HttpContext.Request.Path to "/login" - mirroring the
        // bug instead of exercising the real routed URL. Only a real HTTP round trip like this one
        // can catch a wrong redirect target.
        [TestMethod]
        public async Task HandleLogin_WrongPassword_RedirectsToLoginPageWithErrorMessage()
        {
            using var factory = CreateFactory(seedSuperAdminAccount: true);
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var loginPageHtml = await (await client.GetAsync("/ChalmersILLLoginPage")).Content.ReadAsStringAsync();
            var token = Regex.Match(loginPageHtml, "__RequestVerificationToken[^>]*value=\"([^\"]*)\"").Groups[1].Value;

            using var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/umbraco/surface/LoginSurface/HandleLogin")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["Login"] = "superadmin",
                    ["Password"] = "not-the-right-password",
                    ["__RequestVerificationToken"] = token
                })
            };
            var loginResponse = await client.SendAsync(loginRequest);

            Assert.AreEqual(HttpStatusCode.Found, loginResponse.StatusCode);
            var redirectUrl = loginResponse.Headers.Location.ToString();
            StringAssert.StartsWith(redirectUrl, "/ChalmersILLLoginPage");
            StringAssert.Contains(redirectUrl, "error=invalid-member");

            var errorPageResponse = await client.GetAsync(redirectUrl);
            Assert.AreEqual(HttpStatusCode.OK, errorPageResponse.StatusCode);
            var errorPageHtml = await errorPageResponse.Content.ReadAsStringAsync();
            StringAssert.Contains(errorPageHtml, "Felaktig inloggning");
        }

        // Found by manually exercising DevDataSeeder's output (fas 10, "Ordna testdata för
        // utvecklingsmiljön") against a real running app - every one of the 17 seeded orders'
        // detail view crashed with KeyNotFoundException. Chalmers.ILL.OrderItem.cshtml's log-group
        // header does Model.EventIdToEventNameMapping[eventTypeFromLastTwoCharsOfEventId] with no
        // fallback for an eventId whose type isn't one of the ~30 mapped keys - exactly what the
        // seeder produced before it was fixed to use a real event type, and exactly what would
        // happen to *any* real order whose log entries carry an eventId type outside that mapping.
        // Exercises the real login flow (CSRF token included) because this bug only shows up once
        // Razor actually renders the partial view - the controller-level test in
        // OrderItemSurfaceControllerTest only asserts on the model, never renders anything.
        [TestMethod]
        public async Task RenderOrderItem_SeededOrderWithLogItems_RendersWithoutThrowing()
        {
            using var factory = CreateFactory(seedSuperAdminAccount: true);
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            await LoginAsSuperAdminAsync(client);

            // NodeId 1 is DevDataSeeder's "01:Ny" order - status/type/reference are all set via
            // SetStatus/SetType/SetReference, each of which appends a LogItem under the same
            // eventId, so this order always has at least one grouped log entry to render.
            var response = await client.GetAsync("/OrderItemSurface/RenderOrderItem?nodeId=1");

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

            // Found via a real Puppeteer pass (fas 11 verification, 2026-09-21): the chillinVars
            // <script> block near the bottom of Chalmers.ILL.OrderItem.cshtml had a developer
            // comment that itself contained the literal text "</script>" (as an example of what
            // *data* must be escaped to avoid). The browser's HTML tokenizer has no concept of a
            // JS comment - it scans <script> content as raw text looking for that exact byte
            // sequence - so the comment closed the real tag right there, and everything meant to
            // stay inside it (the rest of the comment, eventIdToEventName/orderItemData JSON, the
            // popover/MAIL-click wiring) rendered as visible page text instead of executing,
            // ending in a JS syntax error ("Unexpected end of input") for whatever script tag
            // happened to close the resulting mess. AssertEqual(OK) alone couldn't catch this -
            // the response is still 200 with a broken body. An unbalanced <script>/</script>
            // count is a generic tripwire for exactly this class of bug, not just this instance.
            var html = await response.Content.ReadAsStringAsync();
            var openTags = Regex.Matches(html, "<script[ >]").Count;
            var closeTags = Regex.Matches(html, "</script>").Count;
            Assert.AreEqual(openTags, closeTags, "Unbalanced <script>/</script> tags - some script content is likely leaking into the visible page.");
        }

        // Found the same way as the test above: opening every tab reachable from the settings page
        // against a freshly seeded (so template-less) DataPath. Views/Partials/Settings/
        // EditTemplates.cshtml did Model.Templates.First().Data to prefill the edit textarea with
        // the first template's content - throwing InvalidOperationException ("Sequence contains no
        // elements") the moment zero templates exist, which is exactly the state of any new
        // DataPath (isolated dev, the isolated test server, or a fresh production deploy) before
        // anyone has created one. Fixed to FirstOrDefault()?.Data.
        [TestMethod]
        public async Task RenderEditTemplatesAction_NoTemplatesYet_RendersWithoutThrowing()
        {
            using var factory = CreateFactory(seedSuperAdminAccount: true);
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            await LoginAsSuperAdminAsync(client);

            var response = await client.GetAsync("/TemplatesSurface/RenderEditTemplatesAction");

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        }

        // A third find from the same sweep. OrderItemReceiveBookSurfaceController.RenderReceiveBookAction
        // reads IChillinTextRepository.ByTextField("standardTitleText") - which, per the fix noted
        // in TODO-remove-dotnet-framework.md's Städning section, returns an *empty* ChillinText
        // (StandardTitleText left at its default null) rather than throwing when that entry hasn't
        // been configured yet, exactly the state of any fresh DataPath. That null then reached
        // ChalmersILLActionReceiveBookModel.SetTitleInformation, which did
        // titleInformation.Contains(text) - ArgumentNullException for any order with real
        // TitleInformation set (i.e. every seeded order), the moment no standard title text has
        // ever been configured. Fixed by treating a null "text" the same as an empty one.
        [TestMethod]
        public async Task RenderReceiveBookAction_NoStandardTitleTextConfigured_RendersWithoutThrowing()
        {
            using var factory = CreateFactory(seedSuperAdminAccount: true);
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            await LoginAsSuperAdminAsync(client);

            var response = await client.GetAsync("/OrderItemReceiveBookSurface/RenderReceiveBookAction?nodeId=1");

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        }

        // Found via fas11-walkthrough2.js (fas 11 browser verification, 2026-09-22): every AJAX
        // action in chalmers.ill.js reads its JSON response as json.Success/json.Message (63 call
        // sites, all PascalCase - Views/Partials/*.cshtml's inline scripts are the same). ASP.NET
        // Core MVC's default Controller.Json(...) serializes with System.Text.Json's camelCase
        // policy, so every one of those responses actually came back as {"success":...,
        // "message":...} - confirmed live: the client's `if (json.Success)` branch was always
        // false, so it always ran the "failure" path (alert(undefined) - json.Message is also
        // undefined - and no view refresh), even though the server-side mutation genuinely
        // succeeded. Exactly the same risk class as the SignalR payload fix already made in fas 5
        // (see Program.cs's PayloadSerializerOptions.PropertyNamingPolicy comment) - that one
        // only looked at the hub, this is the same bug in every plain controller Json() response.
        // Fixed by giving AddControllersWithViews the matching AddJsonOptions in Program.cs.
        [TestMethod]
        public async Task AnonymizeEndpoint_JsonResponse_UsesPascalCasePropertyNames()
        {
            using var factory = CreateFactory(seedSuperAdminAccount: true);
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            await LoginAsSuperAdminAsync(client);

            using var request = new HttpRequestMessage(HttpMethod.Post, "/OrderItemAnonymizationSurface/Anonymize")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["nodeId"] = "1",
                    ["reference"] = "anonymiserad testreferens",
                    ["logsSerialized"] = "[]"
                })
            };
            var response = await client.SendAsync(request);

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            StringAssert.Contains(body, "\"Success\":true", "Response should use the PascalCase property name chalmers.ill.js actually reads (json.Success).");
            Assert.IsFalse(body.Contains("\"success\""), "Response must not use camelCase - the client never reads json.success and would silently treat this as a failure.");
        }

        // Shared by both view-rendering regression tests above - DevDataSeeder (fas 10) always
        // creates this account with SuperAdmin/Administrator/Desk, so any authenticated page is
        // reachable through it without a test needing its own members.json setup.
        private static async Task LoginAsSuperAdminAsync(HttpClient client)
        {
            await LoginAsAsync(client, "superadmin", "chillin-dev-superadmin");
        }

        private static async Task LoginAsViewerAsync(HttpClient client)
        {
            await LoginAsAsync(client, "viewer", "chillin-dev-viewer");
        }

        private static async Task LoginAsRolelessAsync(HttpClient client)
        {
            await LoginAsAsync(client, "roleless", "chillin-dev-roleless");
        }

        private static async Task LoginAsAsync(HttpClient client, string login, string password)
        {
            var loginPageHtml = await (await client.GetAsync("/ChalmersILLLoginPage")).Content.ReadAsStringAsync();
            var token = Regex.Match(loginPageHtml, "__RequestVerificationToken[^>]*value=\"([^\"]*)\"").Groups[1].Value;

            using var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/umbraco/surface/LoginSurface/HandleLogin")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["Login"] = login,
                    ["Password"] = password,
                    ["__RequestVerificationToken"] = token
                })
            };
            var loginResponse = await client.SendAsync(loginRequest);
            Assert.AreEqual(HttpStatusCode.Found, loginResponse.StatusCode, $"Seeded login for '{login}' failed.");
        }

        // An account explicitly assigned the "Viewer" role can still reach the order list - the
        // restriction is read-only, not no-access.
        [TestMethod]
        public async Task Viewer_CanStillViewTheOrderList()
        {
            using var factory = CreateFactory(seedViewerAccount: true);
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            await LoginAsViewerAsync(client);

            var response = await client.GetAsync("/bestaellningar/");

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        }

        // The actual fix: a Viewer must NOT be able to write a log entry - a regular [HttpPost]
        // write action with no [AllowViewer]. Asserts on the actual data rather than a specific
        // status code: cookie authentication's default Forbid() behaviour is a 302 redirect to its
        // (unconfigured, so 404ing) AccessDeniedPath, not a bare 403 - a pre-existing quirk shared
        // with the SuperAdmin-only gate, and orthogonal to the one thing that actually matters
        // here, which is that the write itself must never execute.
        [TestMethod]
        public async Task Viewer_BlockedFromWritingALogItem()
        {
            using var factory = CreateFactory(seedViewerAccount: true);
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            await LoginAsViewerAsync(client);

            using var request = new HttpRequestMessage(HttpMethod.Post, "/LogItemSurface/WriteLogItem")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["nodeId"] = "1",
                    ["Type"] = "KOMMENTAR",
                    ["Message"] = "Should never be written by a Viewer.",
                    ["newFollowUpDate"] = "",
                    ["statusId"] = "-1",
                    ["cancellationReasonId"] = "-1",
                    ["purchasedMaterialId"] = "-1"
                })
            };
            var response = await client.SendAsync(request);

            Assert.AreNotEqual(HttpStatusCode.OK, response.StatusCode);
            var logItemsJson = await (await client.GetAsync("/LogItemSurface/GetLogItems?nodeId=1")).Content.ReadAsStringAsync();
            Assert.IsFalse(logItemsJson.Contains("Should never be written by a Viewer."), "The write must never have executed.");
        }

        // The sneakiest category found while designing this fix: several mutating actions are
        // exposed over [HttpGet] instead of [HttpPost] (pre-existing, not something this fix
        // changes) - a role check that only looked at the HTTP verb would have missed them and
        // left a Viewer able to mutate order data via a plain link/GET. ViewerReadOnlyFilter checks
        // per-action [AllowViewer] instead, so this must be blocked exactly like the POST case above.
        [TestMethod]
        public async Task Viewer_BlockedFromAGetVerbMutation()
        {
            using var factory = CreateFactory(seedViewerAccount: true);
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            await LoginAsViewerAsync(client);

            var response = await client.GetAsync("/OrderItemProviderSurface/SetProvider?nodeId=1&providerName=Should-never-be-set&providerOrderId=&providerInformation=&newFollowUpDate=&updateStatusAndFollowUpDate=false");

            Assert.AreNotEqual(HttpStatusCode.OK, response.StatusCode);
            var orderItemJson = await (await client.GetAsync("/OrderItemSurface/GetOrderItem?nodeId=1")).Content.ReadAsStringAsync();
            Assert.IsFalse(orderItemJson.Contains("Should-never-be-set"), "The write must never have executed.");
        }

        // The exact scenario the user reported: a logged-in account with zero roles at all could
        // reach the order list (and every write endpoint) before ViewerReadOnlyFilter existed - the
        // global AuthorizeFilter only ever checked "logged in", never roles. Per Lars 2026-09-29
        // ("saknar man roles så får man inte se något"), such an account is now stricter than an
        // explicit Viewer: blocked from everything, order list included.
        [TestMethod]
        public async Task RolelessAccount_CannotViewTheOrderList()
        {
            using var factory = CreateFactory(seedRolelessAccount: true);
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            await LoginAsRolelessAsync(client);

            var response = await client.GetAsync("/bestaellningar/");

            Assert.AreNotEqual(HttpStatusCode.OK, response.StatusCode);
        }

        // Regression guard: an account with a real role must be completely unaffected by
        // ViewerReadOnlyFilter, exactly like before this fix existed.
        [TestMethod]
        public async Task SuperAdmin_StillAllowedToWriteALogItem()
        {
            using var factory = CreateFactory(seedSuperAdminAccount: true);
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            await LoginAsSuperAdminAsync(client);

            using var request = new HttpRequestMessage(HttpMethod.Post, "/LogItemSurface/WriteLogItem")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["nodeId"] = "1",
                    ["Type"] = "KOMMENTAR",
                    ["Message"] = "A real role must still be able to write.",
                    ["newFollowUpDate"] = "",
                    ["statusId"] = "-1",
                    ["cancellationReasonId"] = "-1",
                    ["purchasedMaterialId"] = "-1"
                })
            };
            var response = await client.SendAsync(request);

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

        // Fas 10, "Gör sökvägarna till members.json och chillinPrevalues.json konfigurerbara":
        // Bootstrapper.RegisterTypes registers FileMembershipProvider/FileRoleProvider with a
        // factory bound to Chillin:DataPath's members.json (fas 6, isolerat läge steg A) - but
        // Program.cs *also* had bare `builder.Services.AddSingleton<FileMembershipProvider>()`/
        // `<FileRoleProvider>()` calls left over from fas 2/3, registered *after* Bootstrapper's.
        // Since DI resolves the last registration for a type, those shadowed the correctly
        // configured ones, silently falling back to each class's now-removed parameterless
        // constructor - which read members.json from AppDomain.CurrentDomain.BaseDirectory, not
        // DataPath. This resolves the actual DI-wired instance and proves it reads from the
        // configured DataPath, not that stale location.
        [TestMethod]
        public void FileMembershipProvider_ResolvedFromDI_ReadsAccountsFromConfiguredDataPath()
        {
            var dataPath = Path.Combine(Path.GetTempPath(), "chillin-isolated-membership-" + System.Guid.NewGuid());
            Directory.CreateDirectory(dataPath);

            var hasher = new PasswordHasher<MemberAccount>(Options.Create(new PasswordHasherOptions
            {
                CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV2
            }));
            var account = new MemberAccount { Login = "diagnostic-user", Roles = new List<string> { "Desk" } };
            account.PasswordHash = hasher.HashPassword(account, "correct-password");
            File.WriteAllText(Path.Combine(dataPath, "members.json"), JsonConvert.SerializeObject(new List<MemberAccount> { account }));

            using var factory = CreateFactory(new Dictionary<string, string> { ["Chillin:DataPath"] = dataPath });
            var provider = factory.Services.GetRequiredService<FileMembershipProvider>();

            Assert.IsTrue(provider.ValidateUser("diagnostic-user", "correct-password"));
        }
    }
}
