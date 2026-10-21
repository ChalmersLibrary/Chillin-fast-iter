using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Claudeborgen
{
    // One JSON file per conversation under <dataDirectory>/conversations/. Not thread-safe on its
    // own: every caller goes through ClaudeborgenService, which serializes all access with a single
    // lock. Volume is a handful of conversations per week, so one lock for everything is plenty.
    public class ConversationStore
    {
        internal static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        private readonly string _conversationsDirectory;
        private readonly string _nextIdPath;
        private readonly string _removedPath;

        public ConversationStore(string dataDirectory)
        {
            _conversationsDirectory = Path.Combine(dataDirectory, "conversations");
            _nextIdPath = Path.Combine(dataDirectory, "next-id");
            _removedPath = Path.Combine(dataDirectory, "removed.json");
        }

        public List<Conversation> LoadAll()
        {
            if (!Directory.Exists(_conversationsDirectory))
                return new List<Conversation>();

            return Directory.GetFiles(_conversationsDirectory, "*.json")
                .Select(path => JsonSerializer.Deserialize<Conversation>(File.ReadAllText(path), JsonOptions))
                .Where(c => c != null)
                .OrderBy(c => c.Id)
                .ToList();
        }

        public Conversation Load(int id)
        {
            var path = FilePath(id);
            return File.Exists(path) ? JsonSerializer.Deserialize<Conversation>(File.ReadAllText(path), JsonOptions) : null;
        }

        // Conversation numbers are never reused - "#12" in the changelog and in commit messages must
        // always mean the same conversation, even after it's been removed. A counter file, seeded
        // from what exists if it's missing.
        public int NextId()
        {
            var next = 1;
            if (File.Exists(_nextIdPath) && int.TryParse(File.ReadAllText(_nextIdPath).Trim(), out var stored))
                next = stored;

            var existing = Directory.Exists(_conversationsDirectory)
                ? Directory.GetFiles(_conversationsDirectory, "*.json").Select(p => int.TryParse(Path.GetFileNameWithoutExtension(p), out var id) ? id : 0)
                : Enumerable.Empty<int>();
            var highest = existing.Concat(LoadRemoved().Select(r => r.ConversationId)).DefaultIfEmpty(0).Max();
            next = System.Math.Max(next, highest + 1);

            Directory.CreateDirectory(Path.GetDirectoryName(_nextIdPath));
            File.WriteAllText(_nextIdPath, (next + 1).ToString());
            return next;
        }

        public void Delete(int id)
        {
            var path = FilePath(id);
            if (File.Exists(path))
                File.Delete(path);
        }

        public List<RemovedConversation> LoadRemoved() =>
            File.Exists(_removedPath)
                ? JsonSerializer.Deserialize<List<RemovedConversation>>(File.ReadAllText(_removedPath), JsonOptions) ?? new List<RemovedConversation>()
                : new List<RemovedConversation>();

        public void SaveRemoved(List<RemovedConversation> removed)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_removedPath));
            var tempPath = _removedPath + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(removed, JsonOptions));
            if (File.Exists(_removedPath))
                File.Replace(tempPath, _removedPath, null);
            else
                File.Move(tempPath, _removedPath);
        }

        // Write-to-temp-then-replace, so a crash mid-write never leaves a half-written conversation.
        public void Save(Conversation conversation)
        {
            Directory.CreateDirectory(_conversationsDirectory);
            var path = FilePath(conversation.Id);
            var tempPath = path + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(conversation, JsonOptions));
            if (File.Exists(path))
                File.Replace(tempPath, path, null);
            else
                File.Move(tempPath, path);
        }

        private string FilePath(int id) => Path.Combine(_conversationsDirectory, id + ".json");
    }
}
