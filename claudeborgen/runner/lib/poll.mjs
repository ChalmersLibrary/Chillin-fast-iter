// Asking the app for work by long polling (PROTOCOL.md, "GET work"): the app holds the request until
// something changes or `waitSeconds` pass, so the runner reacts at once and asks rarely. An app that
// doesn't hold the request (an older one) answers at once - then fall back to a plain pause, so the
// runner never spins.
export async function waitForWork({ api, kind, waitSeconds, fallbackSeconds, log = () => {}, sleep = defaultSleep, now = Date.now }) {
  const started = now();
  let work;
  try {
    work = await api.work(kind, waitSeconds);
  } catch (e) {
    log(`Kunde inte hämta ${kind === "idea" ? "idéer" : "arbete"}: ${e.message}`);
    await sleep(fallbackSeconds * 1000);
    return [];
  }
  if (work.length === 0 && now() - started < 2000) await sleep(fallbackSeconds * 1000);
  return work;
}

function defaultSleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}
