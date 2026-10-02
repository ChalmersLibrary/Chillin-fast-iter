import { expect, test } from "../fixtures";
import { OrderListPage } from "../pages/OrderListPage";
import { createOrderThroughMail } from "../pages/StartPage";

// Scenario IDs refer to e2e/scenarios/SCENARIOS.md.
//
// ORD-001 and ORD-002 own ref-ny-001 and ref-atgarda-002 from the seed data and share the worker's
// app. Everything after them makes its own order through the mail form, which means its own app
// too (`ownApp`): polling the mailbox also runs the daily housekeeping, which rewrites orders whose
// follow-up date has passed - several of the seed orders.

test.describe("orders, on the shared app", () => {
  test("ORD-001: changing an order's type shows in the order list straight away", async ({ page }) => {
    const list = new OrderListPage(page);
    await list.goto();

    const row = list.row("ref-ny-001");
    await expect(row.type).toHaveText("Bok");
    await row.open();
    await row.setType("Artikel");

    // Regression: the list used to keep showing the old type until the app restarted.
    await list.reload();
    await expect(list.row("ref-ny-001").type).toHaveText("Artikel");
  });

  test("ORD-002: an edited reference is saved and shown in the list", async ({ page }) => {
    const list = new OrderListPage(page);
    await list.goto();

    const row = list.row("ref-atgarda-002");
    await row.open();
    await row.setReference("Ändrad referens åäö – e2e");

    await list.reload();
    await expect(list.row("Ändrad referens åäö").reference).toContainText("e2e");
  });
});

test.describe("orders, each on its own order", () => {
  test("ORD-003: a status change shows in the list straight away", async ({ ownApp, guard }) => {
    guard.allow(/Successfully sent new order/);
    const { page } = await ownApp();
    const reference = await createOrderThroughMail(page, "e2e-ord-003");

    const list = new OrderListPage(page);
    await list.goto();
    const row = list.row(reference);
    await expect(row.status).toHaveText("Ny");
    await row.open();
    // A new order has no type yet, and the status button only appears once it has one.
    await row.setType("Bok");
    await row.setStatus("Åtgärda");

    await list.reload();
    await expect(list.row(reference).status).toHaveText("Åtgärda");
  });

  test("ORD-004: cancelling requires a reason, and the reason is kept on the order", async ({ ownApp, guard }) => {
    guard.allow(/Successfully sent new order/);
    const { page } = await ownApp();
    const reference = await createOrderThroughMail(page, "e2e-ord-004");

    const list = new OrderListPage(page);
    await list.goto();
    const row = list.row(reference);
    await row.open();
    await row.setType("Bok");
    // "Annullerad" is a submenu - there is no way to reach the status without picking a reason.
    await row.cancelWithReason("Finns Z");

    // An annulled order is no longer pending, so it drops out of the default list - it has to be
    // searched for. That is the app working as intended, not the change failing to stick.
    const cancelled = await list.find(reference);
    await expect(cancelled.status).toHaveText("Annullerad");
    await cancelled.open();
    await expect(cancelled.details).toContainText("Finns Z");
  });

  test("ORD-005: marking an order purchased requires the kind of material", async ({ ownApp, guard }) => {
    guard.allow(/Successfully sent new order/);
    const { page } = await ownApp();
    const reference = await createOrderThroughMail(page, "e2e-ord-005");

    const list = new OrderListPage(page);
    await list.goto();
    const row = list.row(reference);
    await row.open();
    await row.setType("Inköpsförslag");
    await row.markPurchased("Bok");

    // Inköpt is not a pending status either - search rather than reload (see ORD-004).
    const purchased = await list.find(reference);
    await expect(purchased.status).toHaveText("Inköpt");
  });

  test("ORD-006: changing the delivery library shows in the list's library column", async ({ ownApp, guard }) => {
    guard.allow(/Successfully sent new order/);
    const { page } = await ownApp();
    const reference = await createOrderThroughMail(page, "e2e-ord-006", { library: "Z" });

    const list = new OrderListPage(page);
    await list.goto();
    const row = list.row(reference);
    await expect(row.root.locator("[data-column='deliveryLibrary']")).toHaveText("Z");
    await row.open();
    await row.setType("Bok");
    await row.setDeliveryLibrary("Kuggen");

    await list.reload();
    await expect(list.row(reference).root.locator("[data-column='deliveryLibrary']")).toHaveText("Zl");
  });

  test("ORD-007: a purchase suggestion can be given a purchase library, which shows in the type column", async ({ ownApp, guard }) => {
    guard.allow(/Successfully sent new order/);
    const { page } = await ownApp();
    const reference = await createOrderThroughMail(page, "e2e-ord-007");

    const list = new OrderListPage(page);
    await list.goto();
    const row = list.row(reference);
    await row.open();
    // The purchase library button only exists for this type.
    await row.setType("Inköpsförslag");
    // The purchase libraries are HB and ACE (OrderItemModel.PurchaseLibraries), not the delivery
    // libraries' sigels.
    await row.setPurchaseLibrary("ACE");

    await list.reload();
    // For a purchase suggestion the list prints the purchase library in front of the type.
    await expect(list.row(reference).type).toHaveText("ACE Inköpsförslag");
  });
});

test.describe("orders, the rest of the panel", () => {
  test("ORD-008: a log entry is saved and shown with its date and author", async ({ ownApp, guard }) => {
    guard.allow(/Successfully sent new order/);
    const { page } = await ownApp();
    const reference = await createOrderThroughMail(page, "e2e-ord-008");

    const list = new OrderListPage(page);
    await list.goto();
    const row = list.row(reference);
    await row.open();

    const message = "Anteckning från e2e åäö";
    const saved = page.waitForResponse((r) => r.url().includes("/LogItemSurface/WriteLogItem"));
    await row.details.getByTestId("order-open-log-entry").click();
    await page.getByTestId("log-entry-message").fill(message);
    await page.getByTestId("log-entry-save").click();
    expect((await saved).ok()).toBeTruthy();

    // The entry shows up in the order's own history, with today's date and the account that
    // wrote it - the dates came out as "/Date(…)/" once, which is what this pins down.
    const reopened = await list.find(reference);
    await reopened.open();
    const history = page.locator(".editmode");
    await expect(history).toContainText(message);
    await expect(history).toContainText("superadmin");
    await expect(history).toContainText(new Date().toISOString().slice(0, 10));
    await expect(history).not.toContainText("/Date(");
  });

  test("ORD-009: the history groups by date without falling over (regression 5370fa7)", async ({ page }) => {
    // Rendering every seed order's panel exercises the log grouping over all statuses at once,
    // and without opening them in the UI it takes no locks from other scenarios.
    for (let nodeId = 1; nodeId <= 17; nodeId++) {
      const response = await page.request.get(`/OrderItemSurface/RenderOrderItem?nodeId=${nodeId}`);
      expect(response.status(), `RenderOrderItem ${nodeId}`).toBe(200);
      const html = await response.text();
      expect(html, `RenderOrderItem ${nodeId}`).not.toMatch(/Exception|KeyNotFound|Stack trace/);
      expect(html).toContain("order-details");
    }
  });

  test("ORD-010: patron data shows the patron's name, e-mail and card number", async ({ ownApp, guard }) => {
    guard.allow(/Successfully sent new order/);
    const { page } = await ownApp();
    const reference = await createOrderThroughMail(page, "e2e-ord-010", {
      name: "Övdis Ångström",
      email: "ovdis.angstrom@example.invalid",
      cardNo: "1122334455",
    });

    const list = new OrderListPage(page);
    await list.goto();
    const row = list.row(reference);
    await row.open();

    const details = page.locator(".editmode");
    await expect(details).toContainText("Övdis Ångström");
    await expect(details).toContainText("ovdis.angstrom@example.invalid");
    await expect(details).toContainText("1122334455");
  });

  test("ORD-013: a save that fails says so instead of looking like it worked (regression 1410e49)", async ({ ownApp, guard }) => {
    guard.allow(/Successfully sent new order/);
    const { page } = await ownApp();
    const reference = await createOrderThroughMail(page, "e2e-ord-013");

    const list = new OrderListPage(page);
    await list.goto();
    const row = list.row(reference);
    await row.open();

    // Break the save on the way out. The client has to notice - this is the shape of bug that
    // was most common in this codebase: the request fails, nothing says so, and the data is
    // quietly not there.
    await page.route("**/OrderItemTypeSurface/SetOrderItemType**", (route) =>
      route.fulfill({ status: 500, contentType: "application/json", body: '{"Success":false,"Message":"Framkallat fel i e2e"}' })
    );
    guard.allow(/500 GET \/OrderItemTypeSurface\/SetOrderItemType/);
    guard.allow(/Framkallat fel i e2e|Error: /);

    const complaints: string[] = [];
    page.on("dialog", (d) => complaints.push(d.message()));

    await row.details.getByTestId("order-type-toggle").click();
    await row.details.getByTestId("order-type-menu").getByTestId("order-type-option").filter({ hasText: /^\s*Bok\s*$/ }).click();

    await expect(async () => {
      expect(complaints.join("\n"), "a failed save has to reach the user").not.toBe("");
    }).toPass({ timeout: 30_000 });

    // And the busy overlay must not be left spinning (regression f1bd8bf is the same shape).
    await expect(page.locator("#lockscreen")).toBeHidden();
  });
});

test.describe("orders, copies", () => {
  test("ORD-011: a copy becomes a second order with the same details", async ({ ownApp, guard }) => {
    guard.allow(/Successfully sent new order/);
    const { page } = await ownApp();
    const reference = await createOrderThroughMail(page, "e2e-ord-011", { name: "Kopia Kopiasson" });

    const list = new OrderListPage(page);
    await list.goto();
    const row = list.row(reference);
    await row.open();
    await row.setType("Bok");

    const copied = page.waitForResponse((r) => r.url().includes("/OrderItemDuplicateSurface/MakeDuplicate"));
    await row.details.getByTestId("order-misc-toggle").click();
    await row.details.getByTestId("order-make-duplicate").click();
    expect((await copied).ok()).toBeTruthy();

    // Two rows now carry the same reference, each with its own node id.
    await list.goto();
    const rows = page.getByTestId("order-row").filter({ hasText: reference });
    await expect(rows).toHaveCount(2);
    const ids = await rows.evaluateAll((els) => els.map((e) => e.id));
    expect(new Set(ids).size, "the copy must be its own order").toBe(2);
  });
});

test.describe("orders, every status", () => {
  // The seed data is the same on every app, so a private app has all 17 orders to itself and can
  // open them without taking locks from anyone else.
  const seedReferences = [
    "ref-ny-001", "ref-atgarda-002", "ref-bestalld-003", "ref-vantar-004", "ref-levererad-005",
    "ref-annullerad-006", "ref-overford-007", "ref-inkopt-008", "ref-mottagen-009", "ref-atersand-010",
    "ref-utlanad-011", "ref-kravd-012", "ref-transport-013", "ref-infodisk-014",
    "ref-forlorad-fraga-015", "ref-forlorad-016", "ref-folio-017",
  ];

  test("ORD-014: the order panel opens without a JavaScript error for every status (regression 279e039)", async ({ ownApp }) => {
    const { page } = await ownApp();
    const list = new OrderListPage(page);

    for (const reference of seedReferences) {
      const row = await list.find(reference);
      await row.open();
      // The guard is what actually fails this test: it collects uncaught errors, console.error
      // and failed requests from every page, so a panel that throws on render is caught here
      // even though the assertion below only checks that something rendered.
      await expect(row.details, reference).toBeVisible();
    }
  });
});
