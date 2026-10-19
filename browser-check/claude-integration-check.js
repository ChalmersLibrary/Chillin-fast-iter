// Claudeborgen (claudeborgen-DESIGN.md) browser check: the Claude integration with FakeClaude in
// isolated mode - start a conversation as the object owner from the start page, watch the status go through the
// phases live (no reload), mark it done and find it in the start page changelog; then a [krasch]
// round, and the IT specialist's "Behöver IT" panel.
//
// Usage: CHROMIUM_EXECUTABLE_PATH=... node claude-integration-check.js [baseUrl]
// Requires: app in isolated mode with accounts agneta (ObjektSpecialist), ivar
// (ObjektSpecialistIT) and viewer (Viewer), passwords pw-<login>.

const puppeteer = require("puppeteer-core");
const path = require("path");
const fs = require("fs");

const baseUrl = process.argv[2] || "http://localhost:5000";
const outDir = path.join(__dirname, "screenshots", "claude");
fs.mkdirSync(outDir, { recursive: true });

const NAV_TIMEOUT = 60000;
const problems = [];
const consoleErrors = [];

function problem(text) {
  problems.push(text);
  console.log("PROBLEM: " + text);
}

async function shoot(page, name) {
  await page.screenshot({ path: path.join(outDir, name), fullPage: true });
  console.log("screenshot: " + name);
}

async function login(page, loginName) {
  await page.goto(baseUrl + "/ChalmersILLLogoutPage", { waitUntil: "load", timeout: NAV_TIMEOUT });
  await page.goto(baseUrl + "/ChalmersILLLoginPage", { waitUntil: "load", timeout: NAV_TIMEOUT });
  await page.waitForSelector("#Login", { timeout: NAV_TIMEOUT });
  await page.evaluate(() => { document.getElementById("Login").value = ""; document.getElementById("Password").value = ""; });
  await page.type("#Login", loginName);
  await page.type("#Password", "pw-" + loginName);
  await Promise.all([
    page.waitForNavigation({ waitUntil: "load", timeout: NAV_TIMEOUT }),
    page.click("button[type=submit]"),
  ]);
  if (page.url().includes("LoginPage")) throw new Error(`Login as ${loginName} failed: ${page.url()}`);
}

async function startConversation(page, title, body) {
  await page.goto(baseUrl + "/", { waitUntil: "load", timeout: NAV_TIMEOUT });
  await page.click("[data-testid=claude-start-toggle]");
  await page.waitForSelector("#claude-title", { visible: true, timeout: NAV_TIMEOUT });
  await page.type("#claude-title", title);
  await page.type("#claude-body", body);
  await Promise.all([
    page.waitForNavigation({ waitUntil: "load", timeout: NAV_TIMEOUT }),
    page.click("[data-testid=claude-start-form] button[type=submit]"),
  ]);
  return parseInt(page.url().match(/Conversation\/(\d+)/)[1], 10);
}

async function waitForState(page, state, timeoutMs) {
  await page.waitForFunction(
    (s) => { const el = document.querySelector("[data-testid=claude-conversation-state]"); return el && el.getAttribute("data-state") === s; },
    { timeout: timeoutMs, polling: 250 }, state);
}

(async () => {
  const browser = await puppeteer.launch({
    executablePath: process.env.CHROMIUM_EXECUTABLE_PATH,
    args: ["--no-sandbox", "--disable-dev-shm-usage"],
  });
  const page = await browser.newPage();
  await page.setViewport({ width: 1280, height: 900 });
  page.on("console", (msg) => { if (msg.type() === "error") consoleErrors.push(msg.text()); });
  page.on("pageerror", (err) => consoleErrors.push(err.message));
  page.on("dialog", (d) => d.accept());

  try {
    // 1. Object owner: a plain round, watched live.
    await login(page, "agneta");
    await page.goto(baseUrl + "/", { waitUntil: "load", timeout: NAV_TIMEOUT });
    await shoot(page, "01-startpage-owner.png");

    const id = await startConversation(page, "Exportknapp", "Lägg till en exportknapp på beställningslistan.");
    // The page must update by itself (SignalR ping / poll) - no reloads from here on.
    await page.waitForFunction(() => /jobbar på/.test(document.querySelector("[data-testid=claude-status]")?.textContent || ""), { timeout: 30000, polling: 250 });
    await shoot(page, "02-claude-working.png");
    await waitForState(page, "WaitingForOwner", 60000);
    await page.waitForFunction(() => /Gröna tester/.test(document.querySelector("[data-testid=claude-rounds]")?.textContent || ""), { timeout: 30000, polling: 250 });
    await shoot(page, "03-delivered.png");

    // 2. Done -> changelog on the start page.
    await page.evaluate(() => { document.getElementById("claude-changelog").value = "Exportknapp på beställningslistan"; });
    await Promise.all([
      page.waitForNavigation({ waitUntil: "load", timeout: NAV_TIMEOUT }),
      page.click("[data-testid=claude-done-form] button[type=submit]"),
    ]);
    await page.goto(baseUrl + "/", { waitUntil: "load", timeout: NAV_TIMEOUT });
    const changelog = await page.$$eval("[data-testid=claude-changelog-entry]", (els) => els.map((e) => e.textContent));
    if (!changelog.some((t) => t.includes("Exportknapp på beställningslistan"))) problem("Changelog entry missing on the start page.");
    await shoot(page, "04-startpage-changelog.png");

    // 3. A crashing round -> Behöver IT, seen by the IT specialist.
    const crashId = await startConversation(page, "Kraschtest", "Det här varvet kraschar [krasch]");
    await page.waitForFunction(() => !!document.querySelector("[data-testid=claude-needs-it]"), { timeout: 60000, polling: 250 });
    await shoot(page, "05-crashed.png");

    await login(page, "ivar");
    await page.goto(baseUrl + "/", { waitUntil: "load", timeout: NAV_TIMEOUT });
    const attention = await page.$eval("[data-testid=claude-attention]", (el) => el.textContent).catch(() => "");
    if (!attention.includes("Kraschtest")) problem("IT's attention panel does not list the crashed conversation.");
    if (await page.$("[data-testid=claude-start-form]")) problem("IT got the start form.");
    await shoot(page, "06-it-attention.png");

    await page.goto(baseUrl + "/Claude/Conversation/" + crashId, { waitUntil: "load", timeout: NAV_TIMEOUT });
    if (!(await page.$("[data-testid=claude-reply-form]"))) problem("IT has no reply form.");
    if (await page.$("[data-testid=claude-done-form]")) problem("IT got the done form.");

    // 4. Viewer: reads, no forms.
    await login(page, "viewer");
    await page.goto(baseUrl + "/Claude/Conversation/" + id, { waitUntil: "load", timeout: NAV_TIMEOUT });
    if (await page.$("form[data-testid^=claude-]")) problem("Viewer got a form.");
    await shoot(page, "07-viewer.png");
  } catch (e) {
    problem("Aborted: " + e.message);
    await shoot(page, "99-error.png").catch(() => {});
  } finally {
    await browser.close();
  }

  console.log("\nConsole errors: " + (consoleErrors.length ? "\n  " + consoleErrors.join("\n  ") : "none"));
  console.log("Problems: " + (problems.length ? "\n  " + problems.join("\n  ") : "none"));
  process.exit(problems.length || consoleErrors.length ? 1 : 0);
})();
