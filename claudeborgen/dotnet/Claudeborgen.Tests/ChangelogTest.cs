using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Claudeborgen.Tests
{
    // The changelog file + done conversations not yet in it (DESIGN.md, "Changelog").
    [TestClass]
    public class ChangelogTest
    {
        private static Conversation Done(int id, string doneAt, string text, string commit = null) => new Conversation
        {
            Id = id,
            Title = "Rubrik " + id,
            State = ConversationState.Done,
            DoneAt = DateTime.Parse(doneAt),
            ChangelogText = text,
            ChangelogCommit = commit,
        };

        [TestMethod]
        public void Merge_SortsByDate_WhoeverWroteTheEntry()
        {
            var file = new List<ChangelogEntry>
            {
                new ChangelogEntry { Date = "2026-11-02", Version = "v2.7.1", Items = { "Handskriven, nyast" } },
                new ChangelogEntry { Date = "2026-10-21", Items = { "Från Claude" }, ConversationId = 3 },
                new ChangelogEntry { Date = "2025-09-09", Version = "v2.6.3", Items = { "Gammal" } },
            };

            var merged = ChangelogFile.Merge(file, new[] { Done(3, "2026-10-21", "Från Claude", "abc"), Done(4, "2026-10-25", "Klar men inte driftsatt", "def") }, fakeMode: false);

            CollectionAssert.AreEqual(
                new[] { "Handskriven, nyast", "Klar men inte driftsatt", "Från Claude", "Gammal" },
                merged.Select(e => e.Items.Single()).ToList());
            Assert.IsTrue(merged[1].Pending);
            Assert.IsFalse(merged[1].Fake);
            Assert.IsFalse(merged[2].Pending, "Already in the deployed file - shown once, from the file.");
        }

        [TestMethod]
        public void Merge_SameDate_KeepsFileOrder()
        {
            var file = new List<ChangelogEntry>
            {
                new ChangelogEntry { Date = "2026-10-21", Items = { "Först i filen" } },
                new ChangelogEntry { Date = "2026-10-21", Items = { "Sedan" } },
            };

            var merged = ChangelogFile.Merge(file, Array.Empty<Conversation>(), fakeMode: false);

            CollectionAssert.AreEqual(new[] { "Först i filen", "Sedan" }, merged.Select(e => e.Items.Single()).ToList());
        }

        [TestMethod]
        public void Merge_InAFakeMode_PendingEntriesAreMarkedFake()
        {
            var merged = ChangelogFile.Merge(new List<ChangelogEntry>(), new[] { Done(1, "2026-10-21", "X", FakeClaude.FakeCommitPrefix + "-changelog") }, fakeMode: true);
            Assert.IsTrue(merged.Single().Fake);
        }

        [TestMethod]
        public void Load_MissingFileIsEmpty_FileIsCamelCaseJson()
        {
            Assert.AreEqual(0, ChangelogFile.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json")).Count);

            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
            File.WriteAllText(path, "[{\"date\":\"2026-10-21\",\"items\":[\"Exportknapp\"],\"conversationId\":12}]");
            try
            {
                var entry = ChangelogFile.Load(path).Single();
                Assert.AreEqual("2026-10-21", entry.Date);
                Assert.AreEqual(12, entry.ConversationId);
                Assert.AreEqual("Exportknapp", entry.Items.Single());
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
