import { expect, test } from "../fixtures";
import { OrderListPage } from "../pages/OrderListPage";

// Scenario IDs refer to e2e/scenarios/SCENARIOS.md. This file owns ref-mottagen-009.

test.describe("locks", () => {
  // The second user is "admin", not "desk": ChalmersILL.cshtml hides every .btn inside .editmode
  // for accounts without the Administrator role, so a desk-only account can't see "Överta låset"
  // (or any other action button) at all. That is pre-existing, deliberate behaviour - see ROLE-004.
  test("LOCK-001: an order opened by one user is shown as locked to another", async ({ page, newSession }) => {
    const editor = new OrderListPage(page);
    await editor.goto();
    await editor.row("ref-mottagen-009").open();
    await expect(editor.row("ref-mottagen-009").details.getByTestId("order-type-toggle")).toBeVisible();

    const other = new OrderListPage(await newSession("admin"));
    await other.goto();
    const row = other.row("ref-mottagen-009");
    await row.open();

    await expect(row.details.getByTestId("order-locked-notice")).toContainText("Låst av");
    await expect(row.details.getByTestId("order-take-over-lock")).toBeVisible();
    await expect(row.details.getByTestId("order-type-toggle")).toHaveCount(0);
  });
});
