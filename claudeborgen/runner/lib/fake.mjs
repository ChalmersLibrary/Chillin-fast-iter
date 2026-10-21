// --fake: the runner runs for real against the app's agent API, but Claude, git and the build are
// fakes (DESIGN.md, "Fejkad runner"). Steered by the same markers as the app's in-process
// FakeClaude, in the latest human message:
//   [röd]     red tests once -> a fix round -> green
//   [röd!]    tests stay red -> "behöver IT" after maxFixAttempts
//   [krasch]  the round crashes
//   [offline] heartbeats stop for a while, so the app shows Claude offline
//   [kvot]    the quota is "used up" once per conversation: back to the queue, runner paused
import { QuotaExhausted } from "./errors.mjs";
import { createFakeWorkspace } from "./workspace.mjs";

export function createFakeContext() {
  const ctx = { instruction: "", isFixRound: false, conversationId: null, quotaPausedOnce: new Set() };
  return {
    markers: () => ctx,
    onRound({ conversation, round }) {
      const lastHuman = [...(conversation.messages ?? [])].reverse().find((m) => m.authorKind === "human");
      ctx.instruction = lastHuman?.body ?? "";
      ctx.isFixRound = !!round.isFixRound;
      ctx.conversationId = conversation.id;
    },
  };
}

export function createFakeClaude({ markers, heartbeat, offlineMs, quotaPauseMs = 45000, phaseDelayMs = 1500, sleep = defaultSleep }) {
  return async function fakeClaude(prompt, { sessionId } = {}) {
    const ctx = markers();
    const { instruction, isFixRound } = ctx;
    await sleep(phaseDelayMs);

    if (instruction.includes("[kvot]") && !ctx.quotaPausedOnce.has(ctx.conversationId)) {
      ctx.quotaPausedOnce.add(ctx.conversationId);
      throw new QuotaExhausted("Fejkrunnern låtsades att kvoten tog slut ([kvot] i meddelandet).", new Date(Date.now() + quotaPauseMs));
    }

    if (instruction.includes("[offline]") && !isFixRound) {
      heartbeat.pause();
      await sleep(offlineMs);
      await heartbeat.resume();
    }
    if (instruction.includes("[krasch]") && !isFixRound) throw new Error("Fejkrunnern låtsades krascha ([krasch] i meddelandet).");

    const text = isFixRound
      ? "(Fejkad runner) Jag har rättat testerna."
      : `(Fejkad runner) Klart: "${shorten(instruction)}". Inget har ändrats på riktigt.`;
    return { text, sessionId: sessionId ?? `fejk-session-${Date.now()}`, costUsd: 0, durationMs: phaseDelayMs };
  };
}

export function createFakes({ heartbeat, offlineMs, phaseDelayMs, quotaPauseMs }) {
  const context = createFakeContext();
  return {
    onRound: context.onRound,
    claude: createFakeClaude({ markers: context.markers, heartbeat, offlineMs, phaseDelayMs, quotaPauseMs }),
    workspace: createFakeWorkspace({ markers: context.markers }),
  };
}

function shorten(text) {
  const oneLine = text.replace(/\s+/g, " ").trim();
  return oneLine.length <= 80 ? oneLine : oneLine.slice(0, 77) + "...";
}

function defaultSleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}
