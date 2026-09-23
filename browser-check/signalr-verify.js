// Lightweight (non-browser) verification of real-time SignalR push notifications.
// Substitute for the two-window Puppeteer test, which is too heavy for this single-core
// sandbox (see TODO-remove-dotnet-framework.md fas 11, "SignalR-realtidsuppdateringar").
//
// What this proves that a full two-window browser test would also prove:
// 1. A client can connect to /notificationHub and receive "updateStream" pushes at all
//    (the hub wiring, JSON protocol, negotiate/WebSocket handshake).
// 2. Changing an order's status over HTTP (as the UI's dropdown would) causes the server
//    to push a notification naming the correct NodeId - the actual client-A-affects-client-B
//    mechanism the two-window test exists to check.
// 3. The previously-fixed FollowUpDate bug (loadOrderItemSummary parsing "/Date(.../" as a
//    literal regex against what is now a plain ISO-8601 string) stays fixed: fetch the same
//    JSON the client-side code fetches after receiving updateStream, and parse the date with
//    JS's `new Date(...)`, exactly like chalmers.ill.js does.
//
// What this deliberately does NOT try to prove (out of scope for a non-browser check):
// - Pixels actually redrawing in two open browser tabs.
// - withAutomaticReconnect after killing the server (still requires a real browser/second
//   client scenario and is orthogonal to the payload-correctness question this checks).

const signalR = require("@microsoft/signalr");

const baseUrl = process.argv[2] || "http://localhost:5000";
const loginName = process.argv[3] || "admin";
const password = process.argv[4] || "chillin123";
const reference = process.argv[5] || "ref-ny-001";

function log(msg) {
  console.log(`[${new Date().toISOString()}] ${msg}`);
}

function parseSetCookies(res) {
  const raw = res.headers.getSetCookie ? res.headers.getSetCookie() : [];
  return raw.map((c) => c.split(";")[0]);
}

async function main() {
  const fetchImpl = globalThis.fetch;

  // ---- 1. GET login page: grab antiforgery cookie + hidden token ----
  const loginPageRes = await fetchImpl(`${baseUrl}/ChalmersILLLoginPage`, { redirect: "manual" });
  const cookies = parseSetCookies(loginPageRes);
  const loginPageHtml = await loginPageRes.text();
  const tokenMatch = loginPageHtml.match(
    /name="__RequestVerificationToken"[^>]*value="([^"]+)"/
  );
  if (!tokenMatch) throw new Error("Could not find __RequestVerificationToken on login page");
  const antiForgeryToken = tokenMatch[1];
  log(`Got login page, antiforgery token acquired, cookies: ${cookies.map((c) => c.split("=")[0]).join(", ")}`);

  // ---- 2. POST HandleLogin ----
  const form = new URLSearchParams();
  form.set("Login", loginName);
  form.set("Password", password);
  form.set("__RequestVerificationToken", antiForgeryToken);

  const loginRes = await fetchImpl(`${baseUrl}/LoginSurface/HandleLogin`, {
    method: "POST",
    headers: {
      "Content-Type": "application/x-www-form-urlencoded",
      Cookie: cookies.join("; "),
    },
    body: form.toString(),
    redirect: "manual",
  });
  const allCookies = cookies.concat(parseSetCookies(loginRes));
  const cookieHeader = dedupeCookies(allCookies).join("; ");
  const location = loginRes.headers.get("location") || "";
  if (loginRes.status !== 302 || location.includes("error=")) {
    throw new Error(`Login failed: status=${loginRes.status} location=${location}`);
  }
  log(`Logged in as ${loginName}, redirected to ${location}`);

  // ---- 3. Resolve the seeded order's NodeId via free-text search ----
  const listRes = await fetchImpl(
    `${baseUrl}/bestaellningar?query=${encodeURIComponent(reference)}`,
    { headers: { Cookie: cookieHeader } }
  );
  const listHtml = await listRes.text();
  const idMatch = listHtml.match(new RegExp(`id="(\\d+)"[^>]*>[\\s\\S]{0,4000}?${reference}`));
  if (!idMatch) {
    throw new Error(`Could not find NodeId for reference "${reference}" in order list HTML`);
  }
  const nodeId = parseInt(idMatch[1], 10);
  log(`Resolved reference "${reference}" -> NodeId ${nodeId}`);

  // ---- 4. Read current status so we pick a different target statusId ----
  const orderRes = await fetchImpl(`${baseUrl}/OrderItemSurface/GetOrderItem?nodeId=${nodeId}`, {
    headers: { Cookie: cookieHeader },
  });
  const orderJson = await orderRes.json();
  log(`Current order state: Status=${orderJson.Status}, FollowUpDate=${orderJson.FollowUpDate}`);
  const parsedDate = new Date(orderJson.FollowUpDate);
  if (isNaN(parsedDate.getTime())) {
    throw new Error(
      `FollowUpDate "${orderJson.FollowUpDate}" did not parse with JS's new Date() - the ` +
        `2026-09-22 fix regressed (chalmers.ill.js's loadOrderItemSummary would throw again).`
    );
  }
  log(`FollowUpDate parses correctly as a real Date: ${parsedDate.toISOString()} (regression check passed)`);

  // "01:Ny" = statusId 5, "02:Åtgärda" = statusId 6 (Chalmers.ILL/UmbracoApi/ChillinOrderConfiguration.cs)
  const targetStatusId = orderJson.Status === "01:Ny" ? 6 : 5;

  // ---- 5. Connect the SignalR client and wait for the push ----
  const connection = new signalR.HubConnectionBuilder()
    .withUrl(`${baseUrl}/notificationHub`)
    .withAutomaticReconnect([5000])
    .build();

  const updatePromise = new Promise((resolve, reject) => {
    const timeout = setTimeout(() => reject(new Error("Timed out waiting for updateStream push (15s)")), 15000);
    connection.on("updateStream", (value) => {
      clearTimeout(timeout);
      resolve(value);
    });
  });

  await connection.start();
  log(`SignalR client connected to /notificationHub (state=${connection.state})`);

  // ---- 6. Trigger the status change over plain HTTP, as the UI's dropdown would ----
  log(`Triggering SetOrderItemStatus(orderNodeId=${nodeId}, statusId=${targetStatusId})...`);
  const setStatusRes = await fetchImpl(
    `${baseUrl}/OrderItemStatusSurface/SetOrderItemStatus?orderNodeId=${nodeId}&statusId=${targetStatusId}`,
    { headers: { Cookie: cookieHeader } }
  );
  const setStatusJson = await setStatusRes.json();
  if (!setStatusJson.Success) {
    throw new Error(`SetOrderItemStatus did not report success: ${JSON.stringify(setStatusJson)}`);
  }
  log(`Server confirmed status change: ${JSON.stringify(setStatusJson)}`);

  // ---- 7. Assert the push actually arrived, with the right NodeId ----
  const notification = await updatePromise;
  log(`Received updateStream push: ${JSON.stringify(notification)}`);
  if (notification.NodeId !== nodeId) {
    throw new Error(
      `updateStream push named NodeId=${notification.NodeId}, expected ${nodeId} - wrong order signaled!`
    );
  }
  if (typeof notification.EditedBy === "undefined" || typeof notification.SignificantUpdate === "undefined") {
    throw new Error(
      `updateStream payload missing expected fields (PascalCase regression?): ${JSON.stringify(notification)}`
    );
  }

  await connection.stop();

  // ---- 8. Re-fetch the order and confirm the client-side date-parse path still works post-update ----
  const afterRes = await fetchImpl(`${baseUrl}/OrderItemSurface/GetOrderItem?nodeId=${nodeId}`, {
    headers: { Cookie: cookieHeader },
  });
  const afterJson = await afterRes.json();
  log(`Post-update state: Status=${afterJson.Status}, FollowUpDate=${afterJson.FollowUpDate}`);
  if (afterJson.Status !== (targetStatusId === 6 ? "02:Åtgärda" : "01:Ny")) {
    throw new Error(`Status did not actually change server-side: ${afterJson.Status}`);
  }
  const afterDate = new Date(afterJson.FollowUpDate);
  if (isNaN(afterDate.getTime())) {
    throw new Error(`Post-update FollowUpDate "${afterJson.FollowUpDate}" failed to parse.`);
  }

  console.log("");
  console.log("ALL CHECKS PASSED:");
  console.log(`  - Hub connect/negotiate: OK`);
  console.log(`  - HTTP status change -> server-side push (correct NodeId ${nodeId}): OK`);
  console.log(`  - Push payload fields present and PascalCase (no camelCase regression): OK`);
  console.log(`  - FollowUpDate ISO-8601 parse before and after update (2026-09-22 fix intact): OK`);
  console.log(`  - Server-side status actually changed (${orderJson.Status} -> ${afterJson.Status}): OK`);
}

function dedupeCookies(cookies) {
  const map = new Map();
  for (const c of cookies) {
    const [name] = c.split("=");
    map.set(name, c);
  }
  return Array.from(map.values());
}

main().catch((err) => {
  console.error("FAILED:", err.message);
  process.exit(1);
});
