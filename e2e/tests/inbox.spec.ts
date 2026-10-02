import { expect, test } from "../fixtures";
import { OrderListPage } from "../pages/OrderListPage";
import { StartPage } from "../pages/StartPage";

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
});
