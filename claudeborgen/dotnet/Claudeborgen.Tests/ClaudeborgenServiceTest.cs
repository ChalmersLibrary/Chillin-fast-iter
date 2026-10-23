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
        public void Question_ShowsAsAQuestion_AndTheAnswerStartsTheNextRound()
        {
            var id = _service.Start("Knapp", "Lägg till en knapp", "agneta").Id;
            _service.Claim(id);
            Tick();
            _service.Reply(id, "Överst eller nederst?", null, null, "s-1", null, null, question: true);

            var asked = _service.Get(id);
            Assert.AreEqual(ConversationState.QuestionForOwner, asked.State);
            Assert.IsTrue(asked.Rounds.Single().AskedQuestion);
            Assert.IsTrue(asked.Messages.Last().IsQuestion);
            Assert.AreEqual(0, _service.Work().Count, "Claude waits for the answer.");

            Tick();
            _service.AddMessage(id, "Överst", "agneta");

            Assert.AreEqual(ConversationState.WaitingForClaude, _service.Get(id).State);
            Assert.IsFalse(_service.Claim(id).IsFixRound);
        }

        [TestMethod]
        public void Question_CanStillBeClosedByTheOwner()
        {
            var id = _service.Start("Knapp", "a", "agneta").Id;
            _service.Claim(id);
            _service.Reply(id, "Överst eller nederst?", null, null, "s-1", null, null, question: true);

            _service.Cancel(id, "agneta");

            Assert.IsNull(_service.Get(id));
            CollectionAssert.AreEqual(new[] { "s-1" }, _service.PendingSessionCleanup().Single().SessionIds, "Claude asked - its session holds the discussion.");
        }

        // DESIGN.md, "Driftsatt": the owner learns when a change is actually there to try.
        [TestMethod]
        public void DeliveredRound_WaitsForDeployment_ThenTellsTheOwner()
        {
            var id = StartAndDeliver(commit: "c1");
            Assert.AreEqual((id, 1, "c1"), _service.PendingDeployments().Single());

            _service.MarkDeployed(id, 1);

            var conversation = _service.Get(id);
            Assert.IsNotNull(conversation.Rounds.Single().DeployedAt);
            Assert.AreEqual("Ändringen är ute på snabbtestservern och går att prova.", conversation.Messages.Last().Body);
            Assert.AreEqual(0, _service.PendingDeployments().Count);
            Assert.AreEqual(ConversationState.WaitingForOwner, conversation.State, "Being deployed changes nothing about whose turn it is.");

            _service.MarkDeployed(id, 1);
            Assert.AreEqual(1, _service.Get(id).Messages.Count(m => m.Body.StartsWith("Ändringen är ute")), "Reported once.");
        }

        [TestMethod]
        public void OnlyTheNewestChange_GetsTheMessage()
        {
            var id = StartAndDeliver(commit: "c1");
            _service.AddMessage(id, "En sak till", "agneta");
            _service.Claim(id);
            _service.Reply(id, "Klart igen", new[] { "c2" }, null, "s", null, null);

            _service.MarkDeployed(id, 1);
            _service.MarkDeployed(id, 2);

            Assert.AreEqual(1, _service.Get(id).Messages.Count(m => m.Body.StartsWith("Ändringen är ute")));
        }

        [TestMethod]
        public void RoundsWithoutCommits_AndOldRounds_AreNotWaitedFor()
        {
            var id = _service.Start("Fråga", "a", "agneta").Id;
            _service.Claim(id);
            _service.Reply(id, "Svar utan ändring", null, null, "s", null, null);
            Assert.AreEqual(0, _service.PendingDeployments().Count, "Nothing to deploy.");

            var other = StartAndDeliver(commit: "c9");
            _now = _now.AddDays(2);
            Assert.AreEqual(0, _service.PendingDeployments().Count, "Two days old - never going to show up.");
        }

        // DESIGN.md, "Idéer"
        [TestMethod]
        public void Idea_NeedsNoTitle_AndIsMadeFromTheFirstMessage()
        {
            var idea = _service.StartIdea("Jag funderar på om man kunde exportera listan till Excel på något sätt, typ en knapp", "agneta");

            Assert.AreEqual(ConversationKind.Idea, idea.Kind);
            Assert.IsTrue(idea.Title.Length <= 60);
            StringAssert.StartsWith(idea.Title, "Jag funderar på om man kunde exportera");
            Assert.AreEqual(idea.Id, _service.Work(ConversationKind.Idea).Single().Id);
            Assert.AreEqual(0, _service.Work(ConversationKind.Task).Count);
        }

        [TestMethod]
        public void IdeaAndTask_CanBeWorkedOnAtTheSameTime_ButOnlyOneOfEach()
        {
            var task = _service.Start("Arbete", "a", "agneta").Id;
            var idea = _service.StartIdea("Idé", "agneta").Id;
            var idea2 = _service.StartIdea("Idé 2", "agneta").Id;
            _service.Claim(task);

            _service.Claim(idea);

            Assert.ThrowsException<ClaudeborgenException>(() => _service.Claim(idea2));
        }

        [TestMethod]
        public void ConvertToTask_KeepsTheDiscussion_AndStartsANormalRoundInTheSameSession()
        {
            var id = _service.StartIdea("Exportknapp?", "agneta").Id;
            _service.Claim(id);
            Tick();
            _service.Reply(id, "Låter rimligt, vem ska använda den?", null, null, "s-idea", null, null);
            Tick();

            _service.ConvertToTask(id, "Exportknapp på listan", "agneta");

            var task = _service.Get(id);
            Assert.AreEqual(ConversationKind.Task, task.Kind);
            Assert.AreEqual("Exportknapp på listan", task.Title);
            Assert.AreEqual(ConversationState.WaitingForClaude, task.State);
            Assert.AreEqual("s-idea", task.SessionId, "Claude keeps the idea discussion in mind.");
            Assert.AreEqual(3, task.Messages.Count);
            StringAssert.Contains(task.Messages.Last().Body, "Exportknapp på listan");
            Assert.AreEqual(MessageAuthorKind.Human, task.Messages.Last().AuthorKind);
            Assert.IsFalse(_service.Claim(id).IsFixRound, "A normal round, not a fix round.");
        }

        [TestMethod]
        public void Idea_CannotBeMarkedDone_OnlyConvertedOrLetGo()
        {
            var id = _service.StartIdea("Idé", "agneta").Id;
            _service.Claim(id);
            _service.Reply(id, "Svar", null, null, "s", null, null);

            Assert.ThrowsException<ClaudeborgenException>(() => _service.MarkDone(id, "agneta", null));

            _service.Cancel(id, "agneta");
            Assert.IsNull(_service.Get(id), "Letting go of an idea removes it.");
        }

        [TestMethod]
        public void IdleIdea_IsRemovedAfterTheRetentionPeriod()
        {
            var id = _service.StartIdea("Idé", "agneta").Id;
            _service.Claim(id);
            _service.Reply(id, "Svar", null, null, "s", null, null);

            _now = _now.AddDays(15);
            _service.ChangelogView();

            Assert.IsNull(_service.Get(id));
            CollectionAssert.AreEqual(new[] { "s" }, _service.PendingSessionCleanup().Single().SessionIds, "Its Claude session is cleaned up too.");
        }

        [TestMethod]
        public void Release_PutsTheConversationBackInTheQueue_WithoutTrace()
        {
            var id = _service.Start("Ett", "a", "agneta").Id;
            _service.Claim(id);

            _service.Release(id);

            var conversation = _service.Get(id);
            Assert.AreEqual(ConversationState.WaitingForClaude, conversation.State);
            Assert.AreEqual(0, conversation.Rounds.Count, "The round never ran - it's dropped, not crashed.");
            Assert.IsFalse(conversation.NeedsIt);
            Assert.AreEqual(id, _service.Work().Single().Id);
            Assert.AreEqual(1, _service.Claim(id).Number, "The next claim starts the same round over.");
        }

        [TestMethod]
        public void PausedHeartbeat_ShowsPausedWithReasonAndRetryTime_NotOffline()
        {
            var until = _now.AddMinutes(30);
            _service.Heartbeat(ClaudeActivity.Paused, null, "kvoten är slut", until);

            var status = _service.GetStatus();
            Assert.AreEqual(ClaudeActivity.Paused, status.Activity);
            Assert.AreEqual("kvoten är slut", status.Phase);
            Assert.AreEqual(until, status.PausedUntil);

            Tick(61);
            Assert.AreEqual(ClaudeActivity.Offline, _service.GetStatus().Activity, "Paused still needs heartbeats - a dead runner is offline.");
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

        // Claude suggests the changelog text after every round, until the owner writes their own;
        // emptying it hands it back to Claude.
        [TestMethod]
        public void ChangelogText_ClaudeSuggestsEachRound_UntilTheOwnerWritesTheirOwn()
        {
            var id = _service.Start("Exportknapp", "Lägg till en exportknapp", "agneta").Id;
            _service.Claim(id);
            _service.Reply(id, "Klart", new[] { "c1" }, null, "s", null, null, changelogText: "Export\n av listan");
            Assert.AreEqual("Export av listan", _service.Get(id).ChangelogText);

            _service.AddMessage(id, "Som Excel också", "agneta");
            _service.Claim(id);
            _service.Reply(id, "Klart", new[] { "c2" }, null, "s", null, null, changelogText: "Export av listan till CSV och Excel");
            Assert.AreEqual("Export av listan till CSV och Excel", _service.Get(id).ChangelogText);

            _service.SetChangelogText(id, "Exportknapp på listan");
            _service.AddMessage(id, "Grön knapp", "agneta");
            _service.Claim(id);
            _service.Reply(id, "Klart", new[] { "c3" }, null, "s", null, null, changelogText: "Grön exportknapp");
            Assert.AreEqual("Exportknapp på listan", _service.Get(id).ChangelogText);
            Assert.IsTrue(_service.Get(id).ChangelogTextByHuman);

            _service.SetChangelogText(id, " ");
            Assert.IsNull(_service.Get(id).ChangelogText);
            Assert.IsFalse(_service.Get(id).ChangelogTextByHuman);

            // No suggestion this round (e.g. a question): the last one stays.
            _service.SetChangelogText(id, " ");
            _service.AddMessage(id, "Och en till", "agneta");
            _service.Claim(id);
            _service.Reply(id, "Klart", new[] { "c4" }, null, "s", null, null, changelogText: "Grön exportknapp");
            _service.AddMessage(id, "Fråga", "agneta");
            _service.Claim(id);
            _service.Reply(id, "Vilken?", null, null, "s", null, null, question: true);
            Assert.AreEqual("Grön exportknapp", _service.Get(id).ChangelogText);
        }

        [TestMethod]
        public void ChangelogText_CannotBeSetOnAnIdea()
        {
            var idea = _service.StartIdea("Tänk om", "agneta");
            Assert.ThrowsException<ClaudeborgenException>(() => _service.SetChangelogText(idea.Id, "Text"));
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
        }

        // DESIGN.md, "Samtal tas bort": once the entry is in the repo the discussion is gone; only
        // the changelog line and the sessions to clean up are remembered.
        [TestMethod]
        public void CommittedChangelog_RemovesTheConversation_KeepingOnlyTheEntryAndSessions()
        {
            var id = StartAndDeliver();
            _service.MarkDone(id, "agneta", "Exportknapp");

            _service.MarkChangelogCommitted(id, "c0ffee");

            Assert.IsNull(_service.Get(id), "The discussion is deleted.");
            Assert.IsFalse(File.Exists(Path.Combine(_dataDirectory, "conversations", id + ".json")));
            var removedJson = File.ReadAllText(Path.Combine(_dataDirectory, "removed.json"));
            Assert.IsFalse(removedJson.Contains("Lägg till en exportknapp"), "No message text survives.");
            Assert.IsFalse(removedJson.Contains("Klart, prova här"), "No message text survives.");

            var cleanup = _service.PendingSessionCleanup().Single();
            Assert.AreEqual(id, cleanup.ConversationId);
            CollectionAssert.AreEqual(new[] { "session-1" }, cleanup.SessionIds);

            var entry = _service.ChangelogView().Single();
            Assert.AreEqual("Exportknapp", entry.Items.Single());
            Assert.IsTrue(entry.Pending, "Committed but not yet in the deployed file: shown as on its way.");
        }

        [TestMethod]
        public void RemovedRecord_IsDropped_OnceTheFileHasTheEntryAndSessionsAreCleaned()
        {
            var changelog = Path.Combine(_dataDirectory, "changelog.json");
            _service.Settings.ChangelogPath = changelog;
            var id = StartAndDeliver();
            _service.MarkDone(id, "agneta", "Exportknapp");
            _service.MarkChangelogCommitted(id, "c0ffee");

            Directory.CreateDirectory(_dataDirectory);
            File.WriteAllText(changelog, $"[{{\"date\":\"2026-10-19\",\"items\":[\"Exportknapp\"],\"conversationId\":{id}}}]");
            Assert.AreEqual(1, _service.ChangelogView().Count, "From the file now, not twice.");
            Assert.IsFalse(_service.ChangelogView().Single().Pending);
            Assert.AreEqual(1, _service.PendingSessionCleanup().Count, "Kept until the runner has deleted the sessions.");

            _service.MarkSessionsCleaned(id);
            _service.ChangelogView();

            Assert.AreEqual(0, _service.PendingSessionCleanup().Count);
            StringAssert.Contains(File.ReadAllText(Path.Combine(_dataDirectory, "removed.json")), "[]");
        }

        // Regression (seen on the fast test server): conversations done or cancelled with a version
        // from before removal existed stayed in the store.
        [TestMethod]
        public void LeftoversFromBeforeRemovalExisted_AreCleanedUp()
        {
            var store = new ConversationStore(_dataDirectory);
            store.Save(new Conversation { Id = 1, Title = "Gammal klar", State = ConversationState.Done, DoneAt = _now, ChangelogText = "Gammal klar", ChangelogCommit = "fejk-changelog",
                Messages = { new ConversationMessage { AuthorKind = MessageAuthorKind.Human, Author = "agneta", Body = "hemligt", At = _now } } });
            store.Save(new Conversation { Id = 2, Title = "Gammal avbruten", State = ConversationState.Cancelled });
            store.Save(new Conversation { Id = 3, Title = "Klar, inte committad", State = ConversationState.Done, DoneAt = _now, ChangelogText = "Väntar" });

            var view = _service.ChangelogView();

            Assert.IsNull(_service.Get(1));
            Assert.IsNull(_service.Get(2));
            Assert.IsNotNull(_service.Get(3), "Not committed yet - still needed.");
            Assert.AreEqual(2, view.Count(e => e.ConversationId != null));
            Assert.IsFalse(File.ReadAllText(Path.Combine(_dataDirectory, "removed.json")).Contains("hemligt"));
        }

        [TestMethod]
        public void ConversationNumbers_AreNeverReused_AfterRemoval()
        {
            var first = _service.Start("Ett", "a", "agneta").Id;
            _service.Cancel(first, "agneta");

            var second = _service.Start("Två", "b", "agneta").Id;

            Assert.AreEqual(first + 1, second);
        }

        [TestMethod]
        public void ChangelogCommitted_OnAConversationThatIsNotDone_IsRejected()
        {
            var id = _service.Start("Ett", "a", "agneta").Id;
            Assert.ThrowsException<ClaudeborgenException>(() => _service.MarkChangelogCommitted(id, "c0ffee"));
        }

        // DESIGN.md, "Pausa utvecklingen"
        [TestMethod]
        public void PausedDevelopment_HandsOutNothingThatPushes_IdeasGoOn()
        {
            var task = _service.Start("Arbete", "a", "agneta").Id;
            var done = StartAndDeliver(commit: "c1");
            _service.MarkDone(done, "agneta", "Klar sak");
            var cancelled = StartAndDeliver(commit: "c2");
            _service.Cancel(cancelled, "agneta");
            var idea = _service.StartIdea("En idé", "agneta").Id;

            _service.PauseDevelopment("ivar");

            Assert.AreEqual("ivar", _service.GetStatus().DevelopmentPause.PausedBy);
            Assert.AreEqual(0, _service.Work(ConversationKind.Task).Count);
            Assert.ThrowsException<ClaudeborgenException>(() => _service.Claim(task));
            Assert.AreEqual(0, _service.PendingChangelog().Count, "No changelog commits.");
            Assert.AreEqual(0, _service.PendingReverts().Count, "No reverts.");
            Assert.AreEqual(idea, _service.Work(ConversationKind.Idea).Single().Id, "Ideas don't touch git.");

            _service.AddMessage(task, "Köas medan det är pausat", "agneta");
            Assert.AreEqual(ConversationState.WaitingForClaude, _service.Get(task).State);

            var reopened = NewService();
            Assert.IsNotNull(reopened.DevelopmentPause(), "Survives an app restart.");

            _service.ResumeDevelopment();

            Assert.IsNull(_service.GetStatus().DevelopmentPause);
            Assert.AreEqual(1, _service.PendingChangelog().Count);
            Assert.AreEqual(1, _service.PendingReverts().Count);
            Assert.AreEqual(0, _service.Work(ConversationKind.Task).Count, "The cancelled work is reverted first.");
            _service.MarkReverted(cancelled, "r1");
            Assert.AreEqual(task, _service.Work(ConversationKind.Task).Single().Id);
        }

        // DESIGN.md, "Avbryta ett arbete"
        [TestMethod]
        public void CancellingWorkThatPushed_RevertsFirst_ThenRemoves()
        {
            var id = StartAndDeliver(commit: "c1");
            _service.AddMessage(id, "En sak till", "agneta");
            _service.Claim(id);
            _service.Reply(id, "Klart igen", new[] { "c2" }, null, "s", null, null);

            _service.Cancel(id, "agneta");

            Assert.AreEqual(ConversationState.Reverting, _service.Get(id).State, "Kept until its commits are reverted.");
            var revert = _service.PendingReverts().Single();
            CollectionAssert.AreEqual(new[] { "c1", "c2" }, revert.Commits, "Everything it pushed, oldest first.");
            Assert.ThrowsException<ClaudeborgenException>(() => _service.AddMessage(id, "hallå", "agneta"));
            Assert.ThrowsException<ClaudeborgenException>(() => _service.MarkDone(id, "agneta", null));
            Assert.AreEqual(0, _service.Work().Count);

            _service.MarkReverted(id, "r1");

            Assert.IsNull(_service.Get(id));
            Assert.AreEqual(0, _service.PendingReverts().Count);
            Assert.AreEqual(0, _service.ChangelogView().Count, "Cancelled work never reaches the changelog.");
        }

        // No new round is built on top of work that is about to be removed - a round already
        // running finishes, and a failed revert (waiting for IT) doesn't hold anything back.
        [TestMethod]
        public void NewWork_WaitsUntilCancelledWorkIsReverted()
        {
            var running = StartAndDeliver(commit: "c0");
            var cancelled = StartAndDeliver(commit: "c1");
            _service.AddMessage(running, "En sak till", "agneta");
            _service.Claim(running);
            _service.Cancel(cancelled, "agneta");
            var next = _service.Start("Nästa", "b", "agneta").Id;
            var idea = _service.StartIdea("En idé", "agneta").Id;

            CollectionAssert.AreEqual(new[] { running, idea }, _service.Work().Select(c => c.Id).ToList());
            Assert.ThrowsException<ClaudeborgenException>(() => _service.Claim(next));

            _service.MarkRevertFailed(cancelled, "konflikt");
            Assert.IsTrue(_service.Work(ConversationKind.Task).Any(c => c.Id == next));

            _service.Cancel(cancelled, "ivar");
            Assert.IsFalse(_service.Work(ConversationKind.Task).Any(c => c.Id == next));
            _service.MarkReverted(cancelled, "r1");
            Assert.IsTrue(_service.Work(ConversationKind.Task).Any(c => c.Id == next));
        }

        [TestMethod]
        public void FailedRevert_GoesToIt_AndCancellingAgainRetries()
        {
            var id = StartAndDeliver(commit: "c1");
            _service.Cancel(id, "agneta");

            _service.MarkRevertFailed(id, "git revert gick inte rent igenom");

            var failed = _service.Get(id);
            Assert.IsTrue(failed.NeedsIt);
            Assert.AreEqual(id, _service.NeedingAttention().Single().Id);
            Assert.AreEqual(0, _service.PendingReverts().Count, "Not retried on its own.");

            _service.Cancel(id, "ivar");

            Assert.IsFalse(_service.Get(id).NeedsIt);
            Assert.AreEqual(1, _service.PendingReverts().Count);
        }

        [TestMethod]
        public void CancellingWorkThatPushedNothing_RemovesItRightAway()
        {
            var id = _service.Start("Ett", "a", "agneta").Id;
            _service.Claim(id);
            _service.Reply(id, "Bara ett svar", null, null, "s", null, null);

            _service.Cancel(id, "agneta");

            Assert.IsNull(_service.Get(id));
            Assert.AreEqual(0, _service.PendingReverts().Count);
        }

        [TestMethod]
        public void CancelledConversation_IsNotInTheChangelog()
        {
            var id = _service.Start("Ett", "a", "agneta").Id;

            _service.Cancel(id, "agneta");

            Assert.IsNull(_service.Get(id), "A cancelled conversation is removed right away.");
            Assert.AreEqual(0, _service.ChangelogView().Count);
            Assert.AreEqual(0, _service.Work().Count);
            Assert.AreEqual(0, _service.PendingSessionCleanup().Count, "Claude never ran - no sessions to clean.");
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
