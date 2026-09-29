// One-off check: verify .btn-default .badge background-color after the Bootstrap 3.0.2 -> 3.0.3
// vendored-file fix (see conversation). Reuses the login flow from screenshot.js.
//
// Usage: node badge-check.js [baseUrl] [login] [password]

const puppeteer = require("puppeteer-core");

const baseUrl = process.argv[2] || "http://localhost:5000";
const login = process.argv[3] || "alice";
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
    page.on("dialog", (dialog) => dialog.dismiss());
    page.on("requestfailed", (r) => console.error("requestfailed:", r.url(), r.failure()));
    page.on("console", (m) => console.error("console:", m.text()));
    await page.setViewport({ width: 1280, height: 900 });

    console.error("step: goto /");
    await page.goto(baseUrl + "/", { waitUntil: "load", timeout: 45000 });
    console.error("step: type login");
    await page.type("#Login", login);
    await page.type("#Password", password);
    console.error("step: submit");
    await Promise.all([
      page.waitForNavigation({ waitUntil: "load", timeout: 45000 }),
      page.click("button[type=submit]"),
    ]);
    console.error("step: goto /bestaellningar/");

    await page.goto(baseUrl + "/bestaellningar/", { waitUntil: "load", timeout: 45000 });
    console.error("step: evaluate");

    const result = await page.evaluate(() => {
      const el = document.querySelector("#status00-counter");
      if (!el) return { error: "element #status00-counter not found" };
      const cs = getComputedStyle(el);
      return {
        text: el.textContent,
        classList: el.className,
        parentClassList: el.parentElement.className,
        backgroundColor: cs.backgroundColor,
        color: cs.color,
      };
    });
    console.log(JSON.stringify(result, null, 2));

    await page.screenshot({ path: __dirname + "/screenshots/badge-check.png", clip: { x: 0, y: 0, width: 1280, height: 300 } });
  } finally {
    await browser.close();
  }
})().catch((e) => {
  console.error("Browser check failed:", e);
  process.exit(1);
});
