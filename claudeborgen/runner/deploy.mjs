#!/usr/bin/env node
// Deploys the current commit to the fast test server: pushes it to Azure App Service "Local Git"
// (DESIGN.md, "Driftsättning") - exactly what the runner does after every push. For everyone else
// who pushes: an interactive Claude session (see the project's CLAUDE.md), or a person.
//
//   node claudeborgen/runner/deploy.mjs [--config claudeborgen.json]
//
// Refuses a commit that isn't on GitHub yet: the fast test server should never run code nobody
// else can see.
import { parseArgs } from "node:util";
import { loadDeployConfig } from "./lib/config.mjs";
import { run } from "./lib/exec.mjs";
import { createWorkspace, redact } from "./lib/workspace.mjs";

const { values } = parseArgs({ options: { config: { type: "string" } } });
const config = loadDeployConfig({ configPath: values.config });

if (!config.deployGitUrl) {
  console.error(config.deployGitWarning ?? "Ingen deployGitUrlFile i claudeborgen.json.");
  process.exit(1);
}

const git = (...args) => run("git", args, { cwd: config.projectRoot, timeoutMs: 5 * 60 * 1000 });
const head = (await git("rev-parse", "HEAD")).stdout.trim();
await git("fetch", "--quiet", "origin");
const onGitHub = (await git("branch", "-r", "--contains", head)).stdout.trim();
if (!onGitHub) {
  console.error(`${head.slice(0, 10)} finns inte på GitHub än - pusha dit först.`);
  process.exit(1);
}

let failed = false;
const workspace = createWorkspace({ cwd: config.projectRoot, ...config, log: (m) => { console.log(redact(m)); failed = m.startsWith("Kunde inte"); } });
await workspace.deploy();
if (failed) process.exit(1);
console.log(`${head.slice(0, 10)} driftsätts på snabbtestservern (Azure-grenen ${config.deployGitBranch}). Bygget tar några minuter - /claudeborgen/version visar när den är ute.`);
