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
  pollSeconds: 20,
  claudeCommand: "claude",
  model: null,
  fallbackModel: null,
  changelogFile: null,
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
