import assert from "node:assert/strict";
import { test } from "node:test";
import { loadConfig } from "../lib/config.mjs";
import { createWorkspace, redact } from "../lib/workspace.mjs";

const URL_WITH_CREDS = "https://$chillin-snabbtest:hemligt@chillin-snabbtest.scm.azurewebsites.net/chillin-snabbtest.git";

function workspace(gitResult = () => ({ code: 0, stdout: "", output: "" })) {
  const calls = [];
  const logs = [];
  const runImpl = async (cmd, args) => {
    calls.push(args.join(" "));
    if (args[0] === "rev-parse") return { code: 0, stdout: "abc\n", output: "" };
    return gitResult(args);
  };
  const ws = createWorkspace({ cwd: "/p", branch: "test", buildCommand: "b", testCommand: "t", testGreenTag: "g", commandTimeoutMinutes: 1, deployGitUrl: URL_WITH_CREDS, log: (m) => logs.push(m), runImpl });
  return { ws, calls, logs };
}

test("deploy: every push to the branch is pushed to Azure Local Git too, forced", async () => {
  const { ws, calls } = workspace();
  await ws.commitAndPush("#7: X");

  const pushes = calls.filter((c) => c.startsWith("push"));
  assert.deepEqual(pushes, ["push origin HEAD:test", `push --force ${URL_WITH_CREDS} HEAD:master`]);
});

test("deploy: a failed Azure push is logged without the password, and doesn't fail the round", async () => {
  const { ws, logs } = workspace((args) => args.includes("--force")
    ? { code: 128, stdout: "", output: `fatal: unable to access '${URL_WITH_CREDS}': 401` }
    : { code: 0, stdout: "", output: "" });

  assert.equal(await ws.commitAndPush("#7: X"), "abc");
  assert.ok(logs.some((l) => l.includes("Kunde inte pusha till Azure")));
  assert.ok(!logs.join("\n").includes("hemligt"), "Password never logged.");
});

test("deploy: no Azure URL configured -> only GitHub", async () => {
  const calls = [];
  const runImpl = async (cmd, args) => { calls.push(args.join(" ")); return { code: 0, stdout: "x", output: "" }; };
  const ws = createWorkspace({ cwd: "/p", branch: "test", buildCommand: "b", testCommand: "t", testGreenTag: "g", commandTimeoutMinutes: 1, runImpl });
  await ws.commitAndPush("#7: X");
  assert.deepEqual(calls.filter((c) => c.startsWith("push")), ["push origin HEAD:test"]);
});

test("redact: credentials in URLs are hidden", () => {
  assert.equal(redact(`x ${URL_WITH_CREDS} y`), "x https://***@chillin-snabbtest.scm.azurewebsites.net/chillin-snabbtest.git y");
  assert.equal(redact("https://github.com/a/b.git"), "https://github.com/a/b.git");
});

test("config: the Azure URL comes from a file outside the repo, and must carry credentials", () => {
  const files = (map) => (p) => { for (const [k, v] of Object.entries(map)) if (p.endsWith(k)) return v; throw new Error("ENOENT"); };
  const base = JSON.stringify({ apiUrl: "https://a/", buildCommand: "b", testCommand: "t", deployGitUrlFile: "/secrets/azure" });

  const ok = loadConfig({ configPath: "/p/claudeborgen.json", env: { CLAUDEBORGEN_TOKEN: "t" }, readFile: files({ "claudeborgen.json": base, "/secrets/azure": URL_WITH_CREDS + "\n" }) });
  assert.equal(ok.deployGitUrl, URL_WITH_CREDS);

  assert.throws(() => loadConfig({ configPath: "/p/claudeborgen.json", env: { CLAUDEBORGEN_TOKEN: "t" }, readFile: files({ "claudeborgen.json": base, "/secrets/azure": "https://chillin.scm.azurewebsites.net/chillin.git" }) }), /med uppgifter/);

  const missing = loadConfig({ configPath: "/p/claudeborgen.json", env: { CLAUDEBORGEN_TOKEN: "t" }, readFile: files({ "claudeborgen.json": base }) });
  assert.equal(missing.deployGitUrl, null, "A missing file doesn't stop the runner...");
  assert.match(missing.deployGitWarning, /driftsätter inte/, "...but it says so.");
});
