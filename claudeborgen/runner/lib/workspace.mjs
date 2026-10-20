// Everything the round does to the project checkout: git, build, tests. One interface, two
// implementations - the real one, and the fake one for --fake (nothing is touched or pushed).
import { RoundFailure } from "./errors.mjs";
import { run, runShell, tail } from "./exec.mjs";

export function createWorkspace({ cwd, branch, buildCommand, testCommand, testGreenTag, commandTimeoutMinutes, runImpl = run, shellImpl = runShell }) {
  const timeoutMs = commandTimeoutMinutes * 60 * 1000;

  async function git(...args) {
    const r = await runImpl("git", args, { cwd, timeoutMs: 5 * 60 * 1000 });
    if (r.code !== 0) throw new Error(`git ${args.join(" ")} misslyckades: ${tail(r.output, 1000)}`);
    return r.stdout.trim();
  }

  async function command(line) {
    const r = await shellImpl(line, { cwd, timeoutMs });
    const summary = r.timedOut ? `Avbröts efter ${commandTimeoutMinutes} minuter.\n${tail(r.output)}` : tail(r.output);
    return { ok: r.code === 0, summary };
  }

  return {
    async hasChanges() {
      return (await git("status", "--porcelain")) !== "";
    },

    // Up to date with the branch before Claude starts. Refuses a dirty tree rather than guessing
    // whose changes those are.
    async prepare() {
      if (await this.hasChanges())
        throw new RoundFailure("Arbetskatalogen har oincheckade ändringar sedan tidigare. Runnern rör dem inte - någon behöver titta (git status).");
      await git("fetch", "origin", branch);
      await git("checkout", branch);
      await git("pull", "--rebase", "origin", branch);
    },

    build: () => command(buildCommand),
    test: () => command(testCommand),

    async commitAndPush(message) {
      await git("add", "-A");
      await git("commit", "-m", message);
      try {
        await git("push", "origin", `HEAD:${branch}`);
      } catch {
        // Someone else pushed meanwhile: rebase onto it once and retry.
        await git("pull", "--rebase", "origin", branch);
        await git("push", "origin", `HEAD:${branch}`);
      }
      return git("rev-parse", "HEAD");
    },

    // Keeps a failed round's changes without leaving the tree dirty for the next round.
    async stash(message) {
      await git("stash", "push", "--include-untracked", "-m", message);
    },

    async markGreen(commit) {
      await git("tag", "-f", testGreenTag, commit);
      await git("push", "-f", "origin", `refs/tags/${testGreenTag}`);
    },
  };
}

// --fake: no git, no build, no Claude changes. Tests are red/green per the markers in the latest
// human message, same markers as the app's in-process FakeClaude ([röd], [röd!], [krasch] - the
// last one handled in the fake Claude). Lets the whole runner <-> app chain be tried on the
// isolated fast test server.
export function createFakeWorkspace({ markers }) {
  let counter = 0;
  return {
    hasChanges: async () => true,
    prepare: async () => {},
    build: async () => ({ ok: true, summary: "Bygget grönt (fejkat)." }),
    async test() {
      const m = markers();
      const red = m.instruction.includes("[röd!]") || (m.instruction.includes("[röd]") && !m.isFixRound);
      return red ? { ok: false, summary: "Fejkrunnern låtsades att 1 test är rött." } : { ok: true, summary: "Alla tester gröna (fejkat)." };
    },
    commitAndPush: async () => `fejk${Date.now().toString(16).slice(-5)}${++counter}`,
    stash: async () => {},
    markGreen: async () => {},
  };
}
