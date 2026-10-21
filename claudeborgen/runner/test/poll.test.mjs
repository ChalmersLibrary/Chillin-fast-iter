import assert from "node:assert/strict";
import { test } from "node:test";
import { createApi } from "../lib/api.mjs";
import { waitForWork } from "../lib/poll.mjs";

function clock() {
  let t = 0;
  return { now: () => t, advance: (ms) => { t += ms; } };
}

test("long poll: asks with kind and wait", async () => {
  let url;
  const api = createApi({ apiUrl: "https://a/", token: "t", fetchImpl: async (u) => { url = String(u); return new Response("[]", { status: 200 }); } });
  await api.work("idea", 25);
  assert.equal(url, "https://a/api/claude/work?kind=idea&wait=25");
});

test("long poll: the app held the request and found nothing -> no extra pause, ask again", async () => {
  const c = clock();
  const sleeps = [];
  const api = { work: async () => { c.advance(25000); return []; } };
  const work = await waitForWork({ api, kind: "idea", waitSeconds: 25, fallbackSeconds: 3, sleep: async (ms) => sleeps.push(ms), now: c.now });
  assert.deepEqual(work, []);
  assert.deepEqual(sleeps, []);
});

test("long poll: an app that answers at once (no long polling) -> fall back to pausing, never spin", async () => {
  const c = clock();
  const sleeps = [];
  const api = { work: async () => { c.advance(50); return []; } };
  await waitForWork({ api, kind: "task", waitSeconds: 25, fallbackSeconds: 20, sleep: async (ms) => sleeps.push(ms), now: c.now });
  assert.deepEqual(sleeps, [20000]);
});

test("long poll: work is returned at once, without pausing", async () => {
  const c = clock();
  const sleeps = [];
  const api = { work: async () => [{ id: 7 }] };
  assert.deepEqual(await waitForWork({ api, kind: "idea", waitSeconds: 25, fallbackSeconds: 3, sleep: async (ms) => sleeps.push(ms), now: c.now }), [{ id: 7 }]);
  assert.deepEqual(sleeps, []);
});

test("long poll: the app can't be reached -> logged, paused, nothing returned", async () => {
  const sleeps = [];
  const logs = [];
  const api = { work: async () => { throw new Error("ECONNREFUSED"); } };
  const work = await waitForWork({ api, kind: "idea", waitSeconds: 25, fallbackSeconds: 3, log: (m) => logs.push(m), sleep: async (ms) => sleeps.push(ms), now: () => 0 });
  assert.deepEqual(work, []);
  assert.deepEqual(sleeps, [3000]);
  assert.match(logs[0], /idéer.*ECONNREFUSED/);
});
