// The project's runner settings: claudeborgen.json in the project root (what to build and test,
// which branch, where the app is), plus the agent token from a file outside the repo. Environment
// variables override the file, so a devcontainer can point the same repo at another app.
import fs from "node:fs";
import os from "node:os";
import path from "node:path";

export const DEFAULTS = {
  branch: "test",
  testGreenTag: "test-green",
  // Dollar limits are optional (null = off): they only mean something with an API key. Loops are
  // caught by the round limits below regardless of what account Claude runs on.
  maxBudgetUsdPerRound: null,
  roundTimeoutMinutes: 45,
  commandTimeoutMinutes: 20,
  maxBuildFixAttempts: 2,
  maxRoundsPerConversationPerHour: 8,
  maxRoundsPerHour: 20,
  maxBudgetUsdPerDay: null,
  pauseRetryMinutes: 30,
  heartbeatSeconds: 20,
  // Long polling: how long the app may hold a "anything to do?" request (max 30). The poll intervals
  // below are only the fallback for an app that answers at once, and the pace while paused.
  longPollSeconds: 25,
  pollSeconds: 20,
  // Ideas are a chat, with their own (chattier) loop limits.
  ideaPollSeconds: 3,
  maxIdeaRepliesPerConversationPerHour: 30,
  maxIdeaRepliesPerHour: 60,
  claudeCommand: "claude",
  model: null,
  fallbackModel: null,
  changelogFile: null,
  // Where the fast test server says which commit it runs; default <testServerUrl>/claudeborgen/version.
  testServerVersionUrl: null,
  // Azure App Service "Local Git" (DESIGN.md, "Driftsättning"): a file outside the repo holding the
  // app's git URL *with* deployment credentials. Every push to `branch` is then pushed there too,
  // which builds and deploys it. null = the runner doesn't deploy (something else does).
  deployGitUrlFile: null,
  deployGitBranch: "master",
  tokenFile: "~/.claudeborgen/token",
};

const REQUIRED = ["apiUrl", "buildCommand", "testCommand"];

export function expandHome(p) {
  return p && p.startsWith("~/") ? path.join(os.homedir(), p.slice(2)) : p;
}

export function loadConfig({ configPath, env = process.env, readFile = fs.readFileSync } = {}) {
  const file = path.resolve(configPath ?? "claudeborgen.json");
  let fromFile;
  try {
    fromFile = JSON.parse(readFile(file, "utf8"));
  } catch (e) {
    throw new Error(`Kunde inte läsa ${file}: ${e.message}`);
  }

  const config = { ...DEFAULTS, ...fromFile, projectRoot: path.dirname(file) };
  if (env.CLAUDEBORGEN_API_URL) config.apiUrl = env.CLAUDEBORGEN_API_URL;
  if (env.CLAUDEBORGEN_TOKEN_FILE) config.tokenFile = env.CLAUDEBORGEN_TOKEN_FILE;
  if (env.CLAUDEBORGEN_TEST_SERVER_URL) config.testServerUrl = env.CLAUDEBORGEN_TEST_SERVER_URL;

  Object.assign(config, readDeployUrl(config, { env, readFile }));

  const missing = REQUIRED.filter((k) => !config[k]);
  if (missing.length) throw new Error(`${file} saknar: ${missing.join(", ")}`);
  if (!config.apiUrl.endsWith("/")) config.apiUrl += "/";

  const tokenPath = expandHome(config.tokenFile);
  try {
    config.token = env.CLAUDEBORGEN_TOKEN || readFile(tokenPath, "utf8").trim();
  } catch (e) {
    throw new Error(`Kunde inte läsa agent-tokenen från ${tokenPath} (CLAUDEBORGEN_TOKEN_FILE): ${e.message}`);
  }
  if (!config.token) throw new Error(`Agent-tokenen i ${tokenPath} är tom.`);

  return config;
}

// The Azure Local Git URL with deployment credentials, from a file outside the repo (DESIGN.md,
// "Driftsättning"). A missing file isn't an error - whoever uses it says it won't deploy.
export function readDeployUrl(config, { env = process.env, readFile = fs.readFileSync } = {}) {
  const file = env.CLAUDEBORGEN_DEPLOY_GIT_URL_FILE || config.deployGitUrlFile;
  if (!file) return { deployGitUrl: null };
  const deployPath = expandHome(file);
  let url;
  try {
    url = readFile(deployPath, "utf8").trim();
  } catch {
    return { deployGitUrl: null, deployGitWarning: `Ingen Azure-adress i ${deployPath} (deployGitUrlFile) - ingen driftsättning.` };
  }
  if (!/^https:\/\/[^/]+@/.test(url))
    throw new Error(`${deployPath} ska innehålla appens Local Git-adress med uppgifter: https://<användare>:<lösenord>@<app>.scm.azurewebsites.net/<app>.git`);
  return { deployGitUrl: url };
}

// Just what deploy.mjs needs: the project's claudeborgen.json and the Azure URL - no agent token or
// app address, so it works in any session that can push.
export function loadDeployConfig({ configPath, env = process.env, readFile = fs.readFileSync } = {}) {
  const file = path.resolve(configPath ?? "claudeborgen.json");
  const config = { ...DEFAULTS, ...JSON.parse(readFile(file, "utf8")), projectRoot: path.dirname(file) };
  return { ...config, ...readDeployUrl(config, { env, readFile }) };
}
