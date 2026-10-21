// A round that failed in an expected, explained way (red build, dirty tree, ...): reported to the
// app as-is, and the runner leaves the working tree alone beyond what it already did itself.
export class RoundFailure extends Error {}

// The account Claude runs on has used up its quota (subscription limit, or an API key's rate or
// spend limit). Not the conversation's fault: the round is handed back to the queue and the runner
// pauses until resetAt (or a default retry interval).
export class QuotaExhausted extends Error {
  constructor(message, resetAt = null) {
    super(message);
    this.resetAt = resetAt;
  }
}

// Recognizes a quota/limit failure in Claude Code's output. Subscription limits have been reported
// as "...usage limit reached|<epoch seconds>"; API limits as HTTP 429. Anything else is a real error.
export function quotaFrom({ apiErrorStatus, text }) {
  const t = String(text ?? "");
  const looksLikeLimit = apiErrorStatus === 429 || /usage limit|rate limit|limit reached|quota|credit balance is too low/i.test(t);
  if (!looksLikeLimit) return null;
  const epoch = t.match(/\|(\d{10})\b/);
  const resetAt = epoch ? new Date(Number(epoch[1]) * 1000) : null;
  return new QuotaExhausted(`Kvoten för kontot Claude kör på är slut: ${t.slice(0, 200)}`, resetAt);
}
