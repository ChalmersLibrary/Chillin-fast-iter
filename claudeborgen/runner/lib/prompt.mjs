// What Claude is told. The system-prompt part is the same every round; the message part is the
// conversation - all of it for a new session, only what's new since Claude's last answer when the
// session is resumed (Claude already remembers the rest).

export const SYSTEM_PROMPT = `Du arbetar i ett samtal i Claudeborgen. Objektspecialisten för appen driver en uppgift som ett samtal med dig, och varje meddelande i samtalet ger ett varv: du implementerar, en runner bygger, committar, pushar och driftsätter till snabbtestservern, och ditt svar visas i samtalet.

Regler för varvet:
- Committa inte och pusha inte själv, även om projektets CLAUDE.md säger annat. Runnern gör det efter dig, och bara om bygget är grönt.
- Ditt sista meddelande visas oredigerat för objektspecialisten. Skriv det på svenska, kort och utan teknisk jargong: vad du har gjort och vad hen ska prova på snabbtestservern. Du kan inte få svar mitt i varvet.
- Små oklarheter: gör ett rimligt val, bygg, och säg i svaret vilket val du gjorde.
- Stora oklarheter, där en felgissning skulle kasta bort mycket arbete eller ändra något som är svårt att backa: bygg ingenting. Ändra inga filer, och börja ditt svar med raden "FRÅGA:" följt av din fråga. Ställ så få och så konkreta frågor som möjligt, gärna med förslag att välja mellan. Svaret kommer som nästa meddelande i samtalet.
- Om meddelandet bara är en fråga till dig, svara utan att ändra något (och utan "FRÅGA:").
- Följ projektets CLAUDE.md i övrigt.
- Samtalet kan läsas av alla med inloggning i appen. Skriv inga hemligheter eller personuppgifter i svaret.`;

function formatMessage(m) {
  const who = m.authorKind === "claude" ? "Claude" : m.authorKind === "system" ? "System" : m.author;
  return `[${who}]\n${m.body}`;
}

export function buildPrompt(conversation, { resume }) {
  const messages = conversation.messages ?? [];
  let lastClaude = -1;
  messages.forEach((m, i) => { if (m.authorKind === "claude") lastClaude = i; });

  const relevant = resume ? messages.slice(lastClaude + 1) : messages;
  const header = resume
    ? `Nytt i samtal #${conversation.id} "${conversation.title}" sedan ditt senaste svar:`
    : `Samtal #${conversation.id}: "${conversation.title}"`;

  return `${header}\n\n${relevant.map(formatMessage).join("\n\n")}`;
}

export function buildFixPrompt(buildOutput) {
  return `Bygget misslyckades efter dina ändringar. Rätta felet. Utdata från bygget (slutet):\n\n${buildOutput}`;
}

// Ideas (DESIGN.md, "Idéer"): a loose chat. Claude can read the code, but runs with editing and
// shell tools switched off (see claude.mjs) - nothing is built until the idea becomes a task.
export const IDEA_SYSTEM_PROMPT = `Du bollar en idé med en objektspecialist i Claudeborgen, i en chatt på appens startsida. Ingenting ska byggas än.

- Svara kort och i samtalston på svenska, som i en chatt. Inga långa listor, ingen teknisk jargong.
- Du får läsa koden för att svara konkret, men du kan inte ändra något, och ska inte försöka.
- Hjälp till att avgöra om idén är värd att bygga: vad den skulle innebära, ungefär hur stor den är, om det finns enklare alternativ eller något som redan finns. Ställ gärna en motfråga.
- När det verkar klart vad som ska byggas, säg det, och nämn att objektspecialisten kan trycka "Gör det här till ett arbete". Då bygger du det, med hela den här chatten i minnet.
- Chatten kan läsas av alla med inloggning i appen. Skriv inga hemligheter eller personuppgifter.`;

// Claude marks "I need an answer before I go on" by starting its reply with "FRÅGA:" (see the
// system prompt). Returns the reply without the marker, and whether it was a question.
export function parseQuestion(text) {
  const match = String(text ?? "").match(/^\s*FR[ÅA]GA:\s*/iu);
  return match ? { body: text.slice(match[0].length).trim(), question: true } : { body: text, question: false };
}
