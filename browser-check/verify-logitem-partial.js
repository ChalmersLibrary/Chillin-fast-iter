// Ad-hoc check for the last open item in "Partial view-upplösning" (fas 11,
// TODO-remove-dotnet-framework.md): Chalmers.ILL.LogItem / LogItemSurfaceController.GetLogItemsAsPartial.
// A code search found no client-side caller anywhere in wwwroot/scripts or any .cshtml - only a
// controller unit test exercises it. This script hits the endpoint directly (authenticated) to
// confirm it renders without a server error even though nothing in the live UI currently reaches it.
const puppeteer = require("puppeteer-core");

const baseUrl = process.argv[2] || "http://localhost:5000";

const NAV_TIMEOUT = 180000;

async function login(page, loginName, password) {
  await page.goto(baseUrl + "/ChalmersILLLoginPage", { waitUntil: "load", timeout: NAV_TIMEOUT });
  await page.waitForSelector("#Login", { timeout: NAV_TIMEOUT });
  await page.type("#Login", loginName);
  await page.type("#Password", password);
  await Promise.all([
    page.waitForNavigation({ waitUntil: "load", timeout: NAV_TIMEOUT }),
    page.click("button[type=submit]"),
  ]);
}

(async () => {
  const browser = await puppeteer.launch({
    executablePath: process.env.CHROMIUM_EXECUTABLE_PATH || "/usr/bin/chromium",
    headless: "new",
    args: ["--no-sandbox", "--disable-dev-shm-usage"],
  });
  try {
    const page = await browser.newPage();
    const consoleErrors = [];
    page.on("console", msg => { if (msg.type() === "error") consoleErrors.push(msg.text()); });
    page.on("pageerror", err => consoleErrors.push(String(err)));

    await login(page, "admin", "chillin123");

    const response = await page.goto(baseUrl + "/LogItemSurface/GetLogItemsAsPartial?nodeid=1", { waitUntil: "load", timeout: NAV_TIMEOUT });
    console.log("HTTP status:", response.status());
    const body = await page.evaluate(() => document.body.innerText);
    console.log("Body length:", body.length);
    console.log("Body snippet:", body.slice(0, 500));
    console.log("Console errors:", consoleErrors);
  } finally {
    await browser.close();
  }
})();
