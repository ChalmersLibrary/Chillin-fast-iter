import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { test } from "node:test";
import { cleanupSessions, deleteSessions, sessionPaths } from "../lib/sessions.mjs";

const ID = "4fffa765-7176-4395-9009-76ad27cdc347";
const OTHER = "0c17c9fb-80bd-4e3a-927d-53bf95c7cbdb";

function configDir() {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), "claudeborgen-sessions-"));
  const write = (p, content = "x") => { fs.mkdirSync(path.dirname(path.join(dir, p)), { recursive: true }); fs.writeFileSync(path.join(dir, p), content); };
  write(`projects/-workspace/${ID}.jsonl`);
  write(`projects/-workspace/${ID}/tool-results/a.txt`);
  write(`projects/-tmp/${ID}.jsonl`);
  write(`session-env/${ID}/env`);
  write(`file-history/${ID}/backup`);
  // Must survive: another session, memory, settings.
  write(`projects/-workspace/${OTHER}.jsonl`);
  write(`projects/-workspace/memory/MEMORY.md`);
  write("settings.json", "{}");
  return dir;
}

test("sessions: every file of the session is deleted, nothing else", () => {
  const dir = configDir();
  const deleted = deleteSessions(dir, [ID]);

  assert.equal(deleted.length, 5);
  assert.ok(!fs.existsSync(path.join(dir, `projects/-workspace/${ID}.jsonl`)));
  assert.ok(!fs.existsSync(path.join(dir, `projects/-workspace/${ID}`)));
  assert.ok(!fs.existsSync(path.join(dir, `session-env/${ID}`)));
  assert.ok(fs.existsSync(path.join(dir, `projects/-workspace/${OTHER}.jsonl`)));
  assert.ok(fs.existsSync(path.join(dir, "projects/-workspace/memory/MEMORY.md")));
  assert.ok(fs.existsSync(path.join(dir, "settings.json")));
});

test("sessions: an id that isn't a session UUID never becomes a path", () => {
  const dir = configDir();
  assert.deepEqual(sessionPaths(dir, "fejk-session-12"), []);
  assert.deepEqual(sessionPaths(dir, "../settings"), []);
  assert.deepEqual(sessionPaths(dir, ""), []);
  assert.ok(fs.existsSync(path.join(dir, "settings.json")));
});

test("sessions: the runner deletes, then reports each conversation cleaned; the fake deletes nothing", async () => {
  const dir = configDir();
  const reported = [];
  const api = {
    pendingCleanup: async () => [{ conversationId: 12, sessionIds: [ID] }],
    sessionsCleaned: async (id) => { reported.push(id); },
  };

  await cleanupSessions({ api, configDir: dir, fake: true });
  assert.ok(fs.existsSync(path.join(dir, `session-env/${ID}`)), "fake mode touches nothing");

  await cleanupSessions({ api, configDir: dir });
  assert.ok(!fs.existsSync(path.join(dir, `session-env/${ID}`)));
  assert.deepEqual(reported, [12, 12]);
});
