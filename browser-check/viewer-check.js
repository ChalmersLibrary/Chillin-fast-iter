// One-off check: log in as a role-less "Viewer" account and confirm (a) the order list still
// renders, (b) opening an order still renders, (c) attempting to write a log entry does NOT
// succeed and does NOT hang/crash the page - see the conversation for the ViewerReadOnlyFilter fix.
//
// Usage: node viewer-check.js [baseUrl] [login] [password]

const puppeteer = require("puppeteer-core");

const baseUrl = process.argv[2] || "http://localhost:5000";
const login = process.argv[3] || "viewer";
const password = process.argv[4] || "chillin123";

(async () => {
  const executablePath = process.env.CHROMIUM_EXECUTABLE_PATH;
  if (!executablePath) {
    console.error("CHROMIUM_EXECUTABLE_PATH is not set.");
    process.exit(1);
  }

  const browser = await puppeteer.launch({
    executablePath,
    headless: true,
    args: ["--no-sandbox", "--disable-dev-shm-usage", "--disable-gpu", "--single-process"],
  });

  try {
    const page = await browser.newPage();
    page.on("dialog", (dialog) => {
      console.error("DIALOG:", dialog.message());
      dialog.dismiss();
    });
    page.on("pageerror", (e) => console.error("PAGEERROR:", e.message));
    page.on("console", (m) => console.error("console:", m.text()));
    await page.setViewport({ width: 1280, height: 900 });

    await page.goto(baseUrl + "/", { waitUntil: "load", timeout: 20000 });
    await page.type("#Login", login);
    await page.type("#Password", password);
    await Promise.all([
      page.waitForNavigation({ waitUntil: "load", timeout: 20000 }),
      page.click("button[type=submit]"),
    ]);
    console.error("step: logged in, url=", page.url());

    await page.goto(baseUrl + "/bestaellningar/", { waitUntil: "load", timeout: 20000 });
    console.error("step: order list loaded, url=", page.url());
    await page.screenshot({ path: __dirname + "/screenshots/viewer-01-orderlist.png" });

    // Open the first order row (chalmers.ill.js binds .illedit's click handler to expand the
    // inline edit partial via $('#edit-' + id).load(...)).
    await page.waitForSelector(".illedit", { timeout: 15000 });
    await page.click(".illedit");
    await new Promise((r) => setTimeout(r, 1500));
    await page.screenshot({ path: __dirname + "/screenshots/viewer-02-orderitem.png" });
    console.error("step: opened first order item");

    const writeAttempt = await page.evaluate(async () => {
      const res = await fetch("/LogItemSurface/WriteLogItem", {
        method: "POST",
        headers: { "Content-Type": "application/x-www-form-urlencoded" },
        body: new URLSearchParams({
          nodeId: "1",
          Type: "KOMMENTAR",
          Message: "Viewer browser check - should never be written.",
          newFollowUpDate: "",
          statusId: "-1",
          cancellationReasonId: "-1",
          purchasedMaterialId: "-1",
        }),
      });
      return { status: res.status, redirected: res.redirected, url: res.url, bodyStart: (await res.text()).slice(0, 200) };
    });
    console.error("step: write attempt result:", JSON.stringify(writeAttempt));

    const logItems = await page.evaluate(async () => {
      const res = await fetch("/LogItemSurface/GetLogItems?nodeId=1");
      return res.text();
    });
    console.error("step: log items after write attempt contains our message:", logItems.includes("should never be written"));
  } finally {
    await browser.close();
  }
})().catch((e) => {
  console.error("Browser check failed:", e);
  process.exit(1);
});
