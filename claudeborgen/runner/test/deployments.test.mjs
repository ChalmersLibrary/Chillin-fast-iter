import assert from "node:assert/strict";
import { test } from "node:test";
import { checkDeployments, fetchDeployedCommit, versionUrl } from "../lib/deployments.mjs";
import { createWorkspace } from "../lib/workspace.mjs";

test("version url: derived from testServerUrl unless set explicitly", () => {
  assert.equal(versionUrl({ testServerUrl: "https://snabb.example/" }), "https://snabb.example/claudeborgen/version");
  assert.equal(versionUrl({ testServerUrl: "https://snabb.example" }), "https://snabb.example/claudeborgen/version");
  assert.equal(versionUrl({ testServerUrl: "https://snabb.example/", testServerVersionUrl: "https://other/v" }), "https://other/v");
  assert.equal(versionUrl({}), null);
});

test("fetchDeployedCommit: reads {commit}", async () => {
  const fetchImpl = async () => new Response(JSON.stringify({ commit: "abc" }), { status: 200 });
  assert.equal(await fetchDeployedCommit("https://x/claudeborgen/version", fetchImpl), "abc");
  await assert.rejects(fetchDeployedCommit("https://x/", async () => new Response("", { status: 404 })), /404/);
});

test("checkDeployments: reports exactly the rounds whose commit is included", async () => {
  const reported = [];
  const api = {
    pendingDeployments: async () => [
      { conversationId: 1, round: 2, commit: "old" },
      { conversationId: 3, round: 1, commit: "new-not-yet" },
    ],
    deployed: async (id, round) => { reported.push([id, round]); },
  };
  const workspace = { isDeployed: async (commit, deployed) => commit === "old" && deployed === "live" };

  await checkDeployments({ api, workspace, getDeployedCommit: async () => "live" });

  assert.deepEqual(reported, [[1, 2]]);
});

test("checkDeployments: server doesn't know its commit, or can't be reached -> nothing reported", async () => {
  const reported = [];
  const api = { pendingDeployments: async () => [{ conversationId: 1, round: 1, commit: "c" }], deployed: async (...a) => reported.push(a) };
  const workspace = { isDeployed: async () => true };

  await checkDeployments({ api, workspace, getDeployedCommit: async () => null });
  await checkDeployments({ api, workspace, getDeployedCommit: async () => { throw new Error("ECONNREFUSED"); } });

  assert.deepEqual(reported, []);
});

test("checkDeployments: nothing pending -> the test server isn't even asked", async () => {
  let asked = false;
  await checkDeployments({ api: { pendingDeployments: async () => [] }, workspace: {}, getDeployedCommit: async () => { asked = true; } });
  assert.equal(asked, false);
});

test("workspace.isDeployed: same commit, or an ancestor of the deployed one", async () => {
  const calls = [];
  const runImpl = async (cmd, args) => {
    calls.push(args.join(" "));
    if (args[0] === "merge-base") return { code: args[2] === "ancestor" ? 0 : args[2] === "unknown" ? 128 : 1, stdout: "", output: "" };
    return { code: 0, stdout: "", output: "" };
  };
  const ws = createWorkspace({ cwd: "/p", branch: "test", buildCommand: "b", testCommand: "t", testGreenTag: "g", commandTimeoutMinutes: 1, runImpl });

  assert.equal(await ws.isDeployed("live", "live"), true);
  assert.equal(await ws.isDeployed("ancestor", "live"), true);
  assert.equal(await ws.isDeployed("newer", "live"), false);
  assert.equal(await ws.isDeployed("unknown", "live"), false);
  assert.equal(await ws.isDeployed("x", null), false);
  assert.ok(calls.includes("fetch --quiet origin test"));
});
