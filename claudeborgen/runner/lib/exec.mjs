// Running commands (build, tests, git, claude) with a timeout, keeping the tail of the output for
// summaries shown to the object owner / IT.
import { spawn } from "node:child_process";

export const TAIL_CHARS = 4000;

export function tail(text, chars = TAIL_CHARS) {
  return text.length <= chars ? text : "…" + text.slice(-chars);
}

// Resolves { code, stdout, stderr, output, timedOut } - never rejects on a non-zero exit, only on
// failing to start at all.
export function run(command, args, { cwd, timeoutMs, input } = {}) {
  return new Promise((resolve, reject) => {
    const child = spawn(command, args, { cwd, stdio: ["pipe", "pipe", "pipe"] });
    let stdout = "";
    let stderr = "";
    let output = "";
    let timedOut = false;

    child.stdout.on("data", (d) => { stdout += d; output += d; });
    child.stderr.on("data", (d) => { stderr += d; output += d; });
    child.on("error", reject);

    const timer = timeoutMs
      ? setTimeout(() => { timedOut = true; child.kill("SIGTERM"); setTimeout(() => child.kill("SIGKILL"), 10000).unref(); }, timeoutMs)
      : null;

    child.on("close", (code) => {
      if (timer) clearTimeout(timer);
      resolve({ code: timedOut ? null : code, stdout, stderr, output, timedOut });
    });

    if (input !== undefined) child.stdin.end(input);
    else child.stdin.end();
  });
}

// A project's build/test command line, run through bash like a person would type it.
export function runShell(commandLine, options) {
  return run("bash", ["-c", commandLine], options);
}
