using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Chalmers.ILL.Members;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chalmers.ILL.Tests.Claudeborgen
{
    // The integration end to end through the real pipeline in isolated mode - i.e. exactly what the
    // fast test server runs: pages behind Chillin's login, the Claudeborgen roles, FakeClaude doing
    // the rounds in the background, the changelog on the start page, and the agent API closed.
    // Same WebApplicationFactory/env-var setup as IsolatedModeSmokeTest (see the comment there on
    // why config has to go through environment variables).
    [TestClass]
    public class ClaudeborgenInChillinTest
    {
        private static readonly (string Login, string Password, string[] Roles)[] Accounts =
        {
            ("agneta", "pw-agneta", new[] { "ObjektSpecialist" }),
            ("ivar", "pw-ivar", new[] { "ObjektSpecialistIT" }),
            ("viewer", "pw-viewer", new[] { "Viewer" }),
        };

        private static WebApplicationFactory<Program> CreateFactory(IDictionary<string, string> extraConfig = null)
        {
            var dataPath = Path.Combine(Path.GetTempPath(), "chillin-claudeborgen-http-" + Guid.NewGuid());
            SeedAccounts(dataPath);

            var config = new Dictionary<string, string>
            {
                ["Chillin:Isolated"] = "true",
                ["Chillin:DataPath"] = dataPath,
                ["Chillin:BaseUrl"] = "http://localhost/",
                ["Claudeborgen:FakePhaseDelayMilliseconds"] = "0",
                ["Claudeborgen:FakeDeployDelayMilliseconds"] = "0",
            };
            foreach (var kvp in extraConfig ?? new Dictionary<string, string>())
                config[kvp.Key] = kvp.Value;

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

            var start = await PostFormAsync(client, "/", "/Claude/Start", new Dictionary<string, string>
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

        // DESIGN.md, "Idéer": bounce an idea on the start page, then turn it into work.
        [TestMethod]
        public async Task Idea_ChatOnTheStartPage_ThenBecomesWork()
        {
            using var factory = CreateFactory();
            using var client = await LoginAsync(factory, "agneta");

            var startPage = await GetHtmlAsync(client, "/");
            StringAssert.Contains(startPage, "Bolla en idé med Claude");

            var posted = await PostFormAsync(client, "/", "/Claude/Idea", new Dictionary<string, string> { ["body"] = "Tänk om man kunde exportera listan" });
            Assert.AreEqual(HttpStatusCode.Redirect, posted.StatusCode);
            var ideaUrl = posted.Headers.Location.ToString();
            StringAssert.StartsWith(ideaUrl, "/?idea=");
            var id = int.Parse(Regex.Match(ideaUrl, @"idea=(\d+)").Groups[1].Value);

            var stopwatch = Stopwatch.StartNew();
            string chat = null;
            while (stopwatch.Elapsed < TimeSpan.FromSeconds(30))
            {
                chat = await GetHtmlAsync(client, $"/Claude/IdeaChat/{id}");
                if (chat.Contains("data-state=\"WaitingForOwner\""))
                    break;
                await Task.Delay(200);
            }
            StringAssert.Contains(chat, "Tänk om man kunde exportera listan");
            StringAssert.Contains(chat, "Fejkad Claude");

            var page = await GetHtmlAsync(client, ideaUrl);
            StringAssert.Contains(page, "Gå vidare");
            StringAssert.Contains(page, "Rensa diskussion");
            Assert.AreEqual(HttpStatusCode.Redirect, (await client.GetAsync($"/Claude/Conversation/{id}")).StatusCode, "Ideas are chatted on the start page.");

            // "Gå vidare" - no title to fill in: it's made from the first message.
            var converted = await PostFormAsync(client, ideaUrl, $"/Claude/Convert/{id}", new Dictionary<string, string>());
            StringAssert.Contains(converted.Headers.Location.ToString(), $"/Claude/Conversation/{id}");

            var delivered = await WaitForStateAsync(client, id, "WaitingForOwner");
            StringAssert.Contains(delivered, "Tänk om man kunde exportera listan");
            StringAssert.Contains(delivered, "Gröna tester");
        }

        [TestMethod]
        public async Task Viewer_SeesIdeas_ButCannotStartOne()
        {
            using var factory = CreateFactory();
            using var owner = await LoginAsync(factory, "agneta");
            await PostFormAsync(owner, "/", "/Claude/Idea", new Dictionary<string, string> { ["body"] = "En idé alla kan se" });

            using var viewer = await LoginAsync(factory, "viewer");
            var html = await GetHtmlAsync(viewer, "/");
            StringAssert.Contains(html, "En idé alla kan se");
            Assert.IsFalse(html.Contains("claude-idea-form"));

            var refused = await PostFormAsync(viewer, "/ChalmersILLLoginPage", "/Claude/Idea", new Dictionary<string, string> { ["body"] = "Får inte" });
            StringAssert.Contains(refused.Headers.Location?.ToString() ?? "", "AccessDenied");
        }

        // Claude asks instead of guessing: the conversation shows as a question, on the start page
        // and in the conversation itself.
        [TestMethod]
        public async Task ClaudeAsks_TheQuestionIsVisible_AndAnAnswerContinues()
        {
            using var factory = CreateFactory();
            using var client = await LoginAsync(factory, "agneta");
            var start = await PostFormAsync(client, "/", "/Claude/Start", new Dictionary<string, string> { ["title"] = "Knapp", ["body"] = "Lägg till en knapp [fråga]" });
            var conversationUrl = start.Headers.Location.ToString();
            var id = int.Parse(Regex.Match(conversationUrl, @"(\d+)$").Groups[1].Value);

            var asked = await WaitForStateAsync(client, id, "QuestionForOwner");
            StringAssert.Contains(asked, "Claude har en fråga");
            StringAssert.Contains(asked, "Claude frågar");
            StringAssert.Contains(asked, ">Frågade<");
            Assert.IsFalse(asked.Contains("Tester körs"), "A question round has no commit, so nothing to test.");
            StringAssert.Contains(await GetHtmlAsync(client, "/Claude/IndexContent"), "Claude har en fråga som väntar på svar.");

            await PostFormAsync(client, conversationUrl, $"/Claude/Reply/{id}", new Dictionary<string, string> { ["body"] = "Överst" });
            await WaitForStateAsync(client, id, "WaitingForOwner");
        }

        // The account's quota is used up: Claude shows as paused on the start page, the
        // conversation waits in the queue, nothing is flagged for IT.
        [TestMethod]
        public async Task QuotaUsedUp_StartPageShowsClaudePaused()
        {
            using var factory = CreateFactory();
            using var client = await LoginAsync(factory, "agneta");
            await PostFormAsync(client, "/", "/Claude/Start", new Dictionary<string, string> { ["title"] = "Ett", ["body"] = "Gör något [kvot]" });

            var stopwatch = Stopwatch.StartNew();
            string html = null;
            while (stopwatch.Elapsed < TimeSpan.FromSeconds(30))
            {
                html = await GetHtmlAsync(client, "/Claude/IndexContent");
                if (html.Contains("data-activity=\"Paused\""))
                    break;
                await Task.Delay(200);
            }

            StringAssert.Contains(html, "Claude är pausad, försöker igen kl.");
            StringAssert.Contains(html, "kvoten är slut");
            Assert.IsFalse(html.Contains("claude-attention"), "A used-up quota is not something for IT.");
        }

        [TestMethod]
        public async Task ItSpecialist_CanWriteInAConversation_ButNotStartOne()
        {
            using var factory = CreateFactory();
            using var owner = await LoginAsync(factory, "agneta");
            var start = await PostFormAsync(owner, "/", "/Claude/Start", new Dictionary<string, string> { ["title"] = "Ett", ["body"] = "a" });
            var conversationUrl = start.Headers.Location.ToString();
            var id = int.Parse(Regex.Match(conversationUrl, @"(\d+)$").Groups[1].Value);
            await WaitForStateAsync(owner, id, "WaitingForOwner");

            using var it = await LoginAsync(factory, "ivar");
            var indexHtml = await GetHtmlAsync(it, "/");
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
            var start = await PostFormAsync(owner, "/", "/Claude/Start", new Dictionary<string, string> { ["title"] = "Synligt för alla", ["body"] = "a" });
            var conversationUrl = start.Headers.Location.ToString();
            var id = int.Parse(Regex.Match(conversationUrl, @"(\d+)$").Groups[1].Value);

            using var viewer = await LoginAsync(factory, "viewer");
            var index = await viewer.GetAsync("/");
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

        // The integration lives on the start page next to the changelog (Lars 2026-10-19) - not
        // on every page, and not behind a menu link of its own.
        [TestMethod]
        public async Task Integration_IsOnTheStartPage_AndNowhereElse()
        {
            using var factory = CreateFactory();
            using var viewer = await LoginAsync(factory, "viewer");

            var orderList = await GetHtmlAsync(viewer, "/bestaellningar/");
            Assert.IsFalse(orderList.Contains("data-testid=\"claude-status\""));
            Assert.IsFalse(orderList.Contains("/_content/Claudeborgen.Web/claude.js"));
            Assert.IsFalse(orderList.Contains("href=\"/Claude\""));

            var startPage = await GetHtmlAsync(viewer, "/");
            StringAssert.Contains(startPage, "data-testid=\"claude-section\"");
            StringAssert.Contains(startPage, "data-testid=\"claude-status\"");
            StringAssert.Contains(startPage, "/_content/Claudeborgen.Web/claude.js?v=");
            Assert.IsTrue(startPage.IndexOf("claude-section") < startPage.IndexOf("Nytt i Chillin"), "The Claude section sits right above the changelog.");

            var claude = await viewer.GetAsync("/Claude");
            Assert.AreEqual(HttpStatusCode.Redirect, claude.StatusCode);
            Assert.AreEqual("/", claude.Headers.Location.ToString());
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

        // The fast test server can open the agent API to test the runner itself - but only for a
        // runner in --fake mode; a real runner would push and redeploy this very server.
        [TestMethod]
        public async Task FakeRunnerMode_OpensTheAgentApi_OnlyToAFakeRunner()
        {
            var token = "snabbtest-token";
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));
            using var factory = CreateFactory(new Dictionary<string, string>
            {
                ["Claudeborgen:IsolatedMode"] = "FakeRunner",
                ["Claudeborgen:AgentTokenSha256"] = hash,
            });
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var real = new HttpRequestMessage(HttpMethod.Get, "/api/claude/work");
            real.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
            Assert.AreEqual(HttpStatusCode.Forbidden, (await client.SendAsync(real)).StatusCode);

            var fake = new HttpRequestMessage(HttpMethod.Get, "/api/claude/work");
            fake.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
            fake.Headers.TryAddWithoutValidation("X-Claudeborgen-Fake-Runner", "true");
            Assert.AreEqual(HttpStatusCode.OK, (await client.SendAsync(fake)).StatusCode);
        }

        [TestMethod]
        public async Task ClaudePages_RequireLogin()
        {
            using var factory = CreateFactory();
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            foreach (var url in new[] { "/Claude", "/Claude/Conversation/1", "/Claude/IndexContent" })
            {
                var response = await client.GetAsync(url);

                Assert.AreEqual(HttpStatusCode.Redirect, response.StatusCode, url);
                StringAssert.Contains(response.Headers.Location.ToString(), "ChalmersILLLoginPage", url);
            }
        }

        // The fast test server tells the runner which commit it runs - without a login, since the
        // runner doesn't log in to the app. Chillin's build stamps the commit (claudeborgen's
        // build/Claudeborgen.Web.targets, imported in Chalmers.ILL.csproj).
        [TestMethod]
        public async Task Version_IsOpenWithoutLogin_AndIsTheBuiltCommit()
        {
            using var factory = CreateFactory();
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var response = await client.GetAsync("/claudeborgen/version");

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            var commit = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("commit").GetString();
            StringAssert.Matches(commit, new Regex("^[0-9a-f]{40}$"), "The git commit of this build.");
        }

        [TestMethod]
        public async Task DeliveredChange_ShowsWhenItIsOutOnTheFastTestServer()
        {
            using var factory = CreateFactory();
            using var client = await LoginAsync(factory, "agneta");
            var start = await PostFormAsync(client, "/", "/Claude/Start", new Dictionary<string, string> { ["title"] = "Ett", ["body"] = "Gör något" });
            var id = int.Parse(Regex.Match(start.Headers.Location.ToString(), @"(\d+)$").Groups[1].Value);

            var stopwatch = Stopwatch.StartNew();
            string html = null;
            while (stopwatch.Elapsed < TimeSpan.FromSeconds(30))
            {
                html = await GetHtmlAsync(client, $"/Claude/ConversationContent/{id}");
                if (html.Contains("Ändringen är ute på snabbtestservern"))
                    break;
                await Task.Delay(200);
            }

            StringAssert.Contains(html, "Ändringen är ute på snabbtestservern och går att prova.");
            Assert.IsFalse(html.Contains("Driftsätts…"));
        }

        // Chalmers.ILL/changelog.json is edited by hand and appended to by the runner - it must
        // always parse, or the start page breaks on every server it reaches.
        [TestMethod]
        public void ChangelogFile_InTheRepo_Parses_AndHasTheHistory()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "changelog.json");
            var entries = global::Claudeborgen.ChangelogFile.Load(path);

            Assert.IsTrue(entries.Count >= 10, $"Expected the version history in {path}.");
            Assert.IsTrue(entries.All(e => System.Text.RegularExpressions.Regex.IsMatch(e.Date ?? "", @"^\d{4}-\d{2}-\d{2}$")), "Every entry needs a yyyy-MM-dd date.");
            Assert.IsTrue(entries.All(e => e.Items.Count > 0), "Every entry needs at least one item.");
        }

        [TestMethod]
        public async Task DoneOnTheFastTestServer_ShowsInTheChangelogAsFake_InDateOrder()
        {
            using var factory = CreateFactory();
            using var client = await LoginAsync(factory, "agneta");
            var start = await PostFormAsync(client, "/", "/Claude/Start", new Dictionary<string, string> { ["title"] = "Exportknapp", ["body"] = "a" });
            var conversationUrl = start.Headers.Location.ToString();
            var id = int.Parse(Regex.Match(conversationUrl, @"(\d+)$").Groups[1].Value);
            await WaitForStateAsync(client, id, "WaitingForOwner");
            await PostFormAsync(client, conversationUrl, $"/Claude/Done/{id}", new Dictionary<string, string> { ["changelogText"] = "Exportknapp på listan" });

            // The fake "commits" the entry, and the conversation is then removed like on a real
            // server - only the changelog line is left, without a link.
            var stopwatch = Stopwatch.StartNew();
            while ((await client.GetAsync($"/Claude/ConversationContent/{id}")).StatusCode != HttpStatusCode.NotFound)
            {
                Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(30), "The conversation was never removed.");
                await Task.Delay(200);
            }

            var html = await GetHtmlAsync(client, "/");
            Assert.IsFalse(html.Contains($"/Claude/Conversation/{id}\""), "Nothing to link to any more.");

            var claudeEntry = html.IndexOf("Exportknapp på listan");
            Assert.IsTrue(claudeEntry > 0);
            Assert.IsTrue(claudeEntry < html.IndexOf("2025-09-09 - v2.6.3"), "Newest first: today's entry above the 2025 one.");
            StringAssert.Contains(html, "(fejk)");
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
