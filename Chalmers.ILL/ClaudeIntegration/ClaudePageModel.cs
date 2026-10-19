using System.Collections.Generic;
using Chalmers.ILL.Models.Page;

namespace Chalmers.ILL.ClaudeIntegration
{
    // Inherits Chillin's layout model - one of the two places (with the layout itself) where the
    // pages are glue to the host app rather than host-independent; see claudeborgen-DESIGN.md.
    public class ClaudePageModel : ChalmersILLModel
    {
        public ClaudeIntegrationSettings Settings { get; set; }
        public ClaudeAccess Access { get; set; }
        public ClaudeStatus Status { get; set; }
        public List<Conversation> Conversations { get; set; } = new List<Conversation>();
        public List<Conversation> NeedingAttention { get; set; } = new List<Conversation>();
        public Conversation Conversation { get; set; }
        public string Error { get; set; }
    }
}
