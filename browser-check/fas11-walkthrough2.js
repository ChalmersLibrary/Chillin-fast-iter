// Fas 11 (TODO-remove-dotnet-framework.md) follow-up to fas11-walkthrough.js.
//
// That first pass only reached the 4 seeded orders whose status the order list's hardcoded
// server-side query returns by default (01:Ny, 02:Åtgärda, 09:Mottagen, plus conditional
// 03:Beställd/14:Infodisk) - see ChalmersILLOrderListPageController.Index(). This pass reaches
// the other 13 seeded statuses via `/bestaellningar?query=<reference>`, which runs a free-text
// search with no status restriction (same controller, the `query` branch), so it finds any
// seeded order regardless of status.
//
// Additionally exercises, none of which the first pass reached:
// - deliver() on every delivery-type sub-view that exposes a "Leverera!" button
// - setOrderItemStatus / setOrderItemType / setOrderItemDeliveryLibrary via the real dropdown
//   menu items (not raw JS calls), on one order each
// - the OrderItemAnonymizationSurface/Anonymize endpoint (see notes below for why it's driven
//   directly rather than through loadAnonymizeAction's own trigger button)
// - saveDocument's failure path (import-from-URL with an unreachable URL - this sandbox has no
//   outbound network, see CLAUDE.md's "Chillin: minimal Azure-integration" note, so only the
//   failure path is reachable here)
//
// loadClaimAction/loadPatronReturnDateAction are deliberately NOT attempted here: both are gated
// (Chalmers.ILL.OrderItem.cshtml) on `CreateDate <= 2021-05-16` in addition to type/status - a
// freshly seeded order's CreateDate is always "now", so no seed data, however its status/type is
// chosen, can ever reach them. See the TODO entry for what this means for verification.
//
// Usage: CHROMIUM_EXECUTABLE_PATH=... node fas11-walkthrough2.js [baseUrl]
// Requires: app running in isolated mode against the same DataPath as fas11-walkthrough.js
// (admin/alice accounts in members.json, DevDataSeeder's 17 orders under DataPath/orders).

const puppeteer = require("puppeteer-core");
const path = require("path");
const fs = require("fs");

const baseUrl = process.argv[2] || "http://localhost:5000";
const outDir = path.join(__dirname, "screenshots", "fas11b");
fs.mkdirSync(outDir, { recursive: true });

const findings = [];
const consoleErrors = [];

function log(msg) { console.log(msg); }
function note(text) { findings.push({ severity: "note", text }); log("NOTE: " + text); }
function bug(text) { findings.push({ severity: "bug", text }); log("BUG: " + text); }

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
  if (url.includes("LoginPage")) {
    throw new Error(`Login as "${loginName}" failed (still on login page: ${url}).`);
  }
  return url;
}

async function callAction(page, fnName, nodeId) {
  await page.evaluate((fn, id) => { window[fn](id); }, fnName, nodeId);
  await new Promise((r) => setTimeout(r, 900));
}

// Reference strings from Chalmers.ILL/Isolated/DevDataSeeder.cs's SeedOrders array, minus the
// four statuses the first pass already reached via the plain order list (01:Ny, 02:Åtgärda,
// 09:Mottagen). 02:Åtgärda is skipped here even though it's included, since fas11-walkthrough.js
// already clicked through it - but note it's now Artikel+Huvudbiblioteket (this session's seed
// fix), so its ArticleInInfodisk delivery-type view is exercised below via a fresh query search.
const TARGET_REFS = [
  { ref: "ref-atgarda-002", status: "02:Åtgärda", type: "Artikel" }, // now Z, for ArticleInInfodisk
  { ref: "ref-vantar-004", status: "04:Väntar", type: "Bok" },
  { ref: "ref-levererad-005", status: "05:Levererad", type: "Artikel" },
  { ref: "ref-annullerad-006", status: "06:Annullerad", type: "Bok" },
  { ref: "ref-overford-007", status: "07:Överförd", type: "Inköpsförslag" },
  { ref: "ref-inkopt-008", status: "08:Inköpt", type: "Bok" },
  { ref: "ref-atersand-010", status: "10:Återsänd", type: "Bok" },
  { ref: "ref-utlanad-011", status: "11:Utlånad", type: "Bok" },
  { ref: "ref-kravd-012", status: "12:Krävd", type: "Bok" },
  { ref: "ref-transport-013", status: "13:Transport", type: "Bok" },
  { ref: "ref-forlorad-fraga-015", status: "15:Förlorad?", type: "Artikel" },
  { ref: "ref-forlorad-016", status: "16:Förlorad", type: "Bok" },
  { ref: "ref-folio-017", status: "17:FOLIO", type: "Bok" },
];

const knownActionFns = [
  "loadReferenceAction", "loadPatronDataView", "loadMailAction", "loadProviderAction",
  "loadLogEntryAction", "loadDeliveryAction", "loadReceiveBookAction", "loadClaimAction",
  "loadReturnAction", "loadAnonymizeAction", "loadPatronReturnDateAction", "loadProviderReturnDateAction",
];

// Single-core sandbox (see chillin-puppeteer-sandbox-limitation memory): a navigation
// occasionally exceeds even a 60s timeout under load. One retry with a fresh navigation clears
// it every time observed so far - a real hang would still surface as two failures in a row.
async function gotoWithRetry(page, url) {
  try {
    await page.goto(url, { waitUntil: "load", timeout: NAV_TIMEOUT });
  } catch (e) {
    if (!String(e).includes("Timeout")) throw e;
    note(`Navigering till ${url} tog för lång tid (${NAV_TIMEOUT}ms), försöker en gång till (enkelkärnig sandbox).`);
    await page.goto(url, { waitUntil: "load", timeout: NAV_TIMEOUT });
  }
}

async function openByQuery(page, ref) {
  await gotoWithRetry(page, baseUrl + "/bestaellningar?query=" + encodeURIComponent(ref));
  const ids = await page.$$eval(".illedit", (els) => els.map((e) => e.id).filter(Boolean));
  if (ids.length !== 1) {
    bug(`Query "${ref}" gav ${ids.length} träffar, väntade exakt 1.`);
    return null;
  }
  const id = ids[0];
  await page.click(`[id="${id}"]`);
  await page.waitForSelector("#action-buttons-" + id, { timeout: 10000 }).catch(() => {});
  await new Promise((r) => setTimeout(r, 500));
  return id;
}

async function walkActionsAndDelivery(page, id, opts) {
  const errCountBefore = consoleErrors.length;
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
  note(`Order ${id}: knappar ${Array.from(fnsPresent).join(", ") || "(inga)"}.`);

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

  if (opts.exerciseDelivery && !fnsPresent.has("loadDeliveryAction")) {
    // Chalmers.ILL.OrderItem.cshtml only renders the "Leverans"-button (loadDeliveryAction) for
    // Type=="Artikel" (line 236-239), even though the partial it loads
    // (Chalmers.ILL.Action.Delivery.cshtml) has a whole Type=="Bok" branch with its own
    // BookInstantLoan/BookReadAtLibrary options - unreachable through any visible UI element for
    // a Bok-type order. Confirmed by grepping every .cshtml/.js for "loadDeliveryAction": the
    // only caller besides that gated button is the DeliveryType partials themselves (their "back"
    // links). Force the call directly to still characterize what those two views actually do.
    bug("Order " + id + " (Bok): 'Leverans'-knappen (loadDeliveryAction) renderas aldrig för Bok-typ (Chalmers.ILL.OrderItem.cshtml:236 gate på Type==\"Artikel\"), trots att Chalmers.ILL.Action.Delivery.cshtml har en hel Bok-gren (BookInstantLoan/BookReadAtLibrary) - de vyerna verkar odåtkomliga via UI:t för Bok-ordrar. Anropar loadDeliveryAction direkt för att ändå karakterisera vad vyerna gör.");
    await callAction(page, "loadDeliveryAction", id);
  }

  if (opts.exerciseDelivery) {
    const deliveryOptions = await page.evaluate(() => {
      const items = Array.from(document.querySelectorAll("#delivery-type-dropdown ~ .dropdown-menu a[data-render-method]"));
      return items.map((a) => ({ method: a.getAttribute("data-render-method"), type: a.getAttribute("data-delivery-type") }));
    });
    for (const opt of deliveryOptions) {
      try {
        await page.evaluate((method, type) => {
          window.renderDeliveryTypePartial(method, type);
        }, opt.method, opt.type);
        await new Promise((r) => setTimeout(r, 900));
        await shoot(page, "order-" + id + "-delivery-" + opt.type.replace(/[^a-z0-9]/gi, "") + ".png");
        const dtText = await page.evaluate(() => {
          const el = document.getElementById("delivery-type-view");
          return el ? el.innerText.trim() : null;
        });
        if (!dtText) {
          bug("Order " + id + ": leveranssätt '" + opt.type + "' (" + opt.method + ") renderade tomt.");
          continue;
        }
        note("Order " + id + ": leveranssätt '" + opt.type + "' renderar.");

        if (opts.deliverType === opt.type) {
          const btn = await page.evaluateHandle(() => {
            const els = Array.from(document.querySelectorAll("#delivery-type-view .btn-success"));
            return els.find((e) => e.textContent.trim() === "Leverera!") || null;
          });
          const btnEl = btn.asElement();
          if (!btnEl) {
            bug("Order " + id + ": ingen 'Leverera!'-knapp hittades för '" + opt.type + "'.");
          } else {
            await btnEl.click();
            await new Promise((r) => setTimeout(r, 1200));
            await shoot(page, "order-" + id + "-after-deliver-" + opt.type.replace(/[^a-z0-9]/gi, "") + ".png");
            note("Order " + id + ": deliver() anropad för '" + opt.type + "', ingen JS-krasch.");
          }
        }
      } catch (e) {
        bug("Order " + id + ": leveranssätt '" + opt.type + "' kastade JS-undantag: " + e.message);
      }
    }
  }

  const newErrs = consoleErrors.slice(errCountBefore);
  if (newErrs.length > 0) bug("Order " + id + ": " + newErrs.length + " JS-konsolfel: " + newErrs.slice(0, 3).join(" | "));
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
    page.on("console", (msg) => { if (msg.type() === "error") consoleErrors.push(msg.text()); });
    page.on("pageerror", (err) => consoleErrors.push("pageerror: " + err.message));
    await page.setViewport({ width: 1400, height: 1000 });

    await login(page, "admin", "chillin123");
    log("Logged in as admin.");

    // ---- 1. Walk every remaining seeded status via query search ----
    for (const target of TARGET_REFS) {
      const id = await openByQuery(page, target.ref);
      if (!id) continue;
      const exerciseDelivery = target.type === "Artikel" || target.type === "Bok";
      // Only actually click "Leverera!" (a real state transition) on orders that aren't needed
      // intact for anything later in this script. "infodisk" is the data-delivery-type value for
      // both ArticleInInfodisk (002, Artikel+Huvudbiblioteket) and BookInstantLoan (004, Bok) -
      // two different deliver() implementations that happen to share the same dropdown key.
      const deliverType = (target.ref === "ref-atgarda-002" || target.ref === "ref-vantar-004") ? "infodisk" : null;
      await walkActionsAndDelivery(page, id, { exerciseDelivery, deliverType });
      await page.click(`[id="${id}"]`).catch(() => {});
      await new Promise((r) => setTimeout(r, 300));
    }

    // ---- 3. setOrderItemStatus / setOrderItemType / setOrderItemDeliveryLibrary via real UI ----
    // NOTE: Chalmers.ILL.OrderItem.cshtml has a real duplicate-id bug - `id="orderitem-statuslist"`
    // is reused on THREE different <ul>s (the actual status list at line 76, the delivery-library
    // list at line 183, and the Inköpsförslag purchase-library list at line 206). getElementById
    // would silently always resolve to the first (status) one, so a naive script targeting these
    // dropdowns by id could never actually reach setOrderItemDeliveryLibrary at all. Worked around
    // here by matching on the onclick handler prefix instead of the (non-unique, useless) id -
    // logged as a real markup bug below regardless, since it'd bite a real screen reader/anchor
    // link/CSS selector just as much as it bit this script.
    {
      const dupIds = await page.evaluate(() => {
        const all = Array.from(document.querySelectorAll("[id]")).map((e) => e.id);
        const seen = new Set();
        const dups = new Set();
        for (const i of all) { if (seen.has(i)) dups.add(i); seen.add(i); }
        return Array.from(dups);
      });
      if (dupIds.length > 0) bug("Dubblerade DOM-id:n på sidan (getElementById blir odefinierat vilket element som träffas): " + dupIds.join(", "));

      const id = await openByQuery(page, "ref-annullerad-006");
      if (id) {
        const statusChanged = await page.evaluate((nid) => {
          const link = Array.from(document.querySelectorAll(`#action-buttons-${nid} a[onclick^="setOrderItemStatus("]`))
            .find((a) => !a.classList.contains("disabled-link"));
          if (!link) return null;
          const label = link.textContent.trim();
          link.click();
          return label;
        }, id);
        await new Promise((r) => setTimeout(r, 1200));
        await shoot(page, "order-" + id + "-after-status-change.png");
        if (statusChanged === null) bug("Order " + id + ": ingen klickbar status-lista hittades.");
        else note("Order " + id + ": setOrderItemStatus via UI-dropdown → '" + statusChanged + "', ingen JS-krasch.");

        const typeChanged = await page.evaluate((nid) => {
          const link = Array.from(document.querySelectorAll(`#action-buttons-${nid} a[onclick^="setOrderItemType("]`))
            .find((a) => !a.classList.contains("disabled-link"));
          if (!link) return null;
          const label = link.textContent.trim();
          link.click();
          return label;
        }, id);
        await new Promise((r) => setTimeout(r, 1200));
        await shoot(page, "order-" + id + "-after-type-change.png");
        if (typeChanged === null) bug("Order " + id + ": ingen klickbar typ-lista hittades.");
        else note("Order " + id + ": setOrderItemType via UI-dropdown → '" + typeChanged + "', ingen JS-krasch.");

        const libraryChanged = await page.evaluate((nid) => {
          const link = Array.from(document.querySelectorAll(`#action-buttons-${nid} a[onclick^="setOrderItemDeliveryLibrary("]`))
            .find((a) => !a.classList.contains("disabled-link"));
          if (!link) return null;
          const label = link.textContent.trim();
          link.click();
          return label;
        }, id);
        await new Promise((r) => setTimeout(r, 1200));
        await shoot(page, "order-" + id + "-after-library-change.png");
        if (libraryChanged === null) bug("Order " + id + ": ingen klickbar leveransbibliotek-lista hittades.");
        else note("Order " + id + ": setOrderItemDeliveryLibrary via UI-dropdown → '" + libraryChanged + "', ingen JS-krasch.");
      }
    }

    // ---- 4. Anonymize: driven directly against the endpoint, not via loadAnonymizeAction's own
    // trigger button. That button/auto-load only appears once IsAnonymized or
    // IsAnonymizedAutomatically is already true (Chalmers.ILL.OrderItem.cshtml:43-48,466-468) -
    // a state DevDataSeeder's fresh orders never have (same shape of gap as the
    // CreateDate<=2021-05-16 gate on Claim/PatronReturnDate). Calling the endpoint directly still
    // exercises the real server-side anonymize() code path end-to-end, which is the part worth
    // characterizing; only the "which button is visible when" UI logic is skipped, and that's
    // plainly readable from the .cshtml source already. ----
    {
      const id = await openByQuery(page, "ref-forlorad-016");
      if (id) {
        const result = await page.evaluate(async (nid) => {
          const res = await fetch("/OrderItemAnonymizationSurface/Anonymize", {
            method: "POST",
            headers: { "Content-Type": "application/x-www-form-urlencoded" },
            body: "nodeId=" + nid + "&reference=" + encodeURIComponent("anonymiserad testreferens") + "&logsSerialized=" + encodeURIComponent("[]"),
          });
          const text = await res.text();
          return { status: res.status, text };
        }, id);
        if (result.status !== 200) {
          bug("Order " + id + ": Anonymize-endpointen gav status " + result.status + ": " + result.text.slice(0, 200));
        } else {
          let json;
          try { json = JSON.parse(result.text); } catch { json = null; }
          if (!json || !json.Success) {
            bug("Order " + id + ": Anonymize-endpointen svarade 200 men Success=false: " + result.text.slice(0, 200));
          } else {
            note("Order " + id + ": Anonymize-endpointen lyckades (Success=true).");
            // Reload and confirm the order now shows as anonymized, and that "Anonymisera mera"
            // (loadAnonymizeAction's real trigger, per the .cshtml condition) now appears.
            await page.goto(baseUrl + "/bestaellningar?query=" + encodeURIComponent("ref-forlorad-016"), { waitUntil: "load", timeout: NAV_TIMEOUT });
            const ids2 = await page.$$eval(".illedit", (els) => els.map((e) => e.id).filter(Boolean));
            if (ids2.length === 1) {
              await page.click(`[id="${ids2[0]}"]`);
              await page.waitForSelector("#action-buttons-" + ids2[0], { timeout: 10000 }).catch(() => {});
              await new Promise((r) => setTimeout(r, 500));
              await shoot(page, "order-" + ids2[0] + "-after-anonymize.png");
              const hasAnonymizeMeraButton = await page.evaluate((nid) => {
                const btn = document.querySelector(`#action-buttons-${nid} button[onclick^="loadAnonymizeAction("]`);
                return btn ? btn.textContent.trim() : null;
              }, ids2[0]);
              if (hasAnonymizeMeraButton === null) bug("Order " + ids2[0] + ": efter anonymisering visas inte 'Anonymisera mera'-knappen (IsAnonymized borde vara true).");
              else note("Order " + ids2[0] + ": efter anonymisering visas '" + hasAnonymizeMeraButton + "'-knappen som förväntat (loadAnonymizeAction).");
            } else {
              bug(`Query efter anonymisering gav ${ids2.length} träffar, väntade exakt 1.`);
            }
          }
        }
      }
    }

    // ---- 5. saveDocument failure path (no outbound network in this sandbox - see CLAUDE.md's
    // "Chillin: minimal Azure-integration" note - so only the unreachable-URL error path is
    // reachable here, not a real import). ----
    {
      const id = await openByQuery(page, "ref-mottagen-009"); // Artikel, already has a delivery tab
      if (id) {
        await callAction(page, "loadDeliveryAction", id);
        const hasSaveDocument = await page.evaluate(() => typeof window.saveDocument === "function");
        if (!hasSaveDocument) {
          bug("Order " + id + ": window.saveDocument är inte en funktion efter loadDeliveryAction.");
        } else {
          const errCountBefore = consoleErrors.length;
          await page.evaluate((nid) => { window.saveDocument(nid, "http://unreachable.invalid/test.pdf"); }, id);
          await new Promise((r) => setTimeout(r, 4000));
          const newErrs = consoleErrors.slice(errCountBefore);
          await shoot(page, "order-" + id + "-savedocument-failure.png");
          if (newErrs.length > 0) bug("Order " + id + ": saveDocument-felvägen gav " + newErrs.length + " JS-konsolfel: " + newErrs.slice(0, 3).join(" | "));
          else note("Order " + id + ": saveDocument mot en onåbar URL misslyckas utan JS-krasch (visar felmeddelande via alert, som dismissas automatiskt här). Lyckad import kräver riktig utgående nätverksåtkomst - inte testbart i den här sandboxen.");
        }
      }
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
