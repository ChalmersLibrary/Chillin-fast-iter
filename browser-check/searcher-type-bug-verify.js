// Browser-level verification for the IOrderItemSearcher DI instance-vs-type-registration bug fix
// (Bootstrapper.cs: services.Replace(ServiceDescriptor.Singleton(orderItemSearcher)) instead of a
// type-based registration). Bug: changing an order's type, then reloading the order LIST page,
// kept showing the OLD type until process restart (list reads via IOrderItemSearcher, which was a
// different singleton instance than the one FileOrderItemManager writes through). Opening the
// order's own detail page always showed the new type (reads the JSON file directly).
//
// Usage: node searcher-type-bug-verify.js [baseUrl] [login] [password] [nodeId]

const puppeteer = require("puppeteer-core");
const path = require("path");
const fs = require("fs");

const baseUrl = process.argv[2] || "http://localhost:5000";
const login = process.argv[3] || "alice";
const password = process.argv[4] || "chillin123";
const nodeId = process.argv[5] || "1";
const outDir = path.join(__dirname, "screenshots", "searcher-type-bug");
fs.mkdirSync(outDir, { recursive: true });

async function shoot(page, name) {
  const file = path.join(outDir, name);
  await page.screenshot({ path: file });
  console.log(`Saved ${name} - url=${page.url()}`);
}

function typeColumnText(page, id) {
  return page.evaluate((id) => {
    const el = document.querySelector(`[id="${id}"] [data-column="type"]`);
    return el ? el.textContent.trim() : null;
  }, id);
}

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
    await page.setViewport({ width: 1280, height: 900 });

    // Log in
    await page.goto(baseUrl + "/", { waitUntil: "load", timeout: 45000 });
    await page.type("#Login", login);
    await page.type("#Password", password);
    await Promise.all([
      page.waitForNavigation({ waitUntil: "load", timeout: 45000 }),
      page.click("button[type=submit]"),
    ]);
    console.log("step: logged in, url=", page.url());

    // Open order list, record current type for our test node
    await page.goto(baseUrl + "/bestaellningar/", { waitUntil: "load", timeout: 45000 });
    await page.waitForSelector(`[id="${nodeId}"]`, { timeout: 30000 });
    const typeBefore = await typeColumnText(page, nodeId);
    console.log(`step: order list BEFORE change, node ${nodeId} type = "${typeBefore}"`);
    await shoot(page, "01-list-before.png");

    // Open the order's inline edit partial
    await page.click(`[id="${nodeId}"]`);
    await page.waitForSelector(`#edit-${nodeId} #orderitem-typelist`, { timeout: 30000 });
    await shoot(page, "02-edit-opened.png");

    // Find current type + an available different type option, and click it
    const { currentType, chosen } = await page.evaluate((nodeId) => {
      const list = document.querySelector(`#edit-${nodeId} #orderitem-typelist`);
      const current = list.querySelector("a.disabled-link .current");
      const currentType = current ? current.textContent.trim() : null;
      const options = Array.from(list.querySelectorAll("a[onclick]"));
      const target = options[0];
      const label = target ? target.textContent.trim() : null;
      if (target) target.click();
      return { currentType, chosen: label };
    }, nodeId);
    console.log(`step: current detail-panel type="${currentType}", clicked type option "${chosen}"`);

    // setOrderItemType is a $.getJSON call; give it a moment to complete.
    await new Promise((r) => setTimeout(r, 1500));
    await shoot(page, "03-after-type-change-click.png");

    // Confirm via detail page reload that the JSON file itself was updated (sanity check, not the bug)
    await page.goto(baseUrl + `/OrderItemSurface/RenderOrderItem?nodeId=${nodeId}`, { waitUntil: "load", timeout: 45000 });
    const detailPageText = await page.evaluate(() => document.body.textContent);
    console.log(`step: detail RenderOrderItem contains chosen type "${chosen}": ${detailPageText.includes(chosen)}`);

    // THE ACTUAL BUG: reload the order LIST page and check whether it now shows the new type
    await page.goto(baseUrl + "/bestaellningar/", { waitUntil: "load", timeout: 45000 });
    await page.waitForSelector(`[id="${nodeId}"]`, { timeout: 30000 });
    const typeAfter = await typeColumnText(page, nodeId);
    console.log(`step: order list AFTER change + reload, node ${nodeId} type = "${typeAfter}"`);
    await shoot(page, "04-list-after-reload.png");

    console.log("=== RESULT ===");
    console.log(`type before: "${typeBefore}", chosen: "${chosen}", type after reload: "${typeAfter}"`);
    if (typeAfter === chosen && typeAfter !== typeBefore) {
      console.log("PASS: order list reflects the new type immediately after reload - bug is fixed.");
    } else if (typeAfter === typeBefore) {
      console.log("FAIL: order list still shows the OLD type after reload - bug NOT fixed (or click didn't register).");
    } else {
      console.log("UNCLEAR: type after reload does not match either expected value - inspect screenshots/log.");
    }
  } finally {
    await browser.close();
  }
})().catch((e) => {
  console.error("Browser check failed:", e);
  process.exit(1);
});
