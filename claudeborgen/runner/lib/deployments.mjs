// Telling the owner when a change is actually there to try (DESIGN.md, "Driftsatt"): the runner asks
// the fast test server which commit it runs (/claudeborgen/version) and reports every delivered round
// whose commit is included. No CI or Azure credentials needed - only what the owner's browser
// reaches anyway. Non-blocking: checked on every poll, alongside everything else.

export function versionUrl(config) {
  if (config.testServerVersionUrl) return config.testServerVersionUrl;
  if (!config.testServerUrl) return null;
  return new URL("claudeborgen/version", config.testServerUrl.endsWith("/") ? config.testServerUrl : config.testServerUrl + "/").toString();
}

export async function fetchDeployedCommit(url, fetchImpl = globalThis.fetch) {
  const response = await fetchImpl(url, { headers: { Accept: "application/json" } });
  if (!response.ok) throw new Error(`${url} svarade ${response.status}`);
  const json = await response.json();
  return json?.commit || null;
}

export async function checkDeployments({ api, workspace, getDeployedCommit, log = () => {} }) {
  let pending = [];
  try {
    pending = await api.pendingDeployments();
  } catch (e) {
    log(`Kunde inte hämta varv som väntar på driftsättning: ${e.message}`);
    return;
  }
  if (pending.length === 0) return;

  let deployed;
  try {
    deployed = await getDeployedCommit();
  } catch (e) {
    log(`Kunde inte fråga snabbtestservern vilken version den kör: ${e.message}`);
    return;
  }
  if (!deployed) return; // the server doesn't know its commit - nothing can be confirmed

  for (const item of pending) {
    try {
      if (await workspace.isDeployed(item.commit, deployed)) {
        await api.deployed(item.conversationId, item.round);
        log(`#${item.conversationId} varv ${item.round}: ute på snabbtestservern (${deployed.slice(0, 10)}).`);
      }
    } catch (e) {
      log(`#${item.conversationId}: kunde inte rapportera driftsättningen: ${e.message}`);
    }
  }
}
