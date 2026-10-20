import assert from "node:assert/strict";
import { test } from "node:test";
import { createGuards, guardRound, runRound } from "../lib/round.mjs";
import { CONFIG, conversation, fakeApi, fakeClaude, fakeWorkspace, heartbeatRecorder } from "./helpers.mjs";

async function round({ api = fakeApi({ conversation: conversation() }), workspace = fakeWorkspace(), claude = fakeClaude(), heartbeat = heartbeatRecorder() } = {}) {
  await runRound({ api, heartbeat, claude, workspace, conversationId: 7, config: CONFIG });
  return { api, workspace, claude, heartbeat };
}

test("a normal round: claim, Claude, build, push, reply, tests, test-green", async () => {
  const { api, workspace, claude, heartbeat } = await round();

  assert.deepEqual(api.names(), ["claim", "conversation", "reply", "tests"]);
  assert.deepEqual(workspace.names(), ["prepare", "hasChanges", "build", "commitAndPush", "test", "markGreen"]);
  assert.equal(claude.calls.length, 1);

  const reply = api.call("reply").args[1];
  assert.equal(reply.body, "Klart!");
  assert.deepEqual(reply.commits, ["abc1234def"]);
  assert.deepEqual(reply.links, ["https://snabbtest.example/"]);
  assert.equal(reply.sessionId, "s-1");
  assert.equal(reply.costUsd, 0.25);

  assert.deepEqual(api.call("tests").args, [7, "abc1234def", true, "Passed!"]);
  assert.equal(workspace.calls.find((c) => c.name === "commitAndPush").message, "#7: Exportknapp");
  assert.deepEqual(heartbeat.phases(), ["läser in", "implementerar", "bygger", "pushar", "kör tester"]);
  assert.equal(heartbeat.states.at(-1).activity, "idle");
});

test("reply comes before the tests (tests run after the push)", async () => {
  const { api } = await round();
  assert.ok(api.names().indexOf("reply") < api.names().indexOf("tests"));
});

test("no changes (Claude only answered a question): reply without commits, no build, no tests", async () => {
  const { api, workspace } = await round({ workspace: fakeWorkspace({ changes: false }) });

  assert.deepEqual(api.names(), ["claim", "conversation", "reply"]);
  assert.deepEqual(api.call("reply").args[1].commits, []);
  assert.deepEqual(api.call("reply").args[1].links, []);
  assert.ok(!workspace.names().includes("build"));
});

test("red build is fixed by Claude in the same session before anything is pushed", async () => {
  const claude = fakeClaude([
    { text: "Första försöket", sessionId: "s-1", costUsd: 0.2, durationMs: 1 },
    { text: "Rättat bygget, klart!", sessionId: "s-1", costUsd: 0.1, durationMs: 1 },
  ]);
  const { api, workspace } = await round({ claude, workspace: fakeWorkspace({ builds: [false, true] }) });

  assert.equal(claude.calls.length, 2);
  assert.equal(claude.calls[1].options.sessionId, "s-1");
  assert.match(claude.calls[1].prompt, /CS1002/);
  assert.deepEqual(workspace.names().filter((n) => n === "build"), ["build", "build"]);
  assert.equal(api.call("reply").args[1].body, "Rättat bygget, klart!");
  assert.equal(api.call("reply").args[1].costUsd, 0.3);
});

test("build stays red: nothing pushed, changes stashed, crash reported", async () => {
  const { api, workspace, claude } = await round({ workspace: fakeWorkspace({ builds: [false] }) });

  assert.equal(claude.calls.length, 1 + CONFIG.maxBuildFixAttempts);
  assert.ok(!workspace.names().includes("commitAndPush"));
  assert.ok(workspace.names().includes("stash"));
  assert.deepEqual(api.names(), ["claim", "conversation", "crash"]);
  assert.match(api.call("crash").args[1], /Bygget är rött efter 2 rättningsförsök/);
});

test("red tests: reported red, test-green not moved", async () => {
  const { api, workspace } = await round({ workspace: fakeWorkspace({ tests: false }) });

  assert.deepEqual(api.call("tests").args, [7, "abc1234def", false, "Failed: 1"]);
  assert.ok(!workspace.names().includes("markGreen"));
});

test("Claude fails: crash reported, half-done changes stashed, status back to idle", async () => {
  const { api, workspace, heartbeat } = await round({ claude: fakeClaude([new Error("API overloaded")]) });

  assert.deepEqual(api.names(), ["claim", "conversation", "crash"]);
  assert.match(api.call("crash").args[1], /API overloaded/);
  assert.ok(workspace.names().includes("stash"));
  assert.equal(heartbeat.states.at(-1).activity, "idle");
});

test("dirty tree before the round: crash, Claude never runs, nothing stashed", async () => {
  const { RoundFailure } = await import("../lib/errors.mjs");
  const claude = fakeClaude();
  const workspace = fakeWorkspace({ prepareError: new RoundFailure("Arbetskatalogen har oincheckade ändringar") });
  const { api } = await round({ claude, workspace });

  assert.equal(claude.calls.length, 0);
  assert.ok(!workspace.names().includes("stash"));
  assert.match(api.call("crash").args[1], /oincheckade ändringar/);
});

test("claim refused (409): nothing reported as crashed", async () => {
  const api = fakeApi({ conversation: conversation() });
  api.claim = async () => { const { ApiError } = await import("../lib/api.mjs"); throw new ApiError(409, "Claude arbetar redan i samtal 3."); };
  const { heartbeat } = await round({ api });

  assert.ok(!api.names().includes("crash"));
  assert.equal(heartbeat.states.at(-1).activity, "idle");
});

test("resumed conversation: Claude gets the session and only what is new", async () => {
  const conv = conversation({
    sessionId: "s-earlier",
    messages: [
      { authorKind: "human", author: "agneta", body: "Lägg till en exportknapp", at: "1" },
      { authorKind: "claude", author: "Claude", body: "Klart", at: "2" },
      { authorKind: "human", author: "agneta", body: "Gör den grön", at: "3" },
    ],
  });
  const claude = fakeClaude();
  await round({ api: fakeApi({ conversation: conv }), claude });

  assert.equal(claude.calls[0].options.sessionId, "s-earlier");
  assert.match(claude.calls[0].prompt, /Gör den grön/);
  assert.doesNotMatch(claude.calls[0].prompt, /exportknapp/);
});

test("fix round: commit message says so", async () => {
  const api = fakeApi({ conversation: conversation(), round: { number: 2, isFixRound: true } });
  const { workspace } = await round({ api });

  assert.equal(workspace.calls.find((c) => c.name === "commitAndPush").message, "#7: Exportknapp (rättning)");
});

// --- guards: never pay for rounds nobody asked for -------------------------------------------


const GUARDS = { maxRoundsPerConversationPerHour: 3, maxBudgetUsdPerDay: 10 };

test("guard: no new message since Claude's last answer -> Claude is never started", async () => {
  const conv = conversation({
    messages: [
      { authorKind: "human", author: "agneta", body: "Gör X" },
      { authorKind: "claude", author: "Claude", body: "Klart" },
    ],
  });
  const claude = fakeClaude();
  const api = fakeApi({ conversation: conv });
  await runRound({ api, heartbeat: heartbeatRecorder(), claude, workspace: fakeWorkspace(), conversationId: 7, config: CONFIG, guards: createGuards(GUARDS) });

  assert.equal(claude.calls.length, 0);
  assert.match(api.call("crash").args[1], /Inget nytt meddelande/);
});

test("guard: a fix round is allowed without a new human message", () => {
  const conv = conversation({
    messages: [
      { authorKind: "human", author: "agneta", body: "Gör X" },
      { authorKind: "claude", author: "Claude", body: "Klart" },
    ],
  });
  assert.equal(guardRound({ conversation: conv, round: { isFixRound: true }, guards: createGuards(GUARDS) }), null);
});

test("guard: too many rounds in an hour for one conversation stops it", async () => {
  const guards = createGuards(GUARDS);
  const claude = fakeClaude();
  const results = [];
  for (let i = 0; i < 4; i++) {
    const api = fakeApi({ conversation: conversation() });
    await runRound({ api, heartbeat: heartbeatRecorder(), claude, workspace: fakeWorkspace(), conversationId: 7, config: CONFIG, guards });
    results.push(api.names().includes("crash"));
  }
  assert.deepEqual(results, [false, false, false, true]);
  assert.equal(claude.calls.length, 3);
});

test("guard: the daily budget stops new rounds", async () => {
  const guards = createGuards(GUARDS);
  guards.recordCost(10);
  const claude = fakeClaude();
  const api = fakeApi({ conversation: conversation() });
  await runRound({ api, heartbeat: heartbeatRecorder(), claude, workspace: fakeWorkspace(), conversationId: 7, config: CONFIG, guards });

  assert.equal(claude.calls.length, 0);
  assert.match(api.call("crash").args[1], /budgettak/);
});

test("guard: spend is recorded per Claude call, including build-fix calls", async () => {
  const guards = createGuards(GUARDS);
  const claude = fakeClaude([{ text: "a", sessionId: "s", costUsd: 1.5, durationMs: 1 }, { text: "b", sessionId: "s", costUsd: 0.5, durationMs: 1 }]);
  await runRound({ api: fakeApi({ conversation: conversation() }), heartbeat: heartbeatRecorder(), claude, workspace: fakeWorkspace({ builds: [false, true] }), conversationId: 7, config: CONFIG, guards });
  assert.equal(guards.spentToday(), 2);
});
