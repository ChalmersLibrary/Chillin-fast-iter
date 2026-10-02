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
