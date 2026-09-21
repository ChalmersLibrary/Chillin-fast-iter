// Browser-driven verification for the "brytpunkt efter fas 2" (TODO-remove-dotnet-framework.md) -
// screenshots key pages so a person (or Claude) can actually *see* the app instead of guessing
// from HTML. Meant to be run repeatedly throughout fas 3-8, not just once.
//
// Prerequisites: app already running (dotnet run in Chalmers.ILL), and a login account in
// Chalmers.ILL/Config/members.json.
//
// Usage: npm run screenshot -- [baseUrl] [login] [password]
//   defaults: http://localhost:5000, alice, chillin123

const puppeteer = require("puppeteer-core");
const path = require("path");
const fs = require("fs");

const baseUrl = process.argv[2] || "http://localhost:5000";
const login = process.argv[3] || "alice";
const password = process.argv[4] || "chillin123";
const outDir = path.join(__dirname, "screenshots");
fs.mkdirSync(outDir, { recursive: true });

async function shoot(page, name) {
  const file = path.join(outDir, name);
  await page.screenshot({ path: file });
  console.log(`Saved ${name} - url=${page.url()} title="${await page.title()}"`);
}

(async () => {
  const executablePath = process.env.CHROMIUM_EXECUTABLE_PATH;
  if (!executablePath) {
    console.error("CHROMIUM_EXECUTABLE_PATH is not set - devcontainer needs chromium (see .devcontainer/Dockerfile).");
    process.exit(1);
  }

  const browser = await puppeteer.launch({
    executablePath,
    headless: true,
    args: ["--no-sandbox", "--disable-dev-shm-usage", "--disable-gpu"],
  });

  try {
    const page = await browser.newPage();
    // ChalmersILL.cshtml's SignalR wiring (chalmers.ill.js, original code, predates every
    // migration) calls window.alert() if the initial notificationHub connection fails - a
    // blocking dialog with no auto-dismiss hangs headless Chromium indefinitely (Puppeteer
    // does not dismiss dialogs on its own). Found 2026-09-21 verifying fas 11: the WebSocket
    // handshake is genuinely flaky under this devcontainer's constrained CPU, so this fires
    // often enough here to be a real problem for automated verification, even though it's not
    // a bug this migration introduced.
    page.on("dialog", (dialog) => dialog.dismiss());
    await page.setViewport({ width: 1280, height: 900 });

    await page.goto(baseUrl + "/", { waitUntil: "load", timeout: 15000 });
    await shoot(page, "01-login-redirect.png");

    await page.type("#Login", login);
    await page.type("#Password", password);
    await shoot(page, "02-login-filled.png");

    await Promise.all([
      page.waitForNavigation({ waitUntil: "load", timeout: 15000 }),
      page.click("button[type=submit]"),
    ]);
    await shoot(page, "03-after-login.png");

    await page.goto(baseUrl + "/bestaellningar/instaellningar/", { waitUntil: "load", timeout: 15000 });
    await shoot(page, "04-settings.png");
  } finally {
    await browser.close();
  }
})().catch((e) => {
  console.error("Browser check failed:", e);
  process.exit(1);
});
