import * as fs from "fs";
import * as path from "path";
import { expect, test } from "../fixtures";
import { OrderListPage } from "../pages/OrderListPage";
import { StartPage, createOrderThroughMail } from "../pages/StartPage";

// Scenario IDs refer to e2e/scenarios/SCENARIOS.md.
//
// These run on their own app (`ownApp`): polling the mailbox also runs the daily housekeeping,
// which rewrites orders whose follow-up date has passed - on the shared app that would reach into
// other scenarios' orders.

test.describe("inbox", () => {
  test("INBOX-001: a new order mail becomes an order with status Ny when the mail is polled", async ({ ownApp, guard }) => {
    // The form confirms with an alert; the guard flags unexpected dialogs, so declare this one.
    guard.allow(/Successfully sent new order/);

    const { page } = await ownApp();
    const start = new StartPage(page);
    await start.goto();

    const reference = "e2e-inbox-001-" + Date.now();
    await start.submitNewOrder({
      message: reference,
      name: "Testa Testsson",
      email: "testa.testsson@example.invalid",
      cardNo: "9876543210",
      library: "Zl",
    });
    await start.pollMail();

    const list = new OrderListPage(page);
    await list.goto();
    const row = list.row(reference);
    await expect(row.status).toHaveText("Ny");
    await expect(row.root).toContainText("Testa Testsson");
  });

  test("INBOX-002: the order carries the patron's details and says it came from mail", async ({ ownApp, guard }) => {
    guard.allow(/Successfully sent new order/);
    const { page } = await ownApp();
    const reference = await createOrderThroughMail(page, "e2e-inbox-002", {
      name: "Inkommen Inkommensson",
      email: "inkommen@example.invalid",
      cardNo: "5544332211",
    });

    const list = new OrderListPage(page);
    await list.goto();
    const row = list.row(reference);
    await row.open();

    const details = page.locator(".editmode");
    await expect(details).toContainText("Inkommen Inkommensson");
    await expect(details).toContainText("inkommen@example.invalid");
    await expect(details).toContainText("5544332211");
    // The history has to say where the order came from, not just that it exists.
    await expect(details).toContainText("maildata");
  });

  test("INBOX-003: a mail that is not an order does not become a half-made one", async ({ ownApp, guard }) => {
    guard.allow(/Successfully sent new order/);
    const { app, page } = await ownApp();

    const list = new OrderListPage(page);
    await list.goto();
    const before = await list.visibleRows.count();

    // No form can produce this - it is an ordinary mail that happens to land in the mailbox, so
    // it is written straight into the inbox the way a mail server would deliver it.
    const inbox = path.join(app.dataPath, "mail", "inbox");
    fs.mkdirSync(inbox, { recursive: true });
    const id = "e2e-skrap-" + Date.now();
    fs.writeFileSync(path.join(inbox, id + ".html"), "<p>Hej, är ni öppna på lördag?</p>");
    fs.writeFileSync(
      path.join(inbox, id + ".meta.json"),
      JSON.stringify({ From: "nyfiken@example.invalid", Sender: "Nyfiken", Subject: "En fråga, inte en beställning" })
    );

    const start = new StartPage(page);
    await start.goto();
    await start.pollMail(); // must not throw: an unparseable mail cannot break the poll

    await list.goto();
    expect(await list.visibleRows.count(), "a stray mail should not become an order").toBe(before);
    await expect(list.noOrders).toHaveCount(0); // the list still works
  });
});
