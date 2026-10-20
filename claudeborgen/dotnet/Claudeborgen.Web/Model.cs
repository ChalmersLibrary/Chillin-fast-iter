using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Claudeborgen
{
    // A conversation is one task the object owner drives with Claude (see claudeborgen/DESIGN.md).
    // Every human message starts a round (implement -> push -> deploy -> reply), until the owner
    // says "klar" and the conversation lands in the changelog. Serialized with System.Text.Json both
    // to disk (ConversationStore) and over the agent API (camelCase there, see PROTOCOL.md).
    public enum ConversationState
    {
        WaitingForClaude,
        ClaudeWorking,
        WaitingForOwner,
        Done,
        Cancelled
    }

    public enum MessageAuthorKind
    {
        Human,
        Claude,
        System
    }

    public enum TestStatus
    {
        Pending,
        Green,
        Red
    }

    public enum RoundOutcome
    {
        Delivered,
        Crashed
    }

    public class ConversationMessage
    {
        public MessageAuthorKind AuthorKind { get; set; }
        public string Author { get; set; }
        public string Body { get; set; }
        public DateTime At { get; set; }
    }

    public class Round
    {
        public int Number { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime? FinishedAt { get; set; }
        public RoundOutcome? Outcome { get; set; }
        public List<string> Commits { get; set; } = new List<string>();
        public List<string> Links { get; set; } = new List<string>();
        public string SessionId { get; set; }
        public TestStatus Tests { get; set; } = TestStatus.Pending;
        public string TestSummary { get; set; }
        public decimal? CostUsd { get; set; }
        public int? DurationSeconds { get; set; }

        // A round started automatically because the previous round's tests were red, rather than
        // by a human message.
        public bool IsFixRound { get; set; }

        // How many messages the conversation had when the round started - what "a message written
        // during the round" is measured against (by position, not by clock).
        public int MessageCountAtStart { get; set; }
    }

    public class Conversation
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        public ConversationState State { get; set; }
        public List<ConversationMessage> Messages { get; set; } = new List<ConversationMessage>();
        public List<Round> Rounds { get; set; } = new List<Round>();

        // Claude Code session to --resume, so Claude remembers the earlier rounds.
        public string SessionId { get; set; }

        // Red-test fix rounds in a row; reset by green tests or a new human message.
        public int ConsecutiveRedRounds { get; set; }

        // Set when Claude can't get further on its own (tests stay red, a round crashed); shown to
        // ObjektSpecialistIT in the attention list. Cleared by the next human message.
        public bool NeedsIt { get; set; }
        public string NeedsItReason { get; set; }

        public DateTime? ClaimedAt { get; set; }
        public DateTime? DoneAt { get; set; }
        public string DoneBy { get; set; }
        public string ChangelogText { get; set; }

        [JsonIgnore]
        public DateTime LastActivityAt => Messages.Count > 0 ? Messages.Max(m => m.At) : CreatedAt;
        [JsonIgnore]
        public Round CurrentRound => Rounds.LastOrDefault();
        [JsonIgnore]
        public bool IsOpen => State != ConversationState.Done && State != ConversationState.Cancelled;
    }

    public enum ClaudeActivity
    {
        Idle,
        Working,
        Offline
    }

    public class ClaudeStatus
    {
        public ClaudeActivity Activity { get; set; }
        public int? ConversationId { get; set; }
        public string ConversationTitle { get; set; }
        public string Phase { get; set; }
        public DateTime? LastHeartbeatAt { get; set; }
        public int QueueLength { get; set; }
    }
}
