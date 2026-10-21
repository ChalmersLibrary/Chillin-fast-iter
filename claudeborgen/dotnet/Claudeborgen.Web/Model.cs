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
    // DESIGN.md, "Idéer": an idea is a loose chat with Claude on the start page - no title needed,
    // nothing built, Claude may read the code but not change it. The object owner can turn it into a
    // task, and Claude then builds it with the whole idea discussion in mind (same session).
    public enum ConversationKind
    {
        Task,
        Idea
    }

    public enum ConversationState
    {
        WaitingForClaude,
        ClaudeWorking,
        WaitingForOwner,

        // "Avbryt och ta bort arbete" on work that has pushed something: the runner reverts all its
        // commits; the conversation is removed once that's pushed (DESIGN.md, "Avbryta ett arbete").
        Reverting,

        // Claude needs an answer before it can go on (it asked instead of guessing). Behaves like
        // WaitingForOwner - the next human message starts a round - but is shown as a question.
        QuestionForOwner,
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

        // A Claude message that asks the owner something and waits for the answer.
        public bool IsQuestion { get; set; }
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

        // The round ended with Claude asking the owner something.
        public bool AskedQuestion { get; set; }

        // When the runner saw this round's commit live on the fast test server (DESIGN.md,
        // "Driftsatt"). Null while it's still on its way - and for rounds without a commit.
        public DateTime? DeployedAt { get; set; }

        // How many messages the conversation had when the round started - what "a message written
        // during the round" is measured against (by position, not by clock).
        public int MessageCountAtStart { get; set; }
    }

    public class Conversation
    {
        public int Id { get; set; }
        public ConversationKind Kind { get; set; } = ConversationKind.Task;
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

        // The commit that added this conversation's entry to the changelog file (the runner does it
        // after "klar"); null = not yet. A FakeClaude.FakeCommitPrefix value means a fake did it.
        public string ChangelogCommit { get; set; }

        [JsonIgnore]
        public DateTime LastActivityAt => Messages.Count > 0 ? Messages.Max(m => m.At) : CreatedAt;
        [JsonIgnore]
        public Round CurrentRound => Rounds.LastOrDefault();
        [JsonIgnore]
        public bool IsOpen => State != ConversationState.Done && State != ConversationState.Cancelled;

        [JsonIgnore]
        public bool IsIdea => Kind == ConversationKind.Idea;
    }

    public enum ClaudeActivity
    {
        Idle,
        Working,
        Offline,

        // The runner is alive but can't use Claude right now (the account's quota is used up);
        // it retries at PausedUntil. Conversations wait in the queue meanwhile.
        Paused
    }

    // What's left of a conversation after it's been removed (DESIGN.md, "Samtal tas bort"): no
    // discussion, only what the changelog needs until the deployed file has the entry, and which
    // Claude Code sessions the runner still has to delete on its side.
    public class RemovedConversation
    {
        public int ConversationId { get; set; }
        public bool Cancelled { get; set; }
        public DateTime RemovedAt { get; set; }

        // Done conversations only: the changelog entry, shown as "(på väg)" until deployed.
        public string ChangelogDate { get; set; }
        public string ChangelogText { get; set; }
        public string ChangelogCommit { get; set; }

        public List<string> SessionIds { get; set; } = new List<string>();
        public bool SessionsCleaned { get; set; }
    }

    // ObjektSpecialistIT has switched development off for a while (DESIGN.md, "Pausa utvecklingen"),
    // e.g. to work on the branch by hand without merge conflicts.
    public class DevelopmentPause
    {
        public string PausedBy { get; set; }
        public DateTime PausedAt { get; set; }
    }

    public class ClaudeStatus
    {
        public ClaudeActivity Activity { get; set; }
        public int? ConversationId { get; set; }
        public string ConversationTitle { get; set; }
        public string Phase { get; set; }
        public DateTime? LastHeartbeatAt { get; set; }
        public int QueueLength { get; set; }

        // Paused only: when the runner tries again (null = it didn't say).
        public DateTime? PausedUntil { get; set; }

        // Development switched off by IT - nothing that pushes to git is handed out.
        public DevelopmentPause DevelopmentPause { get; set; }
    }
}
