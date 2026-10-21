// Claude Code, headless: `claude -p ... --output-format json`. Full permissions inside the
// devcontainer (its firewall and repo-scoped deploy key are the sandbox), a budget and a time
// limit per call, and --resume so a conversation keeps its memory across rounds.
import { quotaFrom } from "./errors.mjs";
import { run, tail } from "./exec.mjs";
import { SYSTEM_PROMPT } from "./prompt.mjs";

// `model`/`fallbackModel` (claudeborgen.json) are passed as --model/--fallback-model when set; without
// them Claude Code uses whatever default the container's own settings say, which then silently
// follows the last interactive /model choice - so projects should set them.
export function createClaude({ command = "claude", cwd, maxBudgetUsd, timeoutMinutes, model = null, fallbackModel = null, runImpl = run }) {
  return async function claude(prompt, { sessionId } = {}) {
    const args = [
      "-p",
      "--output-format", "json",
      "--dangerously-skip-permissions",
      "--append-system-prompt", SYSTEM_PROMPT,
    ];
    if (maxBudgetUsd) args.push("--max-budget-usd", String(maxBudgetUsd));
    if (model) args.push("--model", model);
    if (fallbackModel) args.push("--fallback-model", fallbackModel);
    if (sessionId) args.push("--resume", sessionId);

    const result = await runImpl(command, args, { cwd, timeoutMs: timeoutMinutes * 60 * 1000, input: prompt });
    if (result.timedOut) throw new Error(`Claude svarade inte inom ${timeoutMinutes} minuter.`);

    let json;
    try {
      json = JSON.parse(result.stdout);
    } catch {
      const quota = quotaFrom({ text: result.output });
      if (quota) throw quota;
      throw new Error(`Claude avslutades (kod ${result.code}) utan läsbart svar: ${tail(result.output, 1000)}`);
    }
    if (json.is_error) {
      const quota = quotaFrom({ apiErrorStatus: json.api_error_status, text: json.result });
      if (quota) throw quota;
      throw new Error(`Claude rapporterade ett fel: ${tail(String(json.result ?? json.subtype), 1000)}`);
    }

    return {
      text: json.result ?? "",
      sessionId: json.session_id,
      costUsd: json.total_cost_usd ?? 0,
      durationMs: json.duration_ms ?? 0,
    };
  };
}
