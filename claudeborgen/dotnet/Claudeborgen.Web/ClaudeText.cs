using System;

namespace Claudeborgen
{
    // Swedish labels for the views, kept in one place.
    public static class ClaudeText
    {
        public static string State(ConversationState state) => state switch
        {
            ConversationState.WaitingForClaude => "Väntar på Claude",
            ConversationState.ClaudeWorking => "Claude arbetar",
            ConversationState.WaitingForOwner => "Väntar på objektspecialisten",
            ConversationState.Done => "Klar",
            ConversationState.Cancelled => "Avbruten",
            _ => state.ToString()
        };

        public static string StateLabelClass(ConversationState state) => state switch
        {
            ConversationState.WaitingForClaude => "label-info",
            ConversationState.ClaudeWorking => "label-primary",
            ConversationState.WaitingForOwner => "label-warning",
            ConversationState.Done => "label-success",
            _ => "label-default"
        };

        public static string Tests(TestStatus result) => result switch
        {
            TestStatus.Green => "Gröna tester",
            TestStatus.Red => "Röda tester",
            _ => "Tester körs"
        };

        public static string TestsLabelClass(TestStatus result) => result switch
        {
            TestStatus.Green => "label-success",
            TestStatus.Red => "label-danger",
            _ => "label-default"
        };

        public static string Time(DateTime? at) => at?.ToString("yyyy-MM-dd HH:mm") ?? "";

        public static string ShortCommit(string commit) =>
            commit == null ? "" : commit.Length > 10 ? commit.Substring(0, 10) : commit;
    }
}
