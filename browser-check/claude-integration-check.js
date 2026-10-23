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

// The chat boxes send without reloading the page: a marker set on window before survives.
async function sendInPlace(page, send, what) {
  await page.evaluate(() => { window.__claudeNoReload = true; });
  await send();
  await page.waitForFunction(() => document.querySelector("[data-claude-send] textarea")?.value === "", { timeout: NAV_TIMEOUT, polling: 100 })
    .catch(() => problem(what + ": the box wasn't emptied."));
  if (!(await page.evaluate(() => window.__claudeNoReload === true))) problem(what + ": the page reloaded.");
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
  page.on("response", (res) => { if (res.status() >= 400) consoleErrors.push(res.status() + " " + res.url()); });
  page.on("dialog", (d) => d.accept());

  try {
    // 0. Bounce an idea in the chat box on the start page, then turn it into work.
    await login(page, "agneta");
    await page.goto(baseUrl + "/", { waitUntil: "load", timeout: NAV_TIMEOUT });
    // Chat convention: Shift+Enter is a new line, Enter sends.
    await page.type("[data-testid=claude-idea-form] textarea", "Tänk om man kunde se hur länge");
    await page.keyboard.down("Shift");
    await page.keyboard.press("Enter");
    await page.keyboard.up("Shift");
    await page.type("[data-testid=claude-idea-form] textarea", "en beställning har legat i samma status?");
    const typed = await page.$eval("[data-testid=claude-idea-form] textarea", (t) => t.value);
    if (!typed.includes("\n")) problem("Shift+Enter didn't make a new line.");
    if (page.url().includes("idea=")) problem("Shift+Enter sent the message.");
    await sendInPlace(page, () => page.keyboard.press("Enter"), "First idea message");
    if (!/[?]idea=\d+/.test(page.url())) problem("The address doesn't point at the new idea: " + page.url());
    await page.waitForFunction(() => document.querySelector("[data-testid=claude-idea-chat]")?.getAttribute("data-state") === "WaitingForOwner", { timeout: 60000, polling: 250 });
    await page.type("[data-testid=claude-idea-form] textarea", "Det är mest för handläggarna, så de ser vad som fastnat.");
    await sendInPlace(page, () => page.click("[data-testid=claude-idea-form] button[type=submit]"), "Idea reply");
    await page.waitForFunction(() => document.querySelectorAll("[data-testid=claude-idea-chat] > div").length >= 4 && document.querySelector("[data-testid=claude-idea-chat]")?.getAttribute("data-state") === "WaitingForOwner", { timeout: 60000, polling: 250 });
    await shoot(page, "00-idea-chat.png");
    await Promise.all([
      page.waitForNavigation({ waitUntil: "load", timeout: NAV_TIMEOUT }),
      page.click("[data-testid=claude-convert-form] button[type=submit]"),
    ]);
    if (!/\/Claude\/Conversation\//.test(page.url())) problem("Converting the idea didn't open it as work: " + page.url());
    await waitForState(page, "WaitingForOwner", 60000);
    await shoot(page, "00b-idea-became-work.png");

    // 1. Object owner: a plain round, watched live.
    await page.goto(baseUrl + "/", { waitUntil: "load", timeout: NAV_TIMEOUT });
    await shoot(page, "01-startpage-owner.png");

    const id = await startConversation(page, "Exportknapp", "Lägg till en exportknapp på beställningslistan.");
    // The page must update by itself (SignalR ping / poll) - no reloads from here on.
    await page.waitForFunction(() => /jobbar på/.test(document.querySelector("[data-testid=claude-status]")?.textContent || ""), { timeout: 30000, polling: 250 });
    await shoot(page, "02-claude-working.png");
    await waitForState(page, "WaitingForOwner", 60000);
    await page.waitForFunction(() => /Gröna tester/.test(document.querySelector("[data-testid=claude-rounds]")?.textContent || ""), { timeout: 30000, polling: 250 });
    await shoot(page, "03-delivered.png");
    const suggestion = await page.$eval("#claude-changelog", (el) => el.value);
    if (!suggestion.startsWith("(Fejkat förslag)")) problem("No changelog suggestion from Claude in the box: " + suggestion);
    const doneRow = await page.evaluateHandle(() => document.querySelector("[data-testid=claude-done-form]").closest(".row"));
    await doneRow.screenshot({ path: path.join(outDir, "03b-done-row.png") });

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

    // 2b. Claude asks instead of guessing; an answer continues the conversation.
    const qId = await startConversation(page, "Knapp", "Lägg till en knapp [fråga]");
    await page.waitForSelector("[data-testid=claude-question]", { timeout: 60000 });
    await shoot(page, "04b-question.png");
    await page.type("#claude-reply", "Överst, tack.");
    await sendInPlace(page, () => page.click("[data-testid=claude-reply-form] button[type=submit]"), "Conversation reply");
    await waitForState(page, "WaitingForOwner", 60000);
    // Claude's new suggestion moves into the box live, without a reload.
    await page.waitForFunction(() => document.getElementById("claude-changelog")?.value === "(Fejkat förslag) Överst, tack.", { timeout: 30000, polling: 250 })
      .catch(() => problem("The changelog suggestion didn't update live."));
    // The owner's own text is saved on change and stays through Claude's next round.
    await page.click("#claude-changelog", { clickCount: 3 });
    await page.type("#claude-changelog", "Knapp överst på sidan");
    await page.click("#claude-reply");
    await page.waitForResponse((r) => r.url().includes("/Claude/ChangelogText/"), { timeout: NAV_TIMEOUT });
    await page.type("#claude-reply", "Gör den grön också.");
    await sendInPlace(page, () => page.click("[data-testid=claude-reply-form] button[type=submit]"), "Second conversation reply");
    await waitForState(page, "ClaudeWorking", 30000).catch(() => {});
    await waitForState(page, "WaitingForOwner", 60000);
    await new Promise((r) => setTimeout(r, 1500));
    const kept = await page.$eval("#claude-changelog", (el) => el.value);
    if (kept !== "Knapp överst på sidan") problem("The owner's own changelog text was replaced: " + kept);
    if (qId === id) problem("Question conversation got the same id as the first one.");

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

    // IT switches development off (e.g. to work on the branch by hand) and back on.
    await Promise.all([
      page.waitForNavigation({ waitUntil: "load", timeout: NAV_TIMEOUT }),
      page.click("[data-testid=claude-pause-development]"),
    ]);
    if (!(await page.$("[data-testid=claude-development-paused]"))) problem("No pause banner after pausing.");
    await shoot(page, "06b-development-paused.png");
    await Promise.all([
      page.waitForNavigation({ waitUntil: "load", timeout: NAV_TIMEOUT }),
      page.click("[data-testid=claude-resume-development]"),
    ]);
    if (await page.$("[data-testid=claude-development-paused]")) problem("Pause banner still there after resuming.");

    await page.goto(baseUrl + "/Claude/Conversation/" + crashId, { waitUntil: "load", timeout: NAV_TIMEOUT });
    if (!(await page.$("[data-testid=claude-reply-form]"))) problem("IT has no reply form.");
    if (await page.$("[data-testid=claude-done-form]")) problem("IT got the done form.");

    // 4. Viewer: reads, no forms.
    await login(page, "viewer");
    // The first conversation is done and removed by now - open one that's still there.
    await page.goto(baseUrl + "/Claude/Conversation/" + crashId, { waitUntil: "load", timeout: NAV_TIMEOUT });
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
