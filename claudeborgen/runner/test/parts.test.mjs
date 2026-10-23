import assert from "node:assert/strict";
import { test } from "node:test";
import { createApi } from "../lib/api.mjs";
import { QuotaExhausted, quotaFrom } from "../lib/errors.mjs";
import { Heartbeat } from "../lib/heartbeat.mjs";
import { createClaude } from "../lib/claude.mjs";
import { loadConfig } from "../lib/config.mjs";
import { createFakes } from "../lib/fake.mjs";
import { buildPrompt, parseChangelog, parseQuestion, SYSTEM_PROMPT } from "../lib/prompt.mjs";
import { commitChangelog, runRound } from "../lib/round.mjs";
import { addChangelogEntry, createWorkspace } from "../lib/workspace.mjs";
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

// --- changelog ------------------------------------------------------------------------------


function memoryFiles(initial) {
  const files = { ...initial };
  return {
    files,
    readFile: (p) => { if (!(p in files)) { const e = new Error("ENOENT"); e.code = "ENOENT"; throw e; } return files[p]; },
    writeFile: (p, content) => { files[p] = content; },
  };
}

test("changelog: a done conversation's entry goes on top, once", () => {
  const fs = memoryFiles({ "/p/changelog.json": JSON.stringify([{ date: "2025-09-09", version: "v2.6.3", items: ["Gammalt"] }]) });
  const entry = { conversationId: 12, date: "2026-10-21", text: "Exportknapp" };

  assert.equal(addChangelogEntry("/p/changelog.json", entry, fs), true);
  assert.equal(addChangelogEntry("/p/changelog.json", entry, fs), false, "already there - nothing to add");

  const written = JSON.parse(fs.files["/p/changelog.json"]);
  assert.deepEqual(written[0], { date: "2026-10-21", items: ["Exportknapp"], conversationId: 12 });
  assert.equal(written[1].version, "v2.6.3");
  assert.equal(written.length, 2);
});

test("changelog: a missing file starts a new one; a broken one is an error, not overwritten", () => {
  const fs = memoryFiles({});
  addChangelogEntry("/p/changelog.json", { conversationId: 1, date: "2026-10-21", text: "X" }, fs);
  assert.equal(JSON.parse(fs.files["/p/changelog.json"]).length, 1);

  const broken = memoryFiles({ "/p/changelog.json": "{ inte json" });
  assert.throws(() => addChangelogEntry("/p/changelog.json", { conversationId: 1, date: "d", text: "X" }, broken), /Kunde inte läsa/);
  assert.equal(broken.files["/p/changelog.json"], "{ inte json");
});

test("changelog: workspace pulls, writes, commits and pushes as its own commit", async () => {
  const gitCalls = [];
  const fs = memoryFiles({ "/proj/Chalmers.ILL/changelog.json": "[]" });
  const runImpl = async (command, args) => {
    gitCalls.push(args.join(" "));
    if (args[0] === "status") return { code: 0, stdout: "", output: "" };
    if (args[0] === "rev-parse") return { code: 0, stdout: "c0ffee\n", output: "" };
    return { code: 0, stdout: "", output: "" };
  };
  const ws = createWorkspace({ cwd: "/proj", branch: "test", buildCommand: "b", testCommand: "t", testGreenTag: "g", commandTimeoutMinutes: 1, changelogFile: "Chalmers.ILL/changelog.json", runImpl, files: fs });

  const commit = await ws.commitChangelogEntry({ conversationId: 12, date: "2026-10-21", text: "Exportknapp" });

  assert.equal(commit, "c0ffee");
  assert.ok(gitCalls.includes("pull --rebase origin test"));
  assert.ok(gitCalls.includes("commit -m Changelog: #12 Exportknapp"));
  assert.ok(gitCalls.includes("push origin HEAD:test"));
  assert.equal(JSON.parse(fs.files["/proj/Chalmers.ILL/changelog.json"])[0].conversationId, 12);
});

test("changelog: the runner reports each committed entry; a failure is only logged", async () => {
  const reported = [];
  const logs = [];
  const api = {
    pendingChangelog: async () => [{ conversationId: 1, date: "d", text: "Ett" }, { conversationId: 2, date: "d", text: "Två" }],
    changelogCommitted: async (id, commit) => { reported.push([id, commit]); },
  };
  const workspace = {
    commitChangelogEntry: async (entry) => { if (entry.conversationId === 2) throw new Error("push rejected"); return "abc"; },
  };
  await commitChangelog({ api, heartbeat: heartbeatRecorder(), workspace, log: (m) => logs.push(m) });

  assert.deepEqual(reported, [[1, "abc"]]);
  assert.ok(logs.some((l) => /#2: kunde inte committa/.test(l)));
});

test("changelog: the fake runner never commits", async () => {
  const fakes = createFakes({ heartbeat: heartbeatRecorder(), offlineMs: 0, phaseDelayMs: 0 });
  assert.match(await fakes.workspace.commitChangelogEntry({ conversationId: 1, date: "d", text: "X" }), /^fejk/);
});

// --- quota ----------------------------------------------------------------------------------

test("quota: recognized from HTTP 429 or limit wording, with the reset time when given", () => {
  assert.ok(quotaFrom({ apiErrorStatus: 429, text: "Too many requests" }) instanceof QuotaExhausted);
  const sub = quotaFrom({ text: "Claude AI usage limit reached|1761000000" });
  assert.equal(sub.resetAt.getTime(), 1761000000 * 1000);
  assert.equal(quotaFrom({ text: "Rate limit exceeded" }).resetAt, null);
  assert.equal(quotaFrom({ apiErrorStatus: 500, text: "Internal server error" }), null);
  assert.equal(quotaFrom({ text: "TypeError: x is undefined" }), null);
});

test("claude: a limit error becomes QuotaExhausted, both as JSON and as plain output", async () => {
  const make = (result) => createClaude({ cwd: "/", timeoutMinutes: 1, runImpl: async () => result });
  await assert.rejects(make({ code: 0, stdout: JSON.stringify({ is_error: true, api_error_status: 429, result: "rate limited" }), output: "" })("x"), QuotaExhausted);
  await assert.rejects(make({ code: 1, stdout: "", output: "Claude AI usage limit reached|1761000000" })("x"), QuotaExhausted);
});

test("claude: no --max-budget-usd unless a per-round limit is configured", async () => {
  let args;
  const claude = createClaude({ cwd: "/", timeoutMinutes: 1, maxBudgetUsd: null, runImpl: async (c, a) => { args = a; return { code: 0, stdout: JSON.stringify({ result: "ok", session_id: "s" }), output: "" }; } });
  await claude("x");
  assert.ok(!args.includes("--max-budget-usd"));
});

test("heartbeat: paused carries the reason and when it tries again", async () => {
  const sent = [];
  const hb = new Heartbeat({ api: { heartbeat: async (...a) => { sent.push(a); } }, intervalSeconds: 60 });
  const until = new Date("2026-10-21T14:00:00Z");
  await hb.paused(until, "kvoten är slut");
  assert.deepEqual(sent.at(-1), ["paused", null, "kvoten är slut", until]);
});

test("api: heartbeat sends pausedUntil as ISO", async () => {
  let body;
  const api = createApi({ apiUrl: "https://a/", token: "t", fetchImpl: async (u, init) => { body = JSON.parse(init.body); return new Response(null, { status: 204 }); } });
  await api.heartbeat("paused", null, "kvoten är slut", new Date("2026-10-21T14:00:00Z"));
  assert.equal(body.pausedUntil, "2026-10-21T14:00:00.000Z");
});

test("fake: [kvot] pauses once per conversation, then the round runs", async () => {
  const heartbeat = heartbeatRecorder();
  const fakes = createFakes({ heartbeat, offlineMs: 0, phaseDelayMs: 0, quotaPauseMs: 1000 });
  const conv = conversation({ messages: [{ authorKind: "human", author: "agneta", body: "Gör X [kvot]" }] });

  const first = fakeApi({ conversation: conv });
  first.release = async () => { first.calls.push({ name: "release", args: [] }); };
  const r1 = await runRound({ api: first, heartbeat, config: CONFIG, conversationId: 7, ...fakes });
  assert.equal(r1.paused, true);
  assert.ok(first.names().includes("release"));

  const second = fakeApi({ conversation: conv });
  const r2 = await runRound({ api: second, heartbeat, config: CONFIG, conversationId: 7, ...fakes });
  assert.equal(r2.paused, false);
  assert.ok(second.names().includes("reply"));
});

// --- questions ------------------------------------------------------------------------------


test("question marker: FRÅGA:/fråga:/FRAGA: at the start, nowhere else", () => {
  assert.deepEqual(parseQuestion("FRÅGA: Överst eller nederst?"), { body: "Överst eller nederst?", question: true });
  assert.deepEqual(parseQuestion("  fråga:\nVilken färg?"), { body: "Vilken färg?", question: true });
  assert.equal(parseQuestion("FRAGA: utan prickar").question, true);
  assert.equal(parseQuestion("Klart. En fråga: ska den vara grön?").question, false);
});

test("changelog line: the last CHANGELOG: line is taken out of the reply", () => {
  assert.deepEqual(parseChangelog("Klart!\nCHANGELOG: Exportknapp."), { body: "Klart!", changelogText: "Exportknapp." });
  assert.deepEqual(parseChangelog("Klart!\n**CHANGELOG:** Fet"), { body: "Klart!", changelogText: "Fet" });
  assert.deepEqual(parseChangelog("Bara ett svar."), { body: "Bara ett svar.", changelogText: null });
  assert.equal(parseChangelog("Om changelog: inget").changelogText, null);
});

test("prompt: the changelog text so far is there to improve on, marked when the owner wrote it", () => {
  assert.match(buildPrompt(conversation({ changelogText: "Exportknapp" }), { resume: false }), /Nuvarande changelog-text: "Exportknapp"$/);
  assert.match(buildPrompt(conversation({ changelogText: "Min text", changelogTextByHuman: true }), { resume: false }), /skriven av objektspecialisten/);
  assert.doesNotMatch(buildPrompt(conversation(), { resume: false }), /changelog-text/);
});

test("system prompt: big ambiguities ask without changing files, small ones are decided", () => {
  assert.match(SYSTEM_PROMPT, /Stora oklarheter/);
  assert.match(SYSTEM_PROMPT, /FRÅGA:/);
  assert.match(SYSTEM_PROMPT, /Små oklarheter/);
});

test("fake: [fråga] asks a question and changes nothing", async () => {
  const api = await fakeRound("Lägg till en knapp [fråga]");
  const reply = api.call("reply").args[1];
  assert.equal(reply.question, true);
  assert.deepEqual(reply.commits, []);
  assert.ok(!api.names().includes("tests"));
});

// --- model ----------------------------------------------------------------------------------

test("claude: model and fallback model are passed when configured, left to Claude Code otherwise", async () => {
  const seen = [];
  const runImpl = async (c, args) => { seen.push(args); return { code: 0, stdout: JSON.stringify({ result: "ok", session_id: "s" }), output: "" }; };

  await createClaude({ cwd: "/", timeoutMinutes: 1, model: "opus", fallbackModel: "sonnet", runImpl })("x");
  const flag = (args, name) => args[args.indexOf(name) + 1];
  assert.equal(flag(seen[0], "--model"), "opus");
  assert.equal(flag(seen[0], "--fallback-model"), "sonnet");

  await createClaude({ cwd: "/", timeoutMinutes: 1, runImpl })("x");
  assert.ok(!seen[1].includes("--model"));
  assert.ok(!seen[1].includes("--fallback-model"));
});

test("claude: idea mode reads but can't write - no permission bypass, editing and shell tools off", async () => {
  let args;
  const claude = createClaude({ cwd: "/", timeoutMinutes: 1, runImpl: async (c, a) => { args = a; return { code: 0, stdout: JSON.stringify({ result: "ok", session_id: "s" }), output: "" }; } });

  await claude("x", { mode: "idea" });

  assert.ok(!args.includes("--dangerously-skip-permissions"));
  const disallowed = args[args.indexOf("--disallowedTools") + 1];
  for (const tool of ["Edit", "Write", "MultiEdit", "NotebookEdit", "Bash"]) assert.ok(disallowed.split(",").includes(tool), tool);
  assert.match(args[args.indexOf("--append-system-prompt") + 1], /Ingenting ska byggas än/);
});

test("fake: an idea gets a chat answer", async () => {
  const fakes = createFakes({ heartbeat: heartbeatRecorder(), offlineMs: 0, phaseDelayMs: 0 });
  const answer = await fakes.claude("x", { mode: "idea" });
  assert.match(answer.text, /Fejkad runner/);
});
