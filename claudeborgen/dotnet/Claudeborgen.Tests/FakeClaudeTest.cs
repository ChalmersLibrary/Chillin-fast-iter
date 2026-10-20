using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Claudeborgen;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Claudeborgen.Tests
{
    // FakeClaude's markers ([röd], [röd!], [krasch]) per claudeborgen-DESIGN.md, "Fejkad Claude".
    // Zero phase delay, one RunOnceAsync per runner-loop pass.
    [TestClass]
    public class FakeClaudeTest
    {
        private string _dataDirectory;
        private ClaudeborgenService _service;
        private FakeClaude _fake;

        [TestInitialize]
        public void Setup()
        {
            _dataDirectory = Path.Combine(Path.GetTempPath(), "claudeborgen-fake-test-" + Guid.NewGuid());
            _service = new ClaudeborgenService(
                new ConversationStore(_dataDirectory),
                new ClaudeborgenSettings { Mode = ClaudeborgenMode.InAppFake, DataDirectory = _dataDirectory, MaxFixAttempts = 2 });
            _fake = new FakeClaude(_service, TimeSpan.Zero, TimeSpan.Zero);
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(_dataDirectory))
                Directory.Delete(_dataDirectory, true);
        }

        private async Task RunAsync(int passes)
        {
            for (var i = 0; i < passes; i++)
                await _fake.RunOnceAsync(CancellationToken.None);
        }

        [TestMethod]
        public async Task PlainMessage_GivesOneGreenDeliveredRound()
        {
            var id = _service.Start("Exportknapp", "Lägg till en exportknapp", "agneta").Id;

            await RunAsync(2);

            var conversation = _service.Get(id);
            Assert.AreEqual(ConversationState.WaitingForOwner, conversation.State);
            var round = conversation.Rounds.Single();
            Assert.AreEqual(RoundOutcome.Delivered, round.Outcome);
            Assert.AreEqual(TestStatus.Green, round.Tests);
            StringAssert.StartsWith(round.Commits.Single(), FakeClaude.FakeCommitPrefix);
            StringAssert.Contains(conversation.Messages.Last(m => m.AuthorKind == MessageAuthorKind.Claude).Body, "Fejkad Claude");
            Assert.AreEqual(ClaudeActivity.Idle, _service.GetStatus().Activity);
        }

        [TestMethod]
        public async Task Red_GivesOneRedRound_ThenAGreenFixRound()
        {
            var id = _service.Start("Ett", "Gör något [röd]", "agneta").Id;

            await RunAsync(3);

            var rounds = _service.Get(id).Rounds;
            Assert.AreEqual(2, rounds.Count);
            Assert.AreEqual(TestStatus.Red, rounds[0].Tests);
            Assert.IsTrue(rounds[1].IsFixRound);
            Assert.AreEqual(TestStatus.Green, rounds[1].Tests);
            Assert.AreEqual(ConversationState.WaitingForOwner, _service.Get(id).State);
        }

        [TestMethod]
        public async Task RedForever_FlagsNeedsIt_AfterMaxFixAttempts()
        {
            var id = _service.Start("Ett", "Gör något [röd!]", "agneta").Id;

            await RunAsync(6);

            var conversation = _service.Get(id);
            Assert.AreEqual(3, conversation.Rounds.Count, "First round + MaxFixAttempts (2) fix rounds, then it stops.");
            Assert.IsTrue(conversation.NeedsIt);
            Assert.AreEqual(ConversationState.WaitingForOwner, conversation.State);
        }

        [TestMethod]
        public async Task Crash_FlagsNeedsIt_WithoutDelivering()
        {
            var id = _service.Start("Ett", "Gör något [krasch]", "agneta").Id;

            await RunAsync(2);

            var conversation = _service.Get(id);
            Assert.AreEqual(RoundOutcome.Crashed, conversation.Rounds.Single().Outcome);
            Assert.IsTrue(conversation.NeedsIt);
        }
    }
}
