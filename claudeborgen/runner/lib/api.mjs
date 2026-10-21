// Client for the agent API - ../PROTOCOL.md is the contract.
export class ApiError extends Error {
  constructor(status, message) {
    super(message);
    this.status = status;
  }
}

export function createApi({ apiUrl, token, fake = false, fetchImpl = globalThis.fetch }) {
  const base = new URL("api/claude/", apiUrl);
  const headers = { Authorization: `Bearer ${token}`, "Content-Type": "application/json" };
  if (fake) headers["X-Claudeborgen-Fake-Runner"] = "true";

  async function call(method, path, body) {
    const response = await fetchImpl(new URL(path, base), {
      method,
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
    });
    const text = await response.text();
    const json = text ? JSON.parse(text) : null;
    if (!response.ok) throw new ApiError(response.status, json?.message ?? `HTTP ${response.status}`);
    return json;
  }

  return {
    info: () => call("GET", "info"),
    heartbeat: (activity, conversationId, phase, pausedUntil) =>
      call("POST", "heartbeat", { activity, conversationId, phase, pausedUntil: pausedUntil ? pausedUntil.toISOString() : undefined }),
    release: (id) => call("POST", `conversations/${id}/release`),
    work: (kind, waitSeconds) => {
      const query = new URLSearchParams();
      if (kind) query.set("kind", kind);
      if (waitSeconds) query.set("wait", String(waitSeconds));
      const qs = query.toString();
      return call("GET", qs ? `work?${qs}` : "work");
    },
    conversation: (id) => call("GET", `conversations/${id}`),
    claim: (id) => call("POST", `conversations/${id}/claim`),
    reply: (id, reply) => call("POST", `conversations/${id}/reply`, reply),
    tests: (id, commit, green, summary) => call("POST", `conversations/${id}/tests`, { commit, green, summary }),
    crash: (id, summary) => call("POST", `conversations/${id}/crash`, { summary }),
    pendingChangelog: () => call("GET", "changelog/pending"),
    pendingCleanup: () => call("GET", "cleanup"),
    pendingDeployments: () => call("GET", "deployments/pending"),
    pendingReverts: () => call("GET", "reverts/pending"),
    reverted: (id, commit) => call("POST", `conversations/${id}/reverted`, { commit }),
    revertFailed: (id, summary) => call("POST", `conversations/${id}/revert-failed`, { summary }),
    deployed: (id, round) => call("POST", `conversations/${id}/rounds/${round}/deployed`),
    sessionsCleaned: (id) => call("POST", `conversations/${id}/cleaned`),
    changelogCommitted: (id, commit) => call("POST", `conversations/${id}/changelog`, { commit }),
  };
}
