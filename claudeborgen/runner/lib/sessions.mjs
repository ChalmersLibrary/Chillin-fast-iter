// Removing a conversation's Claude Code sessions from the devcontainer (DESIGN.md, "Samtal tas
// bort"): the app deletes the discussion on its side once a conversation is done and its changelog
// entry committed (or right away when cancelled), and Claude Code's own session files hold the same
// discussion. Only exact session ids are deleted - never anything else in the config directory
// (other sessions, memory, settings).
import fs from "node:fs";
import os from "node:os";
import path from "node:path";

const SESSION_ID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;

export function claudeConfigDir(env = process.env) {
  return env.CLAUDE_CONFIG_DIR || path.join(os.homedir(), ".claude");
}

// Every path Claude Code keeps for one session, that exists. A session id that isn't a UUID (a fake
// one, or anything odd) yields nothing - it can't be turned into a path outside these places.
export function sessionPaths(configDir, sessionId, fsImpl = fs) {
  if (!SESSION_ID.test(sessionId ?? "")) return [];

  const candidates = [
    path.join(configDir, "session-env", sessionId),
    path.join(configDir, "file-history", sessionId),
  ];
  const projects = path.join(configDir, "projects");
  if (fsImpl.existsSync(projects)) {
    for (const project of fsImpl.readdirSync(projects)) {
      candidates.push(path.join(projects, project, `${sessionId}.jsonl`));
      candidates.push(path.join(projects, project, sessionId));
    }
  }
  return candidates.filter((p) => fsImpl.existsSync(p));
}

export function deleteSessions(configDir, sessionIds, fsImpl = fs) {
  const deleted = [];
  for (const id of sessionIds ?? []) {
    for (const p of sessionPaths(configDir, id, fsImpl)) {
      fsImpl.rmSync(p, { recursive: true, force: true });
      deleted.push(p);
    }
  }
  return deleted;
}

// The runner's side of the cleanup: delete the sessions of every removed conversation, then tell the
// app. Failures are logged and retried on the next poll.
export async function cleanupSessions({ api, configDir, fake = false, log = () => {}, fsImpl = fs }) {
  let pending = [];
  try {
    pending = await api.pendingCleanup();
  } catch (e) {
    log(`Kunde inte hämta samtal att städa: ${e.message}`);
    return;
  }

  for (const item of pending) {
    try {
      const deleted = fake ? [] : deleteSessions(configDir, item.sessionIds, fsImpl);
      await api.sessionsCleaned(item.conversationId);
      log(`#${item.conversationId}: samtalet är borttaget; ${deleted.length} sessionsfiler raderade.`);
    } catch (e) {
      log(`#${item.conversationId}: kunde inte radera sessionerna: ${e.message}`);
    }
  }
}
