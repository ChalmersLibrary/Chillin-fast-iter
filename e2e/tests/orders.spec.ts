import { expect, test } from "../fixtures";
import { OrderListPage } from "../pages/OrderListPage";

// Scenario IDs refer to e2e/scenarios/SCENARIOS.md. Each scenario below owns the seed order(s)
// named in it, because the app (and so its orders) is shared by every test in a worker - two
// scenarios mutating the same order would make each other's result depend on run order.

test.describe("orders", () => {
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
