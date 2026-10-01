import { expect, test } from "../fixtures";

// Scenario IDs refer to e2e/scenarios/SCENARIOS.md. This file owns ref-infodisk-014 - it is the
// one order in the default list that no other scenario changes. ref-ny-001, ref-atgarda-002 and
// ref-mottagen-009 belong to ORD-001, ORD-002 and LOCK-001, and asserting on them here would make
// smoke pass or fail depending on whether those scenarios had run yet.

test.describe("smoke", () => {
  test("SMOKE-001: superadmin lands on the order list after login", async ({ page }) => {
    await page.goto("/bestaellningar");
    await expect(page).toHaveURL(/bestaellningar/);
    await expect(page.locator("body")).toContainText("ref-");
  });
});

test.describe("smoke, second test in the same worker (warm app)", () => {
  test("SMOKE-002: order list shows the heading and all four default-status orders", async ({ page }) => {
    await page.goto("/bestaellningar");
    await expect(page.getByRole("heading", { name: /Beställningar/ })).toBeVisible();
    // The seed has 17 orders; only these four statuses show in the unfiltered list.
    await expect(page.getByTestId("order-row")).toHaveCount(4);
    await expect(page.locator("body")).toContainText("ref-infodisk-014");
  });
});
