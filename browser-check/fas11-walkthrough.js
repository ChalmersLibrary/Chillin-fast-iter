// Fas 11 (TODO-remove-dotnet-framework.md) browser verification pass: clicks through the
// remaining unverified order-detail action tabs, delivery-type sub-views, settings tabs,
// password-change flow (incl. wrong-password error path) and attachment upload/download.
// Not exhaustive of every seeded order/status combination - it opens whichever orders exist
// and calls whichever action functions their action-buttons panel actually renders.
//
// Usage: CHROMIUM_EXECUTABLE_PATH=... node fas11-walkthrough.js [baseUrl]
// Requires: app running in isolated mode against a DataPath with an admin (SuperAdmin) and an
// alice (Desk+Administrator, no SuperAdmin) account in members.json.

const puppeteer = require("puppeteer-core");
const path = require("path");
const fs = require("fs");

const baseUrl = process.argv[2] || "http://localhost:5000";
const outDir = path.join(__dirname, "screenshots", "fas11");
fs.mkdirSync(outDir, { recursive: true });

const findings = []; // { severity: "bug"|"note", text }
const consoleErrors = [];

function log(msg) {
  console.log(msg);
}

function note(text) {
  findings.push({ severity: "note", text });
  log("NOTE: " + text);
}

function bug(text) {
  findings.push({ severity: "bug", text });
  log("BUG: " + text);
}

async function shoot(page, name) {
  await page.screenshot({ path: path.join(outDir, name), fullPage: false });
}

const NAV_TIMEOUT = 60000;

async function login(page, loginName, password) {
  await page.goto(baseUrl + "/ChalmersILLLoginPage", { waitUntil: "load", timeout: NAV_TIMEOUT });
  await page.waitForSelector("#Login", { timeout: NAV_TIMEOUT });
  await page.evaluate(() => { document.getElementById("Login").value = ""; document.getElementById("Password").value = ""; });
  await page.type("#Login", loginName);
  await page.type("#Password", password);
  await Promise.all([
    page.waitForNavigation({ waitUntil: "load", timeout: NAV_TIMEOUT }),
    page.click("button[type=submit]"),
  ]);
  const url = page.url();
  // A failed login redirects back to the login page with ?error=... and leaves any prior
  // session's cookie untouched - silently continuing as the PREVIOUS user is worse than a loud
  // failure here, since every later step would then be testing the wrong account.
  if (url.includes("LoginPage")) {
    throw new Error(`Login as "${loginName}" failed (still on login page: ${url}) - a prior session may still be active.`);
  }
  return url;
}

async function callAction(page, fnName, nodeId) {
  await page.evaluate((fn, id) => { window[fn](id); }, fnName, nodeId);
  await new Promise((r) => setTimeout(r, 900));
}

async function main() {
  const executablePath = process.env.CHROMIUM_EXECUTABLE_PATH;
  if (!executablePath) {
    console.error("CHROMIUM_EXECUTABLE_PATH is not set.");
    process.exit(1);
  }

  const browser = await puppeteer.launch({
    executablePath,
    headless: true,
    args: ["--no-sandbox", "--disable-dev-shm-usage", "--disable-gpu"],
  });

  try {
    const page = await browser.newPage();
    page.on("dialog", (dialog) => { dialog.dismiss().catch(() => {}); });
    page.on("console", (msg) => {
      if (msg.type() === "error") consoleErrors.push(msg.text());
    });
    page.on("pageerror", (err) => consoleErrors.push("pageerror: " + err.message));
    await page.setViewport({ width: 1400, height: 1000 });

    // Unique per run so a leftover account from an interrupted previous run (cleanup can fail,
    // see step 7) never collides with this run's CreateMember call and gets silently reused with
    // a stale password.
    const TEMP_LOGIN = "e2etemp" + Date.now();

    // ---- 1. Login as admin (SuperAdmin) ----
    const afterAdminLoginUrl = await login(page, "admin", "chillin123");
    log("Logged in as admin, landed on " + afterAdminLoginUrl);
    await shoot(page, "01-admin-order-list.png");

    // ---- 2. Settings: Konton tab (SuperAdmin-only), create temp account ----
    await page.goto(baseUrl + "/bestaellningar/instaellningar/", { waitUntil: "load", timeout: NAV_TIMEOUT });
    await shoot(page, "02-settings-default-password-tab.png");

    const memberLinkExists = (await page.$("#member-settings-link")) !== null;
    if (!memberLinkExists) bug("Konton-fliken syns inte för admin (SuperAdmin) på Instaellningar-sidan.");
    else {
      await page.click("#member-settings-link");
      await new Promise((r) => setTimeout(r, 800));
      await shoot(page, "03-settings-konton-tab.png");

      await page.waitForSelector("#new-member-login", { timeout: 10000 });
      await page.type("#new-member-login", TEMP_LOGIN);
      await page.type("#new-member-password", "TempPass123!");
      await page.type("#new-member-roles", "Desk");
      await page.click('[onclick="createMember();"]');
      await new Promise((r) => setTimeout(r, 2500));
      await shoot(page, "04-settings-konton-after-create.png");
      let rowExists = (await page.$(`tr[data-login="${TEMP_LOGIN}"]`)) !== null;
      if (!rowExists) {
        // Single-core sandbox can be slow enough that the client-side reload after CreateMember
        // hasn't finished by the time we check (see chillin-puppeteer-sandbox-limitation memory).
        // Re-check with a fresh full navigation before concluding it's a real bug.
        await page.goto(baseUrl + "/bestaellningar/instaellningar/", { waitUntil: "load", timeout: NAV_TIMEOUT });
        await page.click("#member-settings-link");
        await new Promise((r) => setTimeout(r, 1500));
        rowExists = (await page.$(`tr[data-login="${TEMP_LOGIN}"]`)) !== null;
      }
      if (!rowExists) throw new Error(`Testkonto ${TEMP_LOGIN} syns inte i kontolistan efter CreateMember - kan inte fortsatta med lösenordsbyte-testerna.`);
      note(`Konto ${TEMP_LOGIN} skapat via UI och syns i listan.`);
    }

    // ---- 2b. Remaining settings tabs: Mallar, Leverantörsdata, Text (all visible to any logged-in user) ----
    for (const [linkId, label, shotName] of [
      ["#template-settings-link", "Mallar", "08-settings-mallar-tab.png"],
      ["#provider-settings-link", "Leverantörsdata", "09-settings-leverantorsdata-tab.png"],
    ]) {
      await page.goto(baseUrl + "/bestaellningar/instaellningar/", { waitUntil: "load", timeout: NAV_TIMEOUT });
      await page.click(linkId);
      await new Promise((r) => setTimeout(r, 1200));
      await shoot(page, shotName);
      const bodyText = await page.evaluate(() => document.getElementById("partial-settings-view").innerText.trim());
      if (!bodyText) bug("Instaellningar-fliken '" + label + "' renderade tomt.");
      else note("Instaellningar-fliken '" + label + "' renderar.");
    }
    // Text tab has no id on its <a>, only matched by its label text.
    await page.goto(baseUrl + "/bestaellningar/instaellningar/", { waitUntil: "load", timeout: NAV_TIMEOUT });
    await page.evaluate(() => {
      const link = Array.from(document.querySelectorAll(".setting-buttons-container a")).find((a) => a.textContent.trim() === "Text");
      if (link) link.click();
    });
    await new Promise((r) => setTimeout(r, 1200));
    await shoot(page, "10-settings-text-tab.png");
    const textTabBody = await page.evaluate(() => document.getElementById("partial-settings-view").innerText.trim());
    if (!textTabBody) bug("Instaellningar-fliken 'Text' renderade tomt.");
    else note("Instaellningar-fliken 'Text' renderar.");

    // ---- 3. Verify alice (Administrator, not SuperAdmin) does NOT see Konton tab ----
    await login(page, "alice", "chillin123");
    await page.goto(baseUrl + "/bestaellningar/instaellningar/", { waitUntil: "load", timeout: NAV_TIMEOUT });
    const aliceHasMemberLink = (await page.$("#member-settings-link")) !== null;
    if (aliceHasMemberLink) bug("alice (Administrator, ej SuperAdmin) ser aandaa Konton-fliken.");
    else note("alice (Administrator) ser korrekt INTE Konton-fliken (SuperAdmin-only haller).");
    await shoot(page, "05-alice-settings-no-konton-tab.png");

    // ---- 4. Password change flow as temp account: wrong current password, then correct ----
    await login(page, TEMP_LOGIN, "TempPass123!");
    await page.goto(baseUrl + "/bestaellningar/instaellningar/", { waitUntil: "load", timeout: NAV_TIMEOUT });
    await page.waitForSelector("#CurrentPassword", { timeout: 10000 });
    await page.type("#CurrentPassword", "WrongPassword!");
    await page.type("#NewPassword", "NewTempPass456!");
    await Promise.all([
      page.waitForNavigation({ waitUntil: "load", timeout: NAV_TIMEOUT }),
      page.click("form[action*='PasswordSurface'] button[type=submit]"),
    ]);
    await shoot(page, "06-password-change-wrong-current.png");
    const wrongPwBody = await page.evaluate(() => document.body.innerText);
    if (!wrongPwBody.includes("Fel lösenord")) bug("Lösenordsbyte med fel nuvarande lösenord visade inte felmeddelandet \"Fel lösenord\" (url=" + page.url() + ").");
    else note("Lösenordsbyte med fel nuvarande lösenord gav korrekt felmeddelande.");

    await page.waitForSelector("#CurrentPassword", { timeout: 10000 });
    await page.type("#CurrentPassword", "TempPass123!");
    await page.type("#NewPassword", "NewTempPass456!");
    await Promise.all([
      page.waitForNavigation({ waitUntil: "load", timeout: NAV_TIMEOUT }),
      page.click("form[action*='PasswordSurface'] button[type=submit]"),
    ]);
    await shoot(page, "07-password-change-success.png");
    const successBody = await page.evaluate(() => document.body.innerText);
    if (!successBody.toLowerCase().includes("ändrat")) bug("Lösenordsbyte med korrekt nuvarande lösenord visade inte success-meddelandet.");
    else note("Lösenordsbyte med korrekt nuvarande lösenord gav success-meddelande.");

    // Verify the new password actually works end-to-end (not just the UI message)
    const afterPwChangeLoginUrl = await login(page, TEMP_LOGIN, "NewTempPass456!");
    if (!afterPwChangeLoginUrl.includes("disk")) bug("Inloggning med det nya lösenordet lyckades inte hamna pa forvantad sida (fick " + afterPwChangeLoginUrl + ").");
    else note("Inloggning med nytt lösenord efter byte fungerar (Desk-konto landade pa /disk/).");

    // ---- 5. Back to admin: walk every order's action tabs ----
    await login(page, "admin", "chillin123");
    await page.goto(baseUrl + "/bestaellningar", { waitUntil: "load", timeout: NAV_TIMEOUT });
    const orderIds = await page.$$eval(".illedit", (els) => els.map((e) => e.id).filter(Boolean));
    log("Found " + orderIds.length + " seeded orders: " + orderIds.join(", "));

    const knownActionFns = [
      "loadReferenceAction", "loadPatronDataView", "loadMailAction", "loadProviderAction",
      "loadLogEntryAction", "loadDeliveryAction", "loadReceiveBookAction", "loadClaimAction",
      "loadReturnAction", "loadAnonymizeAction", "loadPatronReturnDateAction", "loadProviderReturnDateAction",
    ];

    for (const id of orderIds) {
      const errCountBefore = consoleErrors.length;
      await page.click(`[id="${id}"]`);
      await page.waitForSelector("#action-buttons-" + id, { timeout: 10000 }).catch(() => {});
      await new Promise((r) => setTimeout(r, 500));
      await shoot(page, "order-" + id + "-00-opened.png");

      const onclicks = await page.$$eval("#action-buttons-" + id + " [onclick]", (els) =>
        els.map((e) => e.getAttribute("onclick") || "")
      );
      const fnsPresent = new Set();
      for (const oc of onclicks) {
        for (const fn of knownActionFns) {
          if (oc.startsWith(fn + "(")) fnsPresent.add(fn);
        }
      }

      for (const fn of fnsPresent) {
        try {
          await callAction(page, fn, id);
          await shoot(page, "order-" + id + "-" + fn + ".png");
          const panelText = await page.evaluate((nid) => {
            const el = document.getElementById("action-" + nid);
            return el ? el.innerText.slice(0, 300) : null;
          }, id);
          if (panelText === null || panelText.trim() === "") {
            bug("Order " + id + ": " + fn + " renderade ett tomt action-panel.");
          }
        } catch (e) {
          bug("Order " + id + ": anrop av " + fn + " kastade ett JS-undantag: " + e.message);
        }
      }

      // Delivery tab: walk every delivery-type sub-view offered in the dropdown, not just the
      // one auto-selected by SierraInfo heuristics.
      if (fnsPresent.has("loadDeliveryAction")) {
        const deliveryOptions = await page.evaluate(() => {
          const items = Array.from(document.querySelectorAll("#delivery-type-dropdown ~ .dropdown-menu a[data-render-method]"));
          return items.map((a) => ({ method: a.getAttribute("data-render-method"), type: a.getAttribute("data-delivery-type") }));
        });
        for (const opt of deliveryOptions) {
          try {
            await page.evaluate((method, type, nid) => {
              window.renderDeliveryTypePartial(method, type);
            }, opt.method, opt.type, id);
            await new Promise((r) => setTimeout(r, 900));
            await shoot(page, "order-" + id + "-delivery-" + opt.type.replace(/[^a-z0-9]/gi, "") + ".png");
            const dtText = await page.evaluate(() => {
              const el = document.getElementById("delivery-type-view");
              return el ? el.innerText.trim() : null;
            });
            if (!dtText) bug("Order " + id + ": leveranssätt '" + opt.type + "' (" + opt.method + ") renderade tomt.");
          } catch (e) {
            bug("Order " + id + ": leveranssätt '" + opt.type + "' kastade JS-undantag: " + e.message);
          }
        }

        // Attachment upload/download: only try on the first order that offers a file input
        // for one of the direct-delivery types (epost/post/internpost).
        const hasFileInput = (await page.$("#hidden-file-upload")) !== null;
        if (hasFileInput && !global.__attachmentTested) {
          global.__attachmentTested = true;
          const tmpFile = path.join(outDir, "test-attachment.txt");
          fs.writeFileSync(tmpFile, "fas11 e2e test attachment\n");
          const input = await page.$("#hidden-file-upload");
          await input.uploadFile(tmpFile);
          await new Promise((r) => setTimeout(r, 5000));
          await shoot(page, "order-" + id + "-after-attachment-upload.png");
          let attachBtn = await page.$(".btn-view-attachment");
          if (!attachBtn) {
            // Single-core sandbox flakiness (see chillin-puppeteer-sandbox-limitation memory) -
            // give it a couple more chances before concluding it's a real bug.
            for (let i = 0; i < 2 && !attachBtn; i++) {
              await new Promise((r) => setTimeout(r, 4000));
              attachBtn = await page.$(".btn-view-attachment");
            }
          }
          if (!attachBtn) {
            bug("Order " + id + ": ingen attachment-knapp syns efter filuppladdning (aven efter extra vantetid).");
          } else {
            const link = await page.evaluate((btn) => btn.getAttribute("data-link"), attachBtn);
            const fullLink = link.startsWith("http") ? link : baseUrl.replace(/\/$/, "") + "/" + link.replace(/^\//, "");
            const dl = await page.evaluate(async (url) => {
              const res = await fetch(url);
              return { status: res.status, contentType: res.headers.get("content-type") };
            }, fullLink);
            if (dl.status !== 200) bug("Order " + id + ": nedladdning av uppladdad bilaga gav status " + dl.status + " (" + fullLink + ").");
            else note("Order " + id + ": filuppladdning + nedladdning av bilaga fungerar (status 200, content-type " + dl.contentType + ").");
          }
        }
      }

      const newErrs = consoleErrors.slice(errCountBefore);
      if (newErrs.length > 0) bug("Order " + id + ": " + newErrs.length + " JS-konsolfel under genomklick: " + newErrs.slice(0, 3).join(" | "));

      // Close this order before opening the next (mirrors closeOrderItem's own unlock call).
      await page.click(`[id="${id}"]`).catch(() => {});
      await new Promise((r) => setTimeout(r, 300));
    }

    // ---- 6. Remaining ChalmersILL.cshtml-layout pages ----
    for (const [urlPath, name] of [
      ["/ChalmersILLStatisticsPage", "20-statistics-page.png"],
      ["/ChalmersILLStartPage", "21-start-page.png"],
    ]) {
      await page.goto(baseUrl + urlPath, { waitUntil: "load", timeout: NAV_TIMEOUT });
      await shoot(page, name);
      const hasNavbar = (await page.$("nav.navbar")) !== null;
      if (!hasNavbar) bug(urlPath + " renderar utan layout (ingen .navbar hittad) - kan vara naken vy.");
      else note(urlPath + " renderar med fullständig layout.");
    }

    // Logout page: real navigation (not AJAX), also clears the session.
    await page.goto(baseUrl + "/ChalmersILLLogoutPage", { waitUntil: "load", timeout: NAV_TIMEOUT });
    await shoot(page, "22-logout-page.png");
    const stillAuthed = await page.evaluate(() => document.body.innerText.includes("Logga ut"));
    if (stillAuthed) bug("Efter utloggning visar sidan fortfarande 'Logga ut'-länk (navbaren tror man är inloggad).");
    else note("Utloggningssidan visar korrekt utloggat läge.");

    // Confirm the session is actually cleared server-side, not just the page shown.
    const afterLogoutResp = await page.goto(baseUrl + "/bestaellningar", { waitUntil: "load", timeout: NAV_TIMEOUT });
    if (!afterLogoutResp.url().includes("Login")) bug("Efter utloggning kommer man fortfarande åt /bestaellningar utan att omdirigeras till login (url=" + afterLogoutResp.url() + ").");
    else note("Efter utloggning omdirigeras /bestaellningar korrekt till login.");
    await shoot(page, "23-after-logout-order-list-redirect.png");

    // ---- 7. Cleanup: delete the temp account (log back in as admin first) ----
    await login(page, "admin", "chillin123");
    await page.goto(baseUrl + "/bestaellningar/instaellningar/", { waitUntil: "load", timeout: NAV_TIMEOUT });
    await page.click("#member-settings-link");
    await page.waitForSelector(`tr[data-login="${TEMP_LOGIN}"]`, { timeout: 10000 }).catch(() => {});
    const delBtn = await page.$(`tr[data-login="${TEMP_LOGIN}"] .btn-danger`);
    if (delBtn) {
      await delBtn.click();
      await new Promise((r) => setTimeout(r, 1500));
      const stillThere = (await page.$(`tr[data-login="${TEMP_LOGIN}"]`)) !== null;
      if (stillThere) bug(`Testkonto ${TEMP_LOGIN} kunde inte tas bort efter körningen (finns kvar i /home/node/data/members.json).`);
      else note(`Testkonto ${TEMP_LOGIN} borttaget efter körning.`);
    } else {
      bug(`Testkonto ${TEMP_LOGIN} hittades inte i kontolistan vid städning - kan finnas kvar i members.json.`);
    }
  } finally {
    await browser.close();
  }

  log("\n=== SAMMANFATTNING ===");
  const bugs = findings.filter((f) => f.severity === "bug");
  const notes = findings.filter((f) => f.severity === "note");
  log(bugs.length + " potentiella buggar, " + notes.length + " bekräftade OK.");
  for (const f of bugs) log("BUG: " + f.text);
  for (const f of notes) log("OK: " + f.text);
  if (consoleErrors.length > 0) {
    log("\nAlla JS-konsolfel under hela körningen (" + consoleErrors.length + "):");
    for (const e of consoleErrors) log(" - " + e);
  }

  fs.writeFileSync(path.join(outDir, "report.json"), JSON.stringify({ findings, consoleErrors }, null, 2));
}

main().catch((e) => {
  console.error("Walkthrough failed:", e);
  process.exit(1);
});
