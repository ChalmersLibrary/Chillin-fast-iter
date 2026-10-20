// What Claude is told. The system-prompt part is the same every round; the message part is the
// conversation - all of it for a new session, only what's new since Claude's last answer when the
// session is resumed (Claude already remembers the rest).

export const SYSTEM_PROMPT = `Du arbetar i ett samtal i Claudeborgen. Objektspecialisten för appen driver en uppgift som ett samtal med dig, och varje meddelande i samtalet ger ett varv: du implementerar, en runner bygger, committar, pushar och driftsätter till snabbtestservern, och ditt svar visas i samtalet.

Regler för varvet:
- Committa inte och pusha inte själv, även om projektets CLAUDE.md säger annat. Runnern gör det efter dig, och bara om bygget är grönt.
- Ditt sista meddelande visas oredigerat för objektspecialisten. Skriv det på svenska, kort och utan teknisk jargong: vad du har gjort och vad hen ska prova på snabbtestservern. Om något var oklart, gör ett rimligt val, säg vilket, och fråga i svaret. Du kan inte få svar mitt i varvet.
- Om meddelandet bara är en fråga, svara utan att ändra något.
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
