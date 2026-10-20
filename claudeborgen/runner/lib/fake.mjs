// --fake: the runner runs for real against the app's agent API, but Claude, git and the build are
// fakes (DESIGN.md, "Fejkad runner"). Steered by the same markers as the app's in-process
// FakeClaude, in the latest human message:
//   [röd]     red tests once -> a fix round -> green
//   [röd!]    tests stay red -> "behöver IT" after maxFixAttempts
//   [krasch]  the round crashes
//   [offline] heartbeats stop for a while, so the app shows Claude offline
import { createFakeWorkspace } from "./workspace.mjs";

export function createFakeContext() {
  const ctx = { instruction: "", isFixRound: false };
  return {
    markers: () => ctx,
    onRound({ conversation, round }) {
      const lastHuman = [...(conversation.messages ?? [])].reverse().find((m) => m.authorKind === "human");
      ctx.instruction = lastHuman?.body ?? "";
      ctx.isFixRound = !!round.isFixRound;
    },
  };
}

export function createFakeClaude({ markers, heartbeat, offlineMs, phaseDelayMs = 1500, sleep = defaultSleep }) {
  return async function fakeClaude(prompt, { sessionId } = {}) {
    const { instruction, isFixRound } = markers();
    await sleep(phaseDelayMs);

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

export function createFakes({ heartbeat, offlineMs, phaseDelayMs }) {
  const context = createFakeContext();
  return {
    onRound: context.onRound,
    claude: createFakeClaude({ markers: context.markers, heartbeat, offlineMs, phaseDelayMs }),
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
