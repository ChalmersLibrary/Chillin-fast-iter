using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Claudeborgen.Tests
{
    // The agent API as the runner sees it - claudeborgen/PROTOCOL.md, checked over real HTTP through
    // a minimal host that knows nothing about Chillin. If this passes, any runner that follows the
    // protocol works against this package, whatever app hosts it.
    [TestClass]
    public class AgentApiProtocolTest
    {
        private const string Token = "hemlig-agent-token";
        private static readonly string TokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Token)));

        private string _dataDirectory;
        private WebApplication _app;
        private HttpClient _client;
        private ClaudeborgenService _service;

        [TestCleanup]
        public async Task Cleanup()
        {
            _client?.Dispose();
            if (_app != null)
                await _app.DisposeAsync();
            if (_dataDirectory != null && Directory.Exists(_dataDirectory))
                Directory.Delete(_dataDirectory, true);
        }

        private async Task StartAsync(ClaudeborgenMode mode, bool sendToken = true, bool declareFake = false)
        {
            _dataDirectory = Path.Combine(Path.GetTempPath(), "claudeborgen-protocol-" + Guid.NewGuid());
            var settings = new ClaudeborgenSettings { Mode = mode, DataDirectory = _dataDirectory, AgentTokenSha256 = TokenHash, MaxFixAttempts = 2 };

            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddControllersWithViews().AddApplicationPart(typeof(ClaudeAgentApiController).Assembly);
            builder.Services.AddSignalR();
            builder.Services.AddAuthentication();
            builder.Services.AddAuthorization();
            _service = builder.Services.AddClaudeborgen(settings);
            builder.Services.AddSingleton<IClaudeborgenHost>(new NoPagesHost());

            _app = builder.Build();
            _app.UseRouting();
            _app.UseAuthentication();
            _app.UseAuthorization();
            _app.MapControllers();
            _app.MapClaudeborgen();
            await _app.StartAsync();

            _client = _app.GetTestClient();
            if (sendToken)
                _client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Bearer " + Token);
            if (declareFake)
                _client.DefaultRequestHeaders.TryAddWithoutValidation(ClaudeborgenSettings.FakeRunnerHeader, "true");
        }

        private async Task<HttpResponseMessage> PostAsync(string path, object body = null) =>
            await _client.PostAsync(path, body == null ? null : new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"));

        private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
            JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        [TestMethod]
        public async Task Info_ReportsProtocolVersionAndMode()
        {
            await StartAsync(ClaudeborgenMode.Agent);

            var info = await JsonAsync(await _client.GetAsync("/api/claude/info"));

            Assert.AreEqual(1, info.GetProperty("protocolVersion").GetInt32());
            Assert.AreEqual("Agent", info.GetProperty("mode").GetString());
        }

        [TestMethod]
        public async Task MissingOrWrongToken_Is401()
        {
            await StartAsync(ClaudeborgenMode.Agent, sendToken: false);

            Assert.AreEqual(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/claude/work")).StatusCode);

            _client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Bearer fel");
            var response = await _client.GetAsync("/api/claude/work");
            Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.IsFalse(string.IsNullOrEmpty((await JsonAsync(response)).GetProperty("message").GetString()));
        }

        [TestMethod]
        public async Task ClosedModes_Are404()
        {
            await StartAsync(ClaudeborgenMode.InAppFake);
            Assert.AreEqual(HttpStatusCode.NotFound, (await _client.GetAsync("/api/claude/work")).StatusCode);
        }

        [TestMethod]
        public async Task Off_Is404()
        {
            await StartAsync(ClaudeborgenMode.Off);
            Assert.AreEqual(HttpStatusCode.NotFound, (await _client.GetAsync("/api/claude/work")).StatusCode);
        }

        [TestMethod]
        public async Task FakeRunnerMode_RefusesARunnerThatDoesNotDeclareFake()
        {
            await StartAsync(ClaudeborgenMode.FakeRunner);
            Assert.AreEqual(HttpStatusCode.Forbidden, (await _client.GetAsync("/api/claude/work")).StatusCode);
        }

        [TestMethod]
        public async Task FakeRunnerMode_AcceptsAFakeRunner()
        {
            await StartAsync(ClaudeborgenMode.FakeRunner, declareFake: true);
            Assert.AreEqual(HttpStatusCode.OK, (await _client.GetAsync("/api/claude/work")).StatusCode);
        }

        [TestMethod]
        public async Task FullRound_OverHttp_CamelCaseThroughout()
        {
            await StartAsync(ClaudeborgenMode.Agent);
            var id = _service.Start("Exportknapp", "Lägg till en exportknapp", "agneta").Id;

            Assert.AreEqual(HttpStatusCode.NoContent, (await PostAsync("/api/claude/heartbeat", new { activity = "idle" })).StatusCode);
            Assert.AreEqual(ClaudeActivity.Idle, _service.GetStatus().Activity);

            var work = await JsonAsync(await _client.GetAsync("/api/claude/work"));
            Assert.AreEqual(1, work.GetArrayLength());
            Assert.AreEqual(id, work[0].GetProperty("id").GetInt32());
            Assert.AreEqual("waitingForClaude", work[0].GetProperty("state").GetString());

            var claim = await PostAsync($"/api/claude/conversations/{id}/claim");
            Assert.AreEqual(HttpStatusCode.OK, claim.StatusCode);
            var round = await JsonAsync(claim);
            Assert.AreEqual(1, round.GetProperty("number").GetInt32());
            Assert.IsFalse(round.GetProperty("isFixRound").GetBoolean());

            await PostAsync("/api/claude/heartbeat", new { activity = "working", conversationId = id, phase = "implementerar" });
            var status = _service.GetStatus();
            Assert.AreEqual(ClaudeActivity.Working, status.Activity);
            Assert.AreEqual("implementerar", status.Phase);

            var conversation = await JsonAsync(await _client.GetAsync($"/api/claude/conversations/{id}"));
            Assert.AreEqual("Exportknapp", conversation.GetProperty("title").GetString());
            Assert.AreEqual("human", conversation.GetProperty("messages")[0].GetProperty("authorKind").GetString());
            Assert.AreEqual("claudeWorking", conversation.GetProperty("state").GetString());

            var reply = await PostAsync($"/api/claude/conversations/{id}/reply", new
            {
                body = "Klart, prova här.",
                commits = new[] { "abc1234" },
                links = new[] { "https://snabbtest.example/" },
                sessionId = "session-1",
                costUsd = 0.42,
                durationSeconds = 120
            });
            Assert.AreEqual(HttpStatusCode.NoContent, reply.StatusCode);

            var tests = await PostAsync($"/api/claude/conversations/{id}/tests", new { commit = "abc1234", green = false, summary = "1 rött" });
            Assert.AreEqual(HttpStatusCode.NoContent, tests.StatusCode);

            var after = await JsonAsync(await _client.GetAsync($"/api/claude/conversations/{id}"));
            Assert.AreEqual("waitingForClaude", after.GetProperty("state").GetString(), "Red tests send it back for a fix round.");
            Assert.AreEqual("session-1", after.GetProperty("sessionId").GetString());
            var firstRound = after.GetProperty("rounds")[0];
            Assert.AreEqual("red", firstRound.GetProperty("tests").GetString());
            Assert.AreEqual("delivered", firstRound.GetProperty("outcome").GetString());

            var fix = await JsonAsync(await PostAsync($"/api/claude/conversations/{id}/claim"));
            Assert.IsTrue(fix.GetProperty("isFixRound").GetBoolean());

            var crash = await PostAsync($"/api/claude/conversations/{id}/crash", new { summary = "bygget hängde sig" });
            Assert.AreEqual(HttpStatusCode.NoContent, crash.StatusCode);
            Assert.IsTrue(_service.Get(id).NeedsIt);
        }

        [TestMethod]
        public async Task Changelog_PendingAfterDone_ThenCommitted()
        {
            await StartAsync(ClaudeborgenMode.Agent);
            var id = _service.Start("Exportknapp", "a", "agneta").Id;
            _service.Claim(id);
            _service.Reply(id, "Klart", new[] { "c1" }, null, "s", null, null);
            _service.MarkDone(id, "agneta", "Exportknapp på listan");

            var pending = await JsonAsync(await _client.GetAsync("/api/claude/changelog/pending"));
            Assert.AreEqual(1, pending.GetArrayLength());
            Assert.AreEqual(id, pending[0].GetProperty("conversationId").GetInt32());
            Assert.AreEqual("Exportknapp på listan", pending[0].GetProperty("text").GetString());
            StringAssert.Matches(pending[0].GetProperty("date").GetString(), new System.Text.RegularExpressions.Regex(@"^\d{4}-\d{2}-\d{2}$"));

            Assert.AreEqual(HttpStatusCode.NoContent, (await PostAsync($"/api/claude/conversations/{id}/changelog", new { commit = "c0ffee" })).StatusCode);
            Assert.AreEqual(0, (await JsonAsync(await _client.GetAsync("/api/claude/changelog/pending"))).GetArrayLength());
        }

        [TestMethod]
        public async Task InvalidStateChange_Is409WithMessage()
        {
            await StartAsync(ClaudeborgenMode.Agent);
            var id = _service.Start("Ett", "a", "agneta").Id;

            var response = await PostAsync($"/api/claude/conversations/{id}/reply", new { body = "för tidigt" });

            Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
            StringAssert.Contains((await JsonAsync(response)).GetProperty("message").GetString(), "arbetar inte");
        }

        [TestMethod]
        public async Task UnknownConversation_Is404()
        {
            await StartAsync(ClaudeborgenMode.Agent);
            Assert.AreEqual(HttpStatusCode.NotFound, (await _client.GetAsync("/api/claude/conversations/999")).StatusCode);
        }

        private class NoPagesHost : IClaudeborgenHost
        {
            public ClaudeAccess AccessFor(ClaimsPrincipal user) => ClaudeAccess.None;
            public IActionResult ConversationPage(Controller controller, ClaudePageModel model) => new NotFoundResult();
        }
    }
}
