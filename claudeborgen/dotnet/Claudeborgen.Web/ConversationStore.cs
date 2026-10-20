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

        public ConversationStore(string dataDirectory)
        {
            _conversationsDirectory = Path.Combine(dataDirectory, "conversations");
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

        public int NextId()
        {
            if (!Directory.Exists(_conversationsDirectory))
                return 1;

            var ids = Directory.GetFiles(_conversationsDirectory, "*.json")
                .Select(p => int.TryParse(Path.GetFileNameWithoutExtension(p), out var id) ? id : 0)
                .ToList();
            return ids.Count == 0 ? 1 : ids.Max() + 1;
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
