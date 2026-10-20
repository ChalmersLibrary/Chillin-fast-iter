import assert from "node:assert/strict";
import { test } from "node:test";
import { createApi } from "../lib/api.mjs";
import { createClaude } from "../lib/claude.mjs";
import { loadConfig } from "../lib/config.mjs";
import { createFakes } from "../lib/fake.mjs";
import { buildPrompt } from "../lib/prompt.mjs";
import { runRound } from "../lib/round.mjs";
import { createWorkspace } from "../lib/workspace.mjs";
import { CONFIG, conversation, fakeApi, heartbeatRecorder } from "./helpers.mjs";

// --- config -----------------------------------------------------------------------------------

function files(map) {
  return (p) => {
    for (const [suffix, content] of Object.entries(map)) if (p.endsWith(suffix)) return content;
    throw new Error("ENOENT " + p);
  };
}

test("config: file + defaults + token from file", () => {
  const config = loadConfig({
    configPath: "/proj/claudeborgen.json",
    env: {},
    readFile: files({
      "claudeborgen.json": JSON.stringify({ apiUrl: "https://app.example", buildCommand: "make", testCommand: "make test", tokenFile: "/secrets/t" }),
      "/secrets/t": "hemlig\n",
    }),
  });
  assert.equal(config.apiUrl, "https://app.example/");
  assert.equal(config.token, "hemlig");
  assert.equal(config.branch, "test");
  assert.equal(config.projectRoot, "/proj");
});

test("config: environment overrides", () => {
  const config = loadConfig({
    configPath: "/proj/claudeborgen.json",
    env: { CLAUDEBORGEN_API_URL: "https://other.example/", CLAUDEBORGEN_TOKEN: "env-token" },
    readFile: files({ "claudeborgen.json": JSON.stringify({ apiUrl: "https://app.example/", buildCommand: "b", testCommand: "t" }) }),
  });
  assert.equal(config.apiUrl, "https://other.example/");
  assert.equal(config.token, "env-token");
});

test("config: missing required settings or token are explained", () => {
  assert.throws(
    () => loadConfig({ configPath: "/p/claudeborgen.json", env: {}, readFile: files({ "claudeborgen.json": "{}" }) }),
    /saknar: apiUrl, buildCommand, testCommand/);
  assert.throws(
    () => loadConfig({ configPath: "/p/claudeborgen.json", env: {}, readFile: files({ "claudeborgen.json": JSON.stringify({ apiUrl: "x", buildCommand: "b", testCommand: "t" }) }) }),
    /agent-tokenen/);
});

// --- api --------------------------------------------------------------------------------------

test("api: token and fake header on every call, camelCase bodies, errors carry the message", async () => {
  const requests = [];
  const fetchImpl = async (url, init) => {
    requests.push({ url: String(url), init });
    if (String(url).endsWith("/claim")) return new Response(JSON.stringify({ message: "Ett samtal i taget." }), { status: 409 });
    return new Response(null, { status: 204 });
  };
  const api = createApi({ apiUrl: "https://app.example/sub/", token: "tok", fake: true, fetchImpl });

  await api.heartbeat("working", 3, "bygger");
  assert.equal(requests[0].url, "https://app.example/sub/api/claude/heartbeat");
  assert.equal(requests[0].init.headers.Authorization, "Bearer tok");
  assert.equal(requests[0].init.headers["X-Claudeborgen-Fake-Runner"], "true");
  assert.deepEqual(JSON.parse(requests[0].init.body), { activity: "working", conversationId: 3, phase: "bygger" });

  await assert.rejects(api.claim(3), (e) => e.status === 409 && e.message === "Ett samtal i taget.");
});

// --- claude -----------------------------------------------------------------------------------

test("claude: headless JSON call with budget, permissions, resume; parses the result", async () => {
  let seen;
  const runImpl = async (command, args, options) => {
    seen = { command, args, options };
    return { code: 0, stdout: JSON.stringify({ type: "result", is_error: false, result: "Klart", session_id: "s-9", total_cost_usd: 0.5, duration_ms: 1200 }), output: "" };
  };
  const claude = createClaude({ command: "claude", cwd: "/proj", maxBudgetUsd: 5, timeoutMinutes: 45, runImpl });

  const answer = await claude("Gör X", { sessionId: "s-8" });

  assert.deepEqual(answer, { text: "Klart", sessionId: "s-9", costUsd: 0.5, durationMs: 1200 });
  assert.equal(seen.command, "claude");
  assert.equal(seen.options.input, "Gör X");
  assert.equal(seen.options.cwd, "/proj");
  for (const flag of ["-p", "--dangerously-skip-permissions", "--append-system-prompt"]) assert.ok(seen.args.includes(flag), flag);
  assert.deepEqual(seen.args.slice(seen.args.indexOf("--output-format"), seen.args.indexOf("--output-format") + 2), ["--output-format", "json"]);
  assert.deepEqual(seen.args.slice(seen.args.indexOf("--max-budget-usd"), seen.args.indexOf("--max-budget-usd") + 2), ["--max-budget-usd", "5"]);
  assert.deepEqual(seen.args.slice(seen.args.indexOf("--resume"), seen.args.indexOf("--resume") + 2), ["--resume", "s-8"]);
});

test("claude: error result, garbage output and timeout all throw", async () => {
  const make = (result) => createClaude({ cwd: "/", maxBudgetUsd: 1, timeoutMinutes: 1, runImpl: async () => result });
  await assert.rejects(make({ code: 0, stdout: JSON.stringify({ is_error: true, result: "budget exceeded" }), output: "" })("x"), /budget exceeded/);
  await assert.rejects(make({ code: 1, stdout: "not json", output: "boom" })("x"), /boom/);
  await assert.rejects(make({ timedOut: true, stdout: "", output: "" })("x"), /inom 1 minuter/);
});

// --- prompt -----------------------------------------------------------------------------------

test("prompt: a new session gets the whole conversation, with authors", () => {
  const prompt = buildPrompt(conversation({
    messages: [
      { authorKind: "human", author: "agneta", body: "Gör X" },
      { authorKind: "claude", author: "Claude", body: "Klart" },
      { authorKind: "system", author: "System", body: "Testerna är röda" },
    ],
  }), { resume: false });
  assert.match(prompt, /Samtal #7: "Exportknapp"/);
  assert.match(prompt, /\[agneta\]\nGör X/);
  assert.match(prompt, /\[Claude\]\nKlart/);
  assert.match(prompt, /\[System\]\nTesterna är röda/);
});

// --- workspace (real git/build commands, faked process runner) -------------------------------

test("workspace: refuses a dirty tree, pulls the branch, commits and pushes, retries a rejected push once", async () => {
  const gitCalls = [];
  let pushes = 0;
  let status = " M file.cs";
  const runImpl = async (command, args) => {
    gitCalls.push(args.join(" "));
    if (args[0] === "status") return { code: 0, stdout: status, output: "" };
    if (args[0] === "push" && args[2].startsWith("HEAD:") && ++pushes === 1) return { code: 1, stdout: "", output: "rejected" };
    if (args[0] === "rev-parse") return { code: 0, stdout: "deadbeef\n", output: "" };
    return { code: 0, stdout: "", output: "" };
  };
  const ws = createWorkspace({ cwd: "/proj", branch: "test", buildCommand: "b", testCommand: "t", testGreenTag: "test-green", commandTimeoutMinutes: 1, runImpl, shellImpl: async () => ({ code: 0, output: "ok" }) });

  await assert.rejects(ws.prepare(), /oincheckade ändringar/);

  status = "";
  await ws.prepare();
  assert.deepEqual(gitCalls.slice(-3), ["fetch origin test", "checkout test", "pull --rebase origin test"]);

  const sha = await ws.commitAndPush("#7: Exportknapp");
  assert.equal(sha, "deadbeef");
  assert.ok(gitCalls.includes("commit -m #7: Exportknapp"));
  assert.equal(gitCalls.filter((c) => c === "push origin HEAD:test").length, 2);

  await ws.markGreen("deadbeef");
  assert.ok(gitCalls.includes("tag -f test-green deadbeef"));
  assert.ok(gitCalls.includes("push -f origin refs/tags/test-green"));
});

test("workspace: build/test report ok and an output tail", async () => {
  const ws = createWorkspace({ cwd: "/", branch: "test", buildCommand: "b", testCommand: "t", testGreenTag: "g", commandTimeoutMinutes: 1, runImpl: async () => ({ code: 0, stdout: "" }), shellImpl: async (line) => ({ code: line === "b" ? 0 : 1, output: `ran ${line}` }) });
  assert.deepEqual(await ws.build(), { ok: true, summary: "ran b" });
  assert.deepEqual(await ws.test(), { ok: false, summary: "ran t" });
});

// --- fake mode --------------------------------------------------------------------------------

async function fakeRound(body, round = { number: 1, isFixRound: false }) {
  const heartbeat = heartbeatRecorder();
  const fakes = createFakes({ heartbeat, offlineMs: 0, phaseDelayMs: 0 });
  const api = fakeApi({ conversation: conversation({ messages: [{ authorKind: "human", author: "agneta", body }] }), round });
  await runRound({ api, heartbeat, config: CONFIG, conversationId: 7, ...fakes });
  return api;
}

test("fake: a plain message gives a green delivered round with a fake commit", async () => {
  const api = await fakeRound("Gör X");
  const reply = api.call("reply").args[1];
  assert.match(reply.body, /Fejkad runner/);
  assert.match(reply.commits[0], /^fejk/);
  assert.equal(api.call("tests").args[2], true);
});

test("fake: [röd] is red once, green in the fix round; [röd!] stays red; [krasch] crashes", async () => {
  assert.equal((await fakeRound("Gör X [röd]")).call("tests").args[2], false);
  assert.equal((await fakeRound("Gör X [röd]", { number: 2, isFixRound: true })).call("tests").args[2], true);
  assert.equal((await fakeRound("Gör X [röd!]", { number: 3, isFixRound: true })).call("tests").args[2], false);
  assert.match((await fakeRound("Gör X [krasch]")).call("crash").args[1], /krascha/);
});
