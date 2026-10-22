// workspace.revertWork against real git repositories (a bare "origin" and a clone), since it's all
// about what git does: revert cleanly as one commit, or leave the tree untouched on a conflict.
import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { test } from "node:test";
import { RoundFailure } from "../lib/errors.mjs";
import { createWorkspace } from "../lib/workspace.mjs";

function git(cwd, ...args) {
  return execFileSync("git", args, { cwd, encoding: "utf8", stdio: ["ignore", "pipe", "pipe"] }).trim();
}

function repo() {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), "claudeborgen-revert-"));
  const origin = path.join(dir, "origin.git");
  const work = path.join(dir, "work");
  git(dir, "init", "-q", "--bare", "-b", "test", origin);
  git(dir, "clone", "-q", origin, work);
  git(work, "checkout", "-q", "-b", "test");
  git(work, "config", "user.name", "Test");
  git(work, "config", "user.email", "test@example.com");
  const commit = (file, content, message) => {
    fs.writeFileSync(path.join(work, file), content);
    git(work, "add", "-A");
    git(work, "commit", "-q", "-m", message);
    git(work, "push", "-q", "origin", "test");
    return git(work, "rev-parse", "HEAD");
  };
  commit("app.txt", "rad 1\n", "start");
  const ws = createWorkspace({ cwd: work, branch: "test", buildCommand: "true", testCommand: "true", testGreenTag: "test-green", commandTimeoutMinutes: 1 });
  return { work, origin, commit, ws };
}

test("revert: all the work's commits undone in one pushed commit", async () => {
  const { work, origin, commit, ws } = repo();
  const c1 = commit("export.txt", "knapp\n", "#12: Exportknapp");
  const c2 = commit("export.txt", "knapp, grön\n", "#12: Exportknapp");

  const reverted = await ws.revertWork({ conversationId: 12, title: "Exportknapp", commits: [c1, c2] });

  assert.ok(!fs.existsSync(path.join(work, "export.txt")), "The work's file is gone again.");
  assert.equal(git(origin, "rev-parse", "test"), reverted, "Pushed.");
  assert.match(git(work, "log", "-1", "--format=%s"), /#12: Avbrutet – ändringarna borttagna \(Exportknapp\)/);
  assert.equal(git(work, "status", "--porcelain"), "");
});

test("revert: a conflict with later work pushes nothing and leaves the tree clean", async () => {
  const { work, origin, commit, ws } = repo();
  const c1 = commit("app.txt", "rad 1\nfrån arbete 12\n", "#12: Lägg till rad");
  commit("app.txt", "rad 1\nfrån arbete 12, ändrad av arbete 13\n", "#13: Bygger vidare");
  const before = git(origin, "rev-parse", "test");

  await assert.rejects(ws.revertWork({ conversationId: 12, title: "Lägg till rad", commits: [c1] }), RoundFailure);

  assert.equal(git(origin, "rev-parse", "test"), before, "Nothing pushed.");
  assert.equal(git(work, "status", "--porcelain"), "", "No half-done revert left behind.");
});

test("revert: already undone by hand -> nothing to do, nothing pushed", async () => {
  const { work, origin, commit, ws } = repo();
  const c1 = commit("export.txt", "knapp\n", "#12: Exportknapp");
  git(work, "revert", "--no-edit", c1);
  git(work, "push", "-q", "origin", "test");
  const head = git(work, "rev-parse", "HEAD");

  const result = await ws.revertWork({ conversationId: 12, title: "Exportknapp", commits: [c1] });

  assert.equal(result, head);
  assert.equal(git(origin, "rev-parse", "test"), head);
});

test("deploy: Azure Local Git (here a bare repo) gets exactly what was pushed, even if it had diverged", async () => {
  const { work, commit } = repo();
  const azure = path.join(path.dirname(work), "azure.git");
  git(path.dirname(work), "init", "-q", "--bare", "-b", "master", azure);
  git(work, "push", "-q", azure, "HEAD:master");
  // Someone pushed something else there by hand meanwhile.
  git(work, "commit", "-q", "--allow-empty", "-m", "bara i azure");
  git(work, "push", "-q", "-f", azure, "HEAD:master");
  git(work, "reset", "-q", "--hard", "HEAD~1");

  const ws = createWorkspace({ cwd: work, branch: "test", buildCommand: "true", testCommand: "true", testGreenTag: "test-green", commandTimeoutMinutes: 1, deployGitUrl: azure });
  fs.writeFileSync(path.join(work, "ny.txt"), "x\n");
  const sha = await ws.commitAndPush("#7: Ny fil");

  assert.equal(git(azure, "rev-parse", "master"), sha);
});
