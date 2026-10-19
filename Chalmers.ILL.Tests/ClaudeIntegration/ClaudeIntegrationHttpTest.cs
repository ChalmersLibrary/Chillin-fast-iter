using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Chalmers.ILL.Members;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chalmers.ILL.Tests.ClaudeIntegration
{
    // The integration end to end through the real pipeline in isolated mode - i.e. exactly what the
    // fast test server runs: pages behind Chillin's login, the Claudeborgen roles, FakeClaude doing
    // the rounds in the background, the changelog on the start page, and the agent API closed.
    // Same WebApplicationFactory/env-var setup as IsolatedModeSmokeTest (see the comment there on
    // why config has to go through environment variables).
    [TestClass]
    public class ClaudeIntegrationHttpTest
    {
        private static readonly (string Login, string Password, string[] Roles)[] Accounts =
        {
            ("agneta", "pw-agneta", new[] { "ObjektSpecialist" }),
            ("ivar", "pw-ivar", new[] { "ObjektSpecialistIT" }),
            ("viewer", "pw-viewer", new[] { "Viewer" }),
        };

        private static WebApplicationFactory<Program> CreateFactory()
        {
            var dataPath = Path.Combine(Path.GetTempPath(), "chillin-claude-http-" + Guid.NewGuid());
            SeedAccounts(dataPath);

            var config = new Dictionary<string, string>
            {
                ["Chillin:Isolated"] = "true",
                ["Chillin:DataPath"] = dataPath,
                ["Chillin:BaseUrl"] = "http://localhost/",
                ["ClaudeIntegration:FakePhaseDelayMilliseconds"] = "0",
            };

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
                _ = factory.Server;
                return factory;
            }
            finally
            {
                foreach (var kvp in previousValues)
                    Environment.SetEnvironmentVariable(kvp.Key, kvp.Value);
            }
        }

        private static void SeedAccounts(string dataPath)
        {
            Directory.CreateDirectory(dataPath);
            var hasher = new PasswordHasher<MemberAccount>(Options.Create(new PasswordHasherOptions
            {
                CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV2
            }));

            var accounts = new List<MemberAccount>();
            foreach (var (login, password, roles) in Accounts)
            {
                var account = new MemberAccount { Login = login, Roles = new List<string>(roles) };
                account.PasswordHash = hasher.HashPassword(account, password);
                accounts.Add(account);
            }

            MemberFileStore.Save(accounts, Path.Combine(dataPath, "members.json"));
        }

        private static async Task<HttpClient> LoginAsync(WebApplicationFactory<Program> factory, string login)
        {
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            var password = Array.Find(Accounts, a => a.Login == login).Password;

            var token = AntiForgeryToken(await (await client.GetAsync("/ChalmersILLLoginPage")).Content.ReadAsStringAsync());
            var response = await client.PostAsync("/umbraco/surface/LoginSurface/HandleLogin", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Login"] = login,
                ["Password"] = password,
                ["__RequestVerificationToken"] = token
            }));
            Assert.AreEqual(HttpStatusCode.Found, response.StatusCode, $"Login for '{login}' failed.");
            return client;
        }

        // Razor HTML-encodes non-ASCII in @-expressions (å/ä/ö -> &#xE5; ...), fine in a browser.
        private static async Task<string> GetHtmlAsync(HttpClient client, string url) =>
            WebUtility.HtmlDecode(await (await client.GetAsync(url)).Content.ReadAsStringAsync());

        private static string AntiForgeryToken(string html) =>
            Regex.Match(html, "__RequestVerificationToken[^>]*value=\"([^\"]*)\"").Groups[1].Value;

        private static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string pageUrl, string postUrl, Dictionary<string, string> fields)
        {
            var html = await (await client.GetAsync(pageUrl)).Content.ReadAsStringAsync();
            fields["__RequestVerificationToken"] = AntiForgeryToken(html);
            return await client.PostAsync(postUrl, new FormUrlEncodedContent(fields));
        }

        private static async Task<string> WaitForStateAsync(HttpClient client, int id, string state)
        {
            var stopwatch = Stopwatch.StartNew();
            string html = null;
            while (stopwatch.Elapsed < TimeSpan.FromSeconds(30))
            {
                html = await GetHtmlAsync(client, $"/Claude/ConversationContent/{id}");
                if (html.Contains($"data-state=\"{state}\""))
                    return html;
                await Task.Delay(200);
            }

            Assert.Fail($"Conversation {id} never reached {state}. Last content:\n{html}");
            return null;
        }

        [TestMethod]
        public async Task ObjectOwner_StartsAConversation_FakeClaudeDelivers_AndDoneLandsInTheChangelog()
        {
            using var factory = CreateFactory();
            using var client = await LoginAsync(factory, "agneta");

            var start = await PostFormAsync(client, "/Claude", "/Claude/Start", new Dictionary<string, string>
            {
                ["title"] = "Exportknapp",
                ["body"] = "Lägg till en exportknapp på listan",
            });
            Assert.AreEqual(HttpStatusCode.Redirect, start.StatusCode);
            var conversationUrl = start.Headers.Location.ToString();
            var id = int.Parse(Regex.Match(conversationUrl, @"/Claude/Conversation/(\d+)").Groups[1].Value);

            var delivered = await WaitForStateAsync(client, id, "WaitingForOwner");
            StringAssert.Contains(delivered, "Fejkad Claude");
            StringAssert.Contains(delivered, "Gröna tester");

            var done = await PostFormAsync(client, conversationUrl, $"/Claude/Done/{id}", new Dictionary<string, string>
            {
                ["changelogText"] = "Exportknapp på beställningslistan",
            });
            Assert.AreEqual(HttpStatusCode.Redirect, done.StatusCode);

            var startPage = await GetHtmlAsync(client, "/");
            StringAssert.Contains(startPage, "Exportknapp på beställningslistan");
            StringAssert.Contains(startPage, "data-testid=\"claude-changelog-entry\"");
        }

        [TestMethod]
        public async Task ItSpecialist_CanWriteInAConversation_ButNotStartOne()
        {
            using var factory = CreateFactory();
            using var owner = await LoginAsync(factory, "agneta");
            var start = await PostFormAsync(owner, "/Claude", "/Claude/Start", new Dictionary<string, string> { ["title"] = "Ett", ["body"] = "a" });
            var conversationUrl = start.Headers.Location.ToString();
            var id = int.Parse(Regex.Match(conversationUrl, @"(\d+)$").Groups[1].Value);
            await WaitForStateAsync(owner, id, "WaitingForOwner");

            using var it = await LoginAsync(factory, "ivar");
            var indexHtml = await (await it.GetAsync("/Claude")).Content.ReadAsStringAsync();
            Assert.IsFalse(indexHtml.Contains("claude-start-form"), "IT must not get the start form.");

            var reply = await PostFormAsync(it, conversationUrl, $"/Claude/Reply/{id}", new Dictionary<string, string> { ["body"] = "Gör den grön också" });
            Assert.AreEqual(HttpStatusCode.Redirect, reply.StatusCode);
            StringAssert.Contains(reply.Headers.Location.ToString(), $"/Claude/Conversation/{id}");

            var blockedStart = await PostFormAsync(it, conversationUrl, "/Claude/Start", new Dictionary<string, string> { ["title"] = "Två", ["body"] = "b" });
            StringAssert.Contains(blockedStart.Headers.Location?.ToString() ?? "", "AccessDenied");
        }

        [TestMethod]
        public async Task Viewer_CanReadEverything_ButWriteNothing()
        {
            using var factory = CreateFactory();
            using var owner = await LoginAsync(factory, "agneta");
            var start = await PostFormAsync(owner, "/Claude", "/Claude/Start", new Dictionary<string, string> { ["title"] = "Synligt för alla", ["body"] = "a" });
            var conversationUrl = start.Headers.Location.ToString();
            var id = int.Parse(Regex.Match(conversationUrl, @"(\d+)$").Groups[1].Value);

            using var viewer = await LoginAsync(factory, "viewer");
            var index = await viewer.GetAsync("/Claude");
            Assert.AreEqual(HttpStatusCode.OK, index.StatusCode);
            var indexHtml = WebUtility.HtmlDecode(await index.Content.ReadAsStringAsync());
            StringAssert.Contains(indexHtml, "Synligt för alla");
            Assert.IsFalse(indexHtml.Contains("claude-start-form"));

            var conversation = await viewer.GetAsync(conversationUrl);
            Assert.AreEqual(HttpStatusCode.OK, conversation.StatusCode);
            Assert.IsFalse((await conversation.Content.ReadAsStringAsync()).Contains("claude-reply-form"));

            // The viewer's pages have no form, hence no antiforgery token - take one from the login page
            // (rendered for the logged-in viewer), so this exercises the role check, not the token check.
            var reply = await PostFormAsync(viewer, "/ChalmersILLLoginPage", $"/Claude/Reply/{id}", new Dictionary<string, string> { ["body"] = "Får inte skrivas" });
            StringAssert.Contains(reply.Headers.Location?.ToString() ?? "", "AccessDenied");
            var after = await GetHtmlAsync(viewer, $"/Claude/ConversationContent/{id}");
            Assert.IsFalse(after.Contains("Får inte skrivas"));
        }

        // Claude's status belongs on the integration's own pages, not on every page in Chillin
        // (Lars 2026-10-19) - elsewhere there's only a plain menu link.
        [TestMethod]
        public async Task Status_IsOnlyShownOnTheIntegrationPages()
        {
            using var factory = CreateFactory();
            using var viewer = await LoginAsync(factory, "viewer");

            var orderList = await GetHtmlAsync(viewer, "/bestaellningar/");
            Assert.IsFalse(orderList.Contains("data-testid=\"claude-status\""));
            Assert.IsFalse(orderList.Contains("/scripts/claude.js"));
            StringAssert.Contains(orderList, "href=\"/Claude\"");

            var claudePage = await GetHtmlAsync(viewer, "/Claude");
            StringAssert.Contains(claudePage, "data-testid=\"claude-status\"");
            StringAssert.Contains(claudePage, "/scripts/claude.js");
        }

        [TestMethod]
        public async Task ObjectOwnerRoleAlone_GrantsNoWriteAccessToOrders()
        {
            using var factory = CreateFactory();
            using var owner = await LoginAsync(factory, "agneta");

            var response = await owner.PostAsync("/LogItemSurface/WriteLogItem", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["nodeId"] = "1",
                ["Type"] = "KOMMENTAR",
                ["Message"] = "Should never be written by ObjektSpecialist alone.",
                ["newFollowUpDate"] = "",
                ["statusId"] = "-1",
                ["cancellationReasonId"] = "-1",
                ["purchasedMaterialId"] = "-1"
            }));

            Assert.AreNotEqual(HttpStatusCode.OK, response.StatusCode);
            var logItemsJson = await (await owner.GetAsync("/LogItemSurface/GetLogItems?nodeId=1")).Content.ReadAsStringAsync();
            Assert.IsFalse(logItemsJson.Contains("Should never be written by ObjektSpecialist alone."));
        }

        [TestMethod]
        public async Task AgentApi_IsClosedInIsolatedMode()
        {
            using var factory = CreateFactory();
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var request = new HttpRequestMessage(HttpMethod.Get, "/api/claude/work");
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer whatever");
            var response = await client.SendAsync(request);

            Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        }

        [TestMethod]
        public async Task ClaudePages_RequireLogin()
        {
            using var factory = CreateFactory();
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var response = await client.GetAsync("/Claude");

            Assert.AreEqual(HttpStatusCode.Redirect, response.StatusCode);
            StringAssert.Contains(response.Headers.Location.ToString(), "ChalmersILLLoginPage");
        }

        // Characterization of the start page's existing changelog - the integration's entries are
        // added above it, the hand-written version history must stay.
        [TestMethod]
        public async Task StartPage_KeepsTheHandWrittenChangelog()
        {
            using var factory = CreateFactory();
            using var viewer = await LoginAsync(factory, "viewer");

            var html = await (await viewer.GetAsync("/")).Content.ReadAsStringAsync();

            StringAssert.Contains(html, "Nytt i Chillin");
            StringAssert.Contains(html, "2025-09-09 - v2.6.3");
            StringAssert.Contains(html, "2022-10-20 - v2.1.0");
        }
    }
}
