// Test doubles for the runner: an in-memory agent API that follows PROTOCOL.md closely enough to
// drive a round, and a scriptable workspace / Claude.

export function fakeApi({ conversation, round = { number: 1, isFixRound: false } } = {}) {
  const calls = [];
  const record = (name, ...args) => { calls.push({ name, args }); };
  return {
    calls,
    names: () => calls.map((c) => c.name),
    call: (name) => calls.find((c) => c.name === name),
    info: async () => ({ protocolVersion: 1, mode: "Agent", maxFixAttempts: 2 }),
    heartbeat: async (activity, conversationId, phase) => record("heartbeat", activity, conversationId, phase),
    work: async () => [],
    conversation: async (id) => { record("conversation", id); return conversation; },
    claim: async (id) => { record("claim", id); return round; },
    reply: async (id, reply) => record("reply", id, reply),
    tests: async (id, commit, green, summary) => record("tests", id, commit, green, summary),
    crash: async (id, summary) => record("crash", id, summary),
  };
}

export function heartbeatRecorder() {
  const states = [];
  return {
    states,
    phases: () => states.filter((s) => s.activity === "working").map((s) => s.phase),
    working: async (conversationId, phase) => { states.push({ activity: "working", conversationId, phase }); },
    idle: async () => { states.push({ activity: "idle" }); },
    pause() {},
    resume: async () => {},
  };
}

// builds: list of booleans, consumed one per build() call (last one repeats).
export function fakeWorkspace({ changes = true, builds = [true], tests = true, prepareError = null } = {}) {
  const calls = [];
  let buildIndex = 0;
  return {
    calls,
    names: () => calls.map((c) => c.name),
    hasChanges: async () => { calls.push({ name: "hasChanges" }); return changes; },
    prepare: async () => { calls.push({ name: "prepare" }); if (prepareError) throw prepareError; },
    build: async () => {
      calls.push({ name: "build" });
      const ok = builds[Math.min(buildIndex++, builds.length - 1)];
      return { ok, summary: ok ? "Build succeeded." : "error CS1002: ; expected" };
    },
    test: async () => { calls.push({ name: "test" }); return { ok: tests, summary: tests ? "Passed!" : "Failed: 1" }; },
    commitAndPush: async (message) => { calls.push({ name: "commitAndPush", message }); return "abc1234def"; },
    stash: async (message) => { calls.push({ name: "stash", message }); },
    markGreen: async (commit) => { calls.push({ name: "markGreen", commit }); },
  };
}

export function fakeClaude(answers = [{ text: "Klart!", sessionId: "s-1", costUsd: 0.25, durationMs: 1000 }]) {
  const calls = [];
  let i = 0;
  const claude = async (prompt, options) => {
    calls.push({ prompt, options });
    const answer = answers[Math.min(i++, answers.length - 1)];
    if (answer instanceof Error) throw answer;
    return answer;
  };
  claude.calls = calls;
  return claude;
}

export const CONFIG = { maxBuildFixAttempts: 2, testServerUrl: "https://snabbtest.example/", testGreenTag: "test-green" };

export function conversation(overrides = {}) {
  return {
    id: 7,
    title: "Exportknapp",
    sessionId: null,
    messages: [{ authorKind: "human", author: "agneta", body: "Lägg till en exportknapp", at: "2026-10-20T10:00:00" }],
    ...overrides,
  };
}
