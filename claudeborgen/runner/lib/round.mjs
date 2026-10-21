// One round of a conversation (PROTOCOL.md, "Ett varv, sett från runnern"). The rules that must
// not depend on the model live here as code: build before push, tests after, at most N build-fix
// attempts, nothing pushed on failure, test-green only on green.
import { QuotaExhausted, RoundFailure } from "./errors.mjs";
import { buildFixPrompt, buildPrompt, parseQuestion } from "./prompt.mjs";

export { RoundFailure };

// Guards against paying for rounds nobody asked for. They hold even if the app gets the state
// wrong (found the hard way: a clock mix-up made the app hand out the same conversation 62 times).
export function guardRound({ conversation, round, guards, now = Date.now() }) {
  const messages = conversation.messages ?? [];
  const lastClaude = messages.map((m) => m.authorKind).lastIndexOf("claude");
  const newInput = messages.slice(lastClaude + 1).some((m) => m.authorKind === "human" || m.authorKind === "system");
  if (!newInput && !round.isFixRound)
    return "Inget nytt meddelande att svara på sedan Claudes senaste svar, så runnern startade inte Claude. Appen lämnade ut samtalet ändå - IT behöver titta på det.";

  const hour = 60 * 60 * 1000;
  const recent = guards.roundsFor(conversation.id).filter((t) => now - t < hour);
  if (recent.length >= guards.maxRoundsPerConversationPerHour)
    return `Samtalet har fått ${recent.length} varv den senaste timmen, vilket är runnerns tak (maxRoundsPerConversationPerHour). Runnern startar inga fler varv i det just nu.`;

  const recentAll = guards.allRounds().filter((t) => now - t < hour);
  if (recentAll.length >= guards.maxRoundsPerHour)
    return `Runnern har kört ${recentAll.length} varv den senaste timmen, över alla samtal, vilket är taket (maxRoundsPerHour). Det tyder på en loop; inga fler varv startas just nu.`;

  // Optional, for an API key with a real budget - off by default (null).
  if (guards.maxBudgetUsdPerDay != null && guards.spentToday(now) >= guards.maxBudgetUsdPerDay)
    return `Dagens budgettak för runnern (${guards.maxBudgetUsdPerDay} USD, maxBudgetUsdPerDay) är nått. Inga fler varv startas förrän i morgon.`;

  return null;
}

// Per-process bookkeeping for the guards: rounds per conversation and in total, spend per UTC day.
export function createGuards({ maxRoundsPerConversationPerHour, maxRoundsPerHour, maxBudgetUsdPerDay }) {
  const rounds = new Map();
  const spent = new Map();
  const day = (t) => new Date(t).toISOString().slice(0, 10);
  return {
    maxRoundsPerConversationPerHour,
    maxRoundsPerHour,
    maxBudgetUsdPerDay,
    roundsFor: (id) => rounds.get(id) ?? [],
    allRounds: () => [...rounds.values()].flat(),
    recordRound(id, now = Date.now()) { rounds.set(id, [...(rounds.get(id) ?? []), now]); },
    spentToday: (now = Date.now()) => spent.get(day(now)) ?? 0,
    recordCost(usd, now = Date.now()) { spent.set(day(now), (spent.get(day(now)) ?? 0) + usd); },
  };
}

export async function runRound({ api, heartbeat, claude, workspace, conversationId, config, guards, log = () => {}, onRound = () => {} }) {
  const startedAt = Date.now();
  let costUsd = 0;
  let sessionId = null;
  let round = null;

  try {
    await heartbeat.working(conversationId, "läser in");
    round = await api.claim(conversationId);
    const conversation = await api.conversation(conversationId);
    sessionId = conversation.sessionId ?? null;
    onRound({ conversation, round });
    log(`#${conversationId} varv ${round.number}${round.isFixRound ? " (rättning)" : ""}: ${conversation.title}`);

    if (guards) {
      const refusal = guardRound({ conversation, round, guards });
      if (refusal) throw new RoundFailure(refusal);
      guards.recordRound(conversationId);
    }

    await workspace.prepare();

    await heartbeat.working(conversationId, "implementerar");
    let answer = await claude(buildPrompt(conversation, { resume: !!sessionId }), { sessionId });
    costUsd += answer.costUsd;
    guards?.recordCost(answer.costUsd);
    sessionId = answer.sessionId ?? sessionId;

    let commit = null;
    if (await workspace.hasChanges()) {
      await heartbeat.working(conversationId, "bygger");
      let build = await workspace.build();
      for (let attempt = 1; !build.ok && attempt <= config.maxBuildFixAttempts; attempt++) {
        log(`#${conversationId}: bygget rött, rättningsförsök ${attempt}`);
        await heartbeat.working(conversationId, "rättar bygget");
        answer = await claude(buildFixPrompt(build.summary), { sessionId });
        costUsd += answer.costUsd;
        guards?.recordCost(answer.costUsd);
        sessionId = answer.sessionId ?? sessionId;
        await heartbeat.working(conversationId, "bygger");
        build = await workspace.build();
      }

      if (!build.ok) {
        await workspace.stash(`claudeborgen #${conversationId} varv ${round.number}: bygget rött`);
        throw new RoundFailure(
          `Bygget är rött efter ${config.maxBuildFixAttempts} rättningsförsök. Inget har pushats; ändringarna ligger i git stash.\n\n${build.summary}`);
      }

      await heartbeat.working(conversationId, "pushar");
      commit = await workspace.commitAndPush(`#${conversationId}: ${conversation.title}${round.isFixRound ? " (rättning)" : ""}`);
    }

    const { body, question } = parseQuestion(answer.text);
    await api.reply(conversationId, {
      body: body || "(Claude lämnade inget svar.)",
      question,
      commits: commit ? [commit] : [],
      links: commit && config.testServerUrl ? [config.testServerUrl] : [],
      sessionId,
      costUsd: round2(costUsd),
      durationSeconds: Math.round((Date.now() - startedAt) / 1000),
    });
    log(`#${conversationId}: ${question ? "fråga" : "svar"} skickat${commit ? `, commit ${commit.slice(0, 10)}` : ", inga ändringar"}`);

    if (commit) {
      await heartbeat.working(conversationId, "kör tester");
      const tests = await workspace.test();
      await api.tests(conversationId, commit, tests.ok, tests.summary);
      log(`#${conversationId}: testerna ${tests.ok ? "gröna" : "röda"}`);
      if (tests.ok) {
        try {
          await workspace.markGreen(commit);
        } catch (e) {
          log(`#${conversationId}: kunde inte flytta ${config.testGreenTag}: ${e.message}`);
        }
      }
    }
  } catch (e) {
    if (e instanceof QuotaExhausted) {
      // Not the conversation's fault: keep whatever Claude got done, hand the conversation back to
      // the queue, and tell the loop to pause.
      log(`#${conversationId}: ${e.message}`);
      try {
        if (await workspace.hasChanges())
          await workspace.stash(`claudeborgen #${conversationId} varv ${round?.number}: kvoten tog slut`);
      } catch (stashError) {
        log(`#${conversationId}: kunde inte stasha ändringarna: ${stashError.message}`);
      }
      try {
        if (round) await api.release(conversationId);
      } catch (releaseError) {
        log(`#${conversationId}: kunde inte lämna tillbaka samtalet: ${releaseError.message}`);
      }
      return { paused: true, resetAt: e.resetAt };
    }

    const summary = e instanceof RoundFailure ? e.message : `Runnern avbröt varvet: ${e.message}`;
    log(`#${conversationId}: varvet misslyckades: ${summary}`);

    // A half-done round's changes must not block the next one (prepare() refuses a dirty tree).
    if (!(e instanceof RoundFailure)) {
      try {
        if (round && await workspace.hasChanges())
          await workspace.stash(`claudeborgen #${conversationId} varv ${round.number}: avbrutet`);
      } catch (stashError) {
        log(`#${conversationId}: kunde inte stasha ändringarna: ${stashError.message}`);
      }
    }

    // Only a claimed round can be reported as crashed (otherwise the claim itself failed, e.g.
    // 409 because Claude already works elsewhere - nothing to report).
    if (round) {
      try {
        await api.crash(conversationId, summary);
      } catch (reportError) {
        log(`#${conversationId}: kunde inte rapportera kraschen: ${reportError.message}`);
      }
    }
  } finally {
    await heartbeat.idle();
  }
  return { paused: false };
}

function round2(n) {
  return Math.round(n * 100) / 100;
}

// One answer in an idea chat (DESIGN.md, "Idéer"): no git, no build, Claude in read-only mode.
// Guarded like a task round (no new input -> no Claude; per-conversation and total limits), with
// its own, chattier limits.
export async function runIdeaRound({ api, claude, conversationId, guards, log = () => {} }) {
  let round = null;
  try {
    round = await api.claim(conversationId);
    const conversation = await api.conversation(conversationId);
    if (guards) {
      const refusal = guardRound({ conversation, round, guards });
      if (refusal) throw new RoundFailure(refusal);
      guards.recordRound(conversationId);
    }

    const sessionId = conversation.sessionId ?? null;
    const answer = await claude(buildPrompt(conversation, { resume: !!sessionId }), { sessionId, mode: "idea" });
    guards?.recordCost(answer.costUsd);
    await api.reply(conversationId, {
      body: answer.text || "(Claude lämnade inget svar.)",
      commits: [],
      links: [],
      sessionId: answer.sessionId ?? sessionId,
      costUsd: round2(answer.costUsd),
      durationSeconds: Math.round((answer.durationMs ?? 0) / 1000),
      question: false,
    });
    log(`Idé #${conversationId}: svar skickat`);
    return { paused: false };
  } catch (e) {
    if (e instanceof QuotaExhausted) {
      log(`Idé #${conversationId}: ${e.message}`);
      try { if (round) await api.release(conversationId); } catch (releaseError) { log(`Idé #${conversationId}: kunde inte lämna tillbaka: ${releaseError.message}`); }
      return { paused: true, resetAt: e.resetAt };
    }
    const summary = e instanceof RoundFailure ? e.message : `Runnern kunde inte svara: ${e.message}`;
    log(`Idé #${conversationId}: ${summary}`);
    if (round) {
      try { await api.crash(conversationId, summary); } catch (reportError) { log(`Idé #${conversationId}: kunde inte rapportera: ${reportError.message}`); }
    }
    return { paused: false };
  }
}

// "Avbryt och ta bort arbete": revert what cancelled work pushed (DESIGN.md, "Avbryta ett arbete").
// No Claude. Success removes the conversation on the app's side; failure flags it for IT.
export async function revertCancelledWork({ api, heartbeat, workspace, log = () => {} }) {
  let pending = [];
  try {
    pending = await api.pendingReverts();
  } catch (e) {
    log(`Kunde inte hämta avbrutna arbeten: ${e.message}`);
    return;
  }

  for (const work of pending) {
    try {
      await heartbeat.working(work.conversationId, "tar bort ändringar");
      const commit = await workspace.revertWork(work);
      await api.reverted(work.conversationId, commit);
      log(`#${work.conversationId}: avbrutet, ${work.commits.length} commit(ar) borttagna (${commit.slice(0, 10)})`);

      // test-green may still point at a commit with the cancelled work in it - move it past the
      // revert once the tests agree, like after any round.
      await heartbeat.working(work.conversationId, "kör tester");
      const tests = await workspace.test();
      if (tests.ok) await workspace.markGreen(commit);
      else log(`#${work.conversationId}: testerna är röda efter borttagningen; test-green flyttas inte.`);
    } catch (e) {
      const summary = e instanceof RoundFailure ? e.message : `Runnern kunde inte ta bort ändringarna: ${e.message}`;
      log(`#${work.conversationId}: ${summary}`);
      try { await api.revertFailed(work.conversationId, summary); } catch (reportError) { log(`#${work.conversationId}: kunde inte rapportera: ${reportError.message}`); }
    } finally {
      await heartbeat.idle();
    }
  }
}

// After "klar": commit the conversation's changelog entry to the project's changelog file, so it
// travels with the code (DESIGN.md, "Changelog"). Failures are only logged and retried on the next
// poll - the conversation itself is done either way.
export async function commitChangelog({ api, heartbeat, workspace, log = () => {} }) {
  let pending = [];
  try {
    pending = await api.pendingChangelog();
  } catch (e) {
    log(`Kunde inte hämta changelog-poster: ${e.message}`);
    return;
  }

  for (const entry of pending) {
    try {
      await heartbeat.working(entry.conversationId, "uppdaterar changelogen");
      const commit = await workspace.commitChangelogEntry(entry);
      await api.changelogCommitted(entry.conversationId, commit);
      log(`#${entry.conversationId}: changelog-posten committad (${commit.slice(0, 10)})`);
    } catch (e) {
      log(`#${entry.conversationId}: kunde inte committa changelog-posten: ${e.message}`);
    } finally {
      await heartbeat.idle();
    }
  }
}
