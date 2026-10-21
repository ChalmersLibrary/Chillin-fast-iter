using System;
using System.IO;
using System.Linq;
using Claudeborgen;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Claudeborgen.Tests
{
    // The conversation flow of claudeborgen-DESIGN.md ("Flödet"), driven the way the runner (agent
    // API) and the pages drive it. Real files in a temp directory, a controllable clock.
    [TestClass]
    public class ClaudeborgenServiceTest
    {
        private string _dataDirectory;
        private DateTime _now;
        private ClaudeborgenService _service;

        [TestInitialize]
        public void Setup()
        {
            _dataDirectory = Path.Combine(Path.GetTempPath(), "claudeborgen-test-" + Guid.NewGuid());
            _now = new DateTime(2026, 10, 19, 10, 0, 0);
            _service = NewService();
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(_dataDirectory))
                Directory.Delete(_dataDirectory, true);
        }

        private ClaudeborgenService NewService() =>
            new ClaudeborgenService(
                new ConversationStore(_dataDirectory),
                new ClaudeborgenSettings { Mode = ClaudeborgenMode.Agent, DataDirectory = _dataDirectory, MaxFixAttempts = 2 },
                () => _now);

        private void Tick(int seconds = 1) => _now = _now.AddSeconds(seconds);

        private int StartAndDeliver(string body = "Lägg till en exportknapp", string commit = "abc1234")
        {
            var id = _service.Start("Exportknapp", body, "agneta").Id;
            Tick();
            _service.Claim(id);
            Tick();
            _service.Reply(id, "Klart, prova här.", new[] { commit }, new[] { "https://test.example/bestaellningar" }, "session-1", 0.42m, 120);
            Tick();
            return id;
        }

        [TestMethod]
        public void Start_CreatesConversationWaitingForClaude_AndPersistsIt()
        {
            var conversation = _service.Start("Exportknapp", "Lägg till en exportknapp", "agneta");

            var reloaded = NewService().Get(conversation.Id);
            Assert.AreEqual(ConversationState.WaitingForClaude, reloaded.State);
            Assert.AreEqual("Exportknapp", reloaded.Title);
            Assert.AreEqual("agneta", reloaded.Messages.Single().Author);
            Assert.AreEqual(1, _service.Work().Count);
        }

        [TestMethod]
        public void Start_WithoutTitleOrBody_IsRejected()
        {
            Assert.ThrowsException<ClaudeborgenException>(() => _service.Start("", "x", "agneta"));
            Assert.ThrowsException<ClaudeborgenException>(() => _service.Start("x", " ", "agneta"));
        }

        [TestMethod]
        public void FullRound_ClaimReplyGreenTests_EndsWaitingForOwner()
        {
            var id = StartAndDeliver();
            _service.ReportTests(id, "abc1234", true, "Alla gröna");

            var conversation = _service.Get(id);
            Assert.AreEqual(ConversationState.WaitingForOwner, conversation.State);
            var round = conversation.Rounds.Single();
            Assert.AreEqual(RoundOutcome.Delivered, round.Outcome);
            Assert.AreEqual(TestStatus.Green, round.Tests);
            CollectionAssert.AreEqual(new[] { "abc1234" }, round.Commits);
            Assert.AreEqual("session-1", conversation.SessionId);
            Assert.AreEqual(0.42m, round.CostUsd);
            Assert.AreEqual(MessageAuthorKind.Claude, conversation.Messages.Last().AuthorKind);
            Assert.AreEqual(0, _service.Work().Count);
        }

        [TestMethod]
        public void Claim_WhileClaudeWorksOnAnotherConversation_IsRejected()
        {
            var first = _service.Start("Ett", "a", "agneta").Id;
            var second = _service.Start("Två", "b", "agneta").Id;
            _service.Claim(first);

            Assert.ThrowsException<ClaudeborgenException>(() => _service.Claim(second));
        }

        [TestMethod]
        public void Claim_AgainOnTheSameConversation_ResumesTheSameRound()
        {
            var id = _service.Start("Ett", "a", "agneta").Id;
            var round = _service.Claim(id);

            var again = _service.Claim(id);

            Assert.AreEqual(round.Number, again.Number);
            Assert.AreEqual(1, _service.Get(id).Rounds.Count);
            Assert.AreEqual(id, _service.Work().First().Id, "A round in progress comes first, so a restarted runner resumes it.");
        }

        [TestMethod]
        public void RedTests_SendTheConversationBackToClaude_AsAFixRound()
        {
            var id = StartAndDeliver();

            _service.ReportTests(id, "abc1234", false, "1 test rött");

            var conversation = _service.Get(id);
            Assert.AreEqual(ConversationState.WaitingForClaude, conversation.State);
            Assert.IsFalse(conversation.NeedsIt);
            StringAssert.Contains(conversation.Messages.Last().Body, "1 test rött");

            Tick();
            var fix = _service.Claim(id);
            Assert.IsTrue(fix.IsFixRound);
        }

        [TestMethod]
        public void TestsStayingRed_AfterMaxFixAttempts_FlagsNeedsIt_InsteadOfLooping()
        {
            var id = StartAndDeliver(commit: "c1");
            _service.ReportTests(id, "c1", false, "rött");
            for (var attempt = 2; attempt <= 3; attempt++)
            {
                Tick();
                _service.Claim(id);
                Tick();
                _service.Reply(id, "Rättat", new[] { "c" + attempt }, null, "session-1", null, null);
                Tick();
                _service.ReportTests(id, "c" + attempt, false, "fortfarande rött");
            }

            var conversation = _service.Get(id);
            Assert.IsTrue(conversation.NeedsIt);
            Assert.AreEqual(ConversationState.WaitingForOwner, conversation.State);
            Assert.AreEqual(id, _service.NeedingAttention().Single().Id);
            Assert.AreEqual(0, _service.Work().Count);
        }

        [TestMethod]
        public void HumanMessage_ClearsNeedsIt_AndStartsANewRound()
        {
            var id = _service.Start("Ett", "a", "agneta").Id;
            _service.Claim(id);
            _service.ReportCrash(id, "runnern dog");
            Assert.IsTrue(_service.Get(id).NeedsIt);

            Tick();
            _service.AddMessage(id, "Prova igen", "it-person");

            var conversation = _service.Get(id);
            Assert.IsFalse(conversation.NeedsIt);
            Assert.AreEqual(ConversationState.WaitingForClaude, conversation.State);
        }

        [TestMethod]
        public void MessageWrittenWhileClaudeWorks_IsPickedUpAfterTheRound()
        {
            var id = _service.Start("Ett", "a", "agneta").Id;
            _service.Claim(id);
            Tick();
            _service.AddMessage(id, "Och gör den grön", "agneta");
            Assert.AreEqual(ConversationState.ClaudeWorking, _service.Get(id).State);

            Tick();
            _service.Reply(id, "Klart", new[] { "c1" }, null, null, null, null);

            Assert.AreEqual(ConversationState.WaitingForClaude, _service.Get(id).State);
        }

        // Regression: a message timestamped "in the future" relative to the server clock (seeded
        // data, another time zone) made every reply look like it had a newer message waiting, so
        // the conversation went straight back to Claude - 62 paid rounds in a row. Whether something
        // was written during a round is now decided by position in the conversation, not by clock.
        [TestMethod]
        public void MessageTimestampedAfterTheClaim_DoesNotSendTheConversationBackToClaude()
        {
            _now = _now.AddHours(7);
            var id = _service.Start("Ett", "a", "agneta").Id;
            _now = _now.AddHours(-7);

            _service.Claim(id);
            _service.Reply(id, "Klart", new[] { "c1" }, null, "s", null, null);

            Assert.AreEqual(ConversationState.WaitingForOwner, _service.Get(id).State);
            Assert.AreEqual(0, _service.Work().Count);
        }

        [TestMethod]
        public void FixRoundDetection_IsByPosition_NotByClock()
        {
            var id = _service.Start("Ett", "a", "agneta").Id;
            _service.Claim(id);
            _service.Reply(id, "Klart", new[] { "c1" }, null, "s", null, null);
            _now = _now.AddHours(-7); // the next human message gets an "older" timestamp
            _service.AddMessage(id, "Och en sak till", "agneta");

            var round = _service.Claim(id);

            Assert.IsFalse(round.IsFixRound, "A human message after the last round makes it a normal round, whatever its timestamp.");
        }

        [TestMethod]
        public void Crash_MarksRoundCrashed_AndFlagsNeedsIt()
        {
            var id = _service.Start("Ett", "a", "agneta").Id;
            _service.Claim(id);

            _service.ReportCrash(id, "bygget hängde sig");

            var conversation = _service.Get(id);
            Assert.AreEqual(RoundOutcome.Crashed, conversation.Rounds.Single().Outcome);
            Assert.IsTrue(conversation.NeedsIt);
            Assert.AreEqual(ConversationState.WaitingForOwner, conversation.State);
        }

        [TestMethod]
        public void MarkDone_PutsTheConversationInTheChangelog_WithTheGivenText()
        {
            var id = StartAndDeliver();

            _service.MarkDone(id, "agneta", "Exportknapp på beställningslistan");

            var entry = _service.Changelog().Single();
            Assert.AreEqual(id, entry.Id);
            Assert.AreEqual("Exportknapp på beställningslistan", entry.ChangelogText);
            Assert.AreEqual(ConversationState.Done, entry.State);
            Assert.ThrowsException<ClaudeborgenException>(() => _service.AddMessage(id, "en sak till", "agneta"));
        }

        [TestMethod]
        public void MarkDone_WithoutText_UsesTheTitle()
        {
            var id = StartAndDeliver();

            _service.MarkDone(id, "agneta", " ");

            Assert.AreEqual("Exportknapp", _service.Changelog().Single().ChangelogText);
        }

        [TestMethod]
        public void MarkDoneOrCancel_WhileClaudeWorks_IsRejected()
        {
            var id = _service.Start("Ett", "a", "agneta").Id;
            _service.Claim(id);

            Assert.ThrowsException<ClaudeborgenException>(() => _service.MarkDone(id, "agneta", null));
            Assert.ThrowsException<ClaudeborgenException>(() => _service.Cancel(id, "agneta"));
        }

        [TestMethod]
        public void Done_IsPendingForTheChangelog_UntilTheRunnerCommitsIt()
        {
            var id = StartAndDeliver();
            Assert.AreEqual(0, _service.PendingChangelog().Count, "Not done yet.");

            _service.MarkDone(id, "agneta", "Exportknapp");
            Assert.AreEqual(id, _service.PendingChangelog().Single().Id);

            _service.MarkChangelogCommitted(id, "c0ffee");
            Assert.AreEqual(0, _service.PendingChangelog().Count);
            Assert.AreEqual("c0ffee", _service.Get(id).ChangelogCommit);
        }

        [TestMethod]
        public void ChangelogCommitted_OnAConversationThatIsNotDone_IsRejected()
        {
            var id = _service.Start("Ett", "a", "agneta").Id;
            Assert.ThrowsException<ClaudeborgenException>(() => _service.MarkChangelogCommitted(id, "c0ffee"));
        }

        [TestMethod]
        public void CancelledConversation_IsNotInTheChangelog()
        {
            var id = _service.Start("Ett", "a", "agneta").Id;

            _service.Cancel(id, "agneta");

            Assert.AreEqual(ConversationState.Cancelled, _service.Get(id).State);
            Assert.AreEqual(0, _service.Changelog().Count);
            Assert.AreEqual(0, _service.Work().Count);
        }

        [TestMethod]
        public void Status_FollowsHeartbeats_AndGoesOfflineWithoutThem()
        {
            Assert.AreEqual(ClaudeActivity.Offline, _service.GetStatus().Activity, "No heartbeat yet = offline.");

            var id = _service.Start("Exportknapp", "a", "agneta").Id;
            _service.Heartbeat(ClaudeActivity.Working, id, "implementerar");
            var working = _service.GetStatus();
            Assert.AreEqual(ClaudeActivity.Working, working.Activity);
            Assert.AreEqual(id, working.ConversationId);
            Assert.AreEqual("Exportknapp", working.ConversationTitle);
            Assert.AreEqual("implementerar", working.Phase);
            Assert.AreEqual(1, working.QueueLength);

            _service.Heartbeat(ClaudeActivity.Idle, null, null);
            Assert.AreEqual(ClaudeActivity.Idle, _service.GetStatus().Activity);

            Tick(61);
            Assert.AreEqual(ClaudeActivity.Offline, _service.GetStatus().Activity);
        }

        [TestMethod]
        public void RoundStuckWhileClaudeIsOffline_NeedsAttention()
        {
            var id = _service.Start("Ett", "a", "agneta").Id;
            _service.Heartbeat(ClaudeActivity.Working, id, "implementerar");
            _service.Claim(id);
            Assert.AreEqual(0, _service.NeedingAttention().Count);

            Tick(61);

            Assert.AreEqual(id, _service.NeedingAttention().Single().Id);
        }

        [TestMethod]
        public void Reply_DropsUnsafeLinks()
        {
            var id = _service.Start("Ett", "a", "agneta").Id;
            _service.Claim(id);

            _service.Reply(id, "Klart", new[] { "c1" }, new[] { "javascript:alert(1)", "//evil.example", "/bestaellningar", "https://test.example/x" }, null, null, null);

            CollectionAssert.AreEqual(new[] { "/bestaellningar", "https://test.example/x" }, _service.Get(id).Rounds.Single().Links);
        }

        [TestMethod]
        public void Changes_AreBroadcast()
        {
            var broadcaster = new RecordingBroadcaster();
            _service.SetBroadcaster(broadcaster);

            var id = _service.Start("Ett", "a", "agneta").Id;
            _service.Heartbeat(ClaudeActivity.Idle, null, null);
            _service.Heartbeat(ClaudeActivity.Idle, null, null);

            CollectionAssert.Contains(broadcaster.Conversations, id);
            Assert.AreEqual(1, broadcaster.StatusChanges, "An unchanged heartbeat must not broadcast again.");
        }

        private class RecordingBroadcaster : IClaudeborgenBroadcaster
        {
            public System.Collections.Generic.List<int> Conversations { get; } = new System.Collections.Generic.List<int>();
            public int StatusChanges { get; private set; }
            public void ConversationChanged(int conversationId) => Conversations.Add(conversationId);
            public void StatusChanged() => StatusChanges++;
        }
    }
}
