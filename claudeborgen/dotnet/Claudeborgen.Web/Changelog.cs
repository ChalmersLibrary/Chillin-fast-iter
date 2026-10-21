using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Claudeborgen
{
    // The app's changelog is one file in the repo (DESIGN.md, "Changelog"): hand-written entries and
    // the ones the runner commits when a conversation is marked done, so an entry travels with the
    // code through every environment. Newest first in the file, by convention; rendering sorts by
    // date anyway, so the order a human happens to write them in doesn't matter.
    public class ChangelogEntry
    {
        public string Date { get; set; }
        public string Version { get; set; }
        public List<string> Items { get; set; } = new List<string>();

        // Set on entries the runner wrote for a conversation; lets the page tell which done
        // conversations are already in the deployed file.
        public int? ConversationId { get; set; }

        // Not in the file: a done conversation whose entry isn't in this deployment's file yet
        // (committed but not deployed here, or - in a fake mode - never committed at all).
        public bool Pending { get; set; }
        public bool Fake { get; set; }

        // The conversation still exists on this server and can be opened. Conversations are
        // removed once their entry is committed, so most entries aren't.
        public bool Linkable { get; set; }
    }

    public static class ChangelogFile
    {
        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
        };

        // A missing file is an empty changelog. A broken one throws: it's checked into the repo and
        // covered by a test, so it should never reach a server broken.
        public static List<ChangelogEntry> Load(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return new List<ChangelogEntry>();

            return JsonSerializer.Deserialize<List<ChangelogEntry>>(File.ReadAllText(path), Json) ?? new List<ChangelogEntry>();
        }

        // File entries plus done conversations not yet in the file, newest first. Entries with the
        // same date keep their relative order (OrderByDescending is stable), with the pending
        // ones first since they're the newest news.
        public static List<ChangelogEntry> Merge(IEnumerable<ChangelogEntry> fileEntries, IEnumerable<Conversation> doneConversations, bool fakeMode)
        {
            var file = fileEntries.ToList();
            var inFile = new HashSet<int>(file.Where(e => e.ConversationId != null).Select(e => e.ConversationId.Value));

            var pending = doneConversations
                .Where(c => !inFile.Contains(c.Id))
                .OrderByDescending(c => c.DoneAt)
                .Select(c => new ChangelogEntry
                {
                    Date = (c.DoneAt ?? c.CreatedAt).ToString("yyyy-MM-dd"),
                    Items = new List<string> { c.ChangelogText ?? c.Title },
                    ConversationId = c.Id,
                    Pending = true,
                    Fake = fakeMode || (c.ChangelogCommit?.StartsWith(FakeClaude.FakeCommitPrefix) ?? false),
                });

            return pending.Concat(file)
                .OrderByDescending(e => e.Date, StringComparer.Ordinal)
                .ToList();
        }
    }
}
