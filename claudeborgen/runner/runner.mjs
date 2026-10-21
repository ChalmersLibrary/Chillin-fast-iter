#!/usr/bin/env node
// Claudeborgen runner (see ../DESIGN.md, ../PROTOCOL.md). Runs in the project's devcontainer, next
// to Claude Code, in a loop: heartbeat, ask the app for work, run one round at a time. Deliberately
// dumb - everything that must not depend on the model (status, limits, build before push, tests
// after) is here; all the thinking is Claude Code's.
//
//   node claudeborgen/runner/runner.mjs [--config claudeborgen.json] [--fake] [--once]
//
//   --config  the project's settings (default: ./claudeborgen.json)
//   --fake    fake Claude, git and build - for trying the chain against the fast test server
//             (app in Claudeborgen:IsolatedMode=FakeRunner). Refuses to run against the real channel.
//   --once    at most one round, then exit (for trying things out)
import { parseArgs } from "node:util";
import { createApi } from "./lib/api.mjs";
import { createClaude } from "./lib/claude.mjs";
import { loadConfig } from "./lib/config.mjs";
import { createFakes } from "./lib/fake.mjs";
import { Heartbeat } from "./lib/heartbeat.mjs";
import { commitChangelog, createGuards, runRound } from "./lib/round.mjs";
import { claudeConfigDir, cleanupSessions } from "./lib/sessions.mjs";
import { createWorkspace } from "./lib/workspace.mjs";

function log(message) {
  console.log(`${new Date().toISOString()} ${message}`);
}

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

// The account's quota is used up: show "paused" (heartbeats keep going, so it isn't "offline") and
// wait until the reset time Claude gave, or pauseRetryMinutes, then carry on.
async function pause(heartbeat, resetAt, config, log) {
  const fallback = new Date(Date.now() + config.pauseRetryMinutes * 60 * 1000);
  const until = resetAt && resetAt > new Date() ? resetAt : fallback;
  log(`Pausar till ${until.toISOString()}: kvoten för kontot Claude kör på är slut.`);
  await heartbeat.paused(until, "kvoten är slut");
  await sleep(until - Date.now());
  await heartbeat.idle();
}

export async function main(argv = process.argv.slice(2)) {
  const { values } = parseArgs({
    args: argv,
    options: {
      config: { type: "string" },
      fake: { type: "boolean", default: false },
      once: { type: "boolean", default: false },
    },
  });

  const config = loadConfig({ configPath: values.config });
  const api = createApi({ apiUrl: config.apiUrl, token: config.token, fake: values.fake });

  const info = await api.info();
  log(`Ansluten till ${config.apiUrl} (protokoll ${info.protocolVersion}, läge ${info.mode})${values.fake ? " i fejkläge" : ""}.`);
  if (!values.fake)
    log(config.model
      ? `Modell: ${config.model}${config.fallbackModel ? `, reserv ${config.fallbackModel}` : ""}.`
      : "Ingen modell i claudeborgen.json - Claude Codes standardinställning i containern används.");
  if (info.protocolVersion !== 1) throw new Error(`Appen talar protokoll ${info.protocolVersion}; runnern kan bara version 1.`);
  if (values.fake && info.mode !== "FakeRunner")
    throw new Error(`Fejkläget är bara till för en snabbtestserver i läge FakeRunner, inte ${info.mode} - fejksvar hör inte hemma i riktiga samtal.`);
  if (!values.fake && info.mode !== "Agent")
    throw new Error(`Appen är i läge ${info.mode}. En riktig runner kör bara mot läge Agent${info.mode === "FakeRunner" ? " (kör med --fake mot snabbtestservern)" : ""}.`);

  const heartbeat = new Heartbeat({ api, intervalSeconds: config.heartbeatSeconds, log });
  const guards = createGuards(config);

  let claude, workspace, onRound;
  if (values.fake) {
    ({ claude, workspace, onRound } = createFakes({ heartbeat, offlineMs: 90 * 1000, phaseDelayMs: 1500 }));
  } else {
    claude = createClaude({
      command: config.claudeCommand,
      cwd: config.projectRoot,
      maxBudgetUsd: config.maxBudgetUsdPerRound,
      timeoutMinutes: config.roundTimeoutMinutes,
      model: config.model,
      fallbackModel: config.fallbackModel,
    });
    workspace = createWorkspace({ cwd: config.projectRoot, ...config });
  }

  await heartbeat.start();
  const shutdown = () => { heartbeat.stop(); process.exit(0); };
  process.on("SIGINT", shutdown);
  process.on("SIGTERM", shutdown);

  for (;;) {
    await commitChangelog({ api, heartbeat, workspace, log });
    await cleanupSessions({ api, configDir: claudeConfigDir(), fake: values.fake, log });

    let work = [];
    try {
      work = await api.work();
    } catch (e) {
      log(`Kunde inte hämta arbete: ${e.message}`);
    }

    if (work.length > 0) {
      const result = await runRound({ api, heartbeat, claude, workspace, conversationId: work[0].id, config, guards, log, onRound });
      if (result.paused) {
        await pause(heartbeat, result.resetAt, config, log);
        if (values.once) break;
        continue;
      }
      if (values.once) break;
      // There may be more right away (a fix round, a message written meanwhile) - a short pause
      // only, so a round that keeps failing early can't spin.
      await sleep(2000);
      continue;
    }

    if (values.once) break;
    await sleep(config.pollSeconds * 1000);
  }

  heartbeat.stop();
}

if (import.meta.url === `file://${process.argv[1]}`) {
  main().catch((e) => {
    log(`Runnern stannade: ${e.message}`);
    process.exit(1);
  });
}
