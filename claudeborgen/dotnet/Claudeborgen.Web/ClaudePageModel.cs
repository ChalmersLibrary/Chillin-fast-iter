using System.Collections.Generic;

namespace Claudeborgen
{
    public class ClaudePageModel
    {
        public ClaudeborgenSettings Settings { get; set; }
        public ClaudeAccess Access { get; set; }
        public ClaudeStatus Status { get; set; }
        public List<Conversation> Conversations { get; set; } = new List<Conversation>();
        public List<Conversation> NeedingAttention { get; set; } = new List<Conversation>();
        public Conversation Conversation { get; set; }
        public string Error { get; set; }
    }
}
