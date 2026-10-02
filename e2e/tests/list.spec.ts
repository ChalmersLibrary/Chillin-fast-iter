import { expect, test } from "../fixtures";
import { OrderListPage } from "../pages/OrderListPage";

// Scenario IDs refer to e2e/scenarios/SCENARIOS.md.
//
// These are read-only, so they share the worker's app. They deliberately never assert an absolute
// number of orders: other scenarios in the same worker change their own orders, and a count would
// make this file's result depend on what ran first (the way SMOKE-002 once did). Where a number
// matters, it is checked against what the page itself shows.
//
// The orders leaned on here - ref-levererad-005, ref-forlorad-fraga-015 - are outside the default
// list and owned by no other scenario.

test.describe("list", () => {
  test("LIST-001: free-text search on a patron name finds that order and says what was searched for", async ({ page }) => {
    const list = new OrderListPage(page);
    await list.goto("Astrid Nordqvist");

    await expect(list.searchHeading).toContainText("Astrid Nordqvist");
    await expect(list.row("ref-levererad-005").root).toBeVisible();
    for (const reference of await list.visibleReferences()) {
      expect(reference).toContain("ref-levererad-005");
    }
  });

  test("LIST-002: a field search returns only orders with that status", async ({ page }) => {
    const list = new OrderListPage(page);
    await list.goto("status:Levererad");

    await expect(list.row("ref-levererad-005").root).toBeVisible();
    for (const status of await list.visibleRows.getByTestId("order-status").allInnerTexts()) {
      expect(status.trim()).toBe("Levererad");
    }
  });

  test("LIST-003: a status filter shows exactly the orders with that status, and its badge agrees", async ({ page }) => {
    const list = new OrderListPage(page);
    await list.goto();

    const ny = list.statusFilter(".chillin-status-01");
    await list.applyFilter(ny);

    const expected = await list.filterCount(ny);
    await expect(list.visibleRows).toHaveCount(expected);
    for (const status of await list.visibleRows.getByTestId("order-status").allInnerTexts()) {
      expect(status.trim()).toBe("Ny");
    }

    // "Alla" puts everything back.
    await list.applyFilter(list.statusFilter(""));
    await expect(list.visibleRows).toHaveCount(await list.filterCount(list.statusFilter("")));
  });

  test("LIST-004: a library filter shows exactly the orders for that library", async ({ page }) => {
    const list = new OrderListPage(page);
    await list.goto();

    const kuggen = list.libraryFilter(".Lindholmenbiblioteket");
    await list.applyFilter(kuggen);

    await expect(list.visibleRows).toHaveCount(await list.filterCount(kuggen));
    for (const row of await list.visibleRows.all()) {
      await expect(row.locator("[data-column='deliveryLibrary']")).toHaveText("Zl");
    }
  });

  test("LIST-005: status and library filters together give the intersection, not the union", async ({ page }) => {
    const list = new OrderListPage(page);
    await list.goto();

    await list.applyFilter(list.statusFilter(".chillin-status-09"));
    await list.applyFilter(list.libraryFilter(".Lindholmenbiblioteket"));

    for (const row of await list.visibleRows.all()) {
      await expect(row.getByTestId("order-status")).toHaveText("Mottagen");
      await expect(row.locator("[data-column='deliveryLibrary']")).toHaveText("Zl");
    }

    // The intersection must be smaller than, or equal to, either side on its own.
    const intersection = await list.visibleRows.count();
    await list.applyFilter(list.libraryFilter(""));
    expect(intersection).toBeLessThanOrEqual(await list.visibleRows.count());
  });

  test("LIST-006: sorting on type reorders the list, and sorting back on status restores it", async ({ page }) => {
    const list = new OrderListPage(page);
    await list.goto();

    // The page arrives sorted by status, server-side.
    const byStatus = await list.visibleReferences();
    await page.getByTestId("sort-on-type").click();
    await page.waitForTimeout(600);
    const byType = await list.visibleReferences();

    expect(byType.slice().sort()).toEqual(byStatus.slice().sort()); // same orders...
    expect(byType).not.toEqual(byStatus); // ...in a different order

    // Sorting back has to reproduce the server's own order exactly. It did not: the client's
    // status weights were a different table than the server's, so clicking "Status" rearranged
    // the list into an order nothing else in the app agreed with.
    await page.getByTestId("sort-on-status").click();
    await page.waitForTimeout(600);
    expect(await list.visibleReferences()).toEqual(byStatus);
  });

  test("LIST-008: the search dropdown's shortcut finds the orders with that status", async ({ page }) => {
    const list = new OrderListPage(page);
    await list.goto();

    await page.locator("form[role='search'] .dropdown-toggle").click();
    await Promise.all([
      page.waitForNavigation({ waitUntil: "domcontentloaded" }),
      page.locator("form[role='search'] .dropdown-menu a").click(),
    ]);

    await expect(list.row("ref-forlorad-fraga-015").root).toBeVisible();
    for (const status of await list.visibleRows.getByTestId("order-status").allInnerTexts()) {
      expect(status.trim()).toBe("Förlorad?");
    }
  });

  test("LIST-009: a search with no hits says so instead of failing", async ({ page }) => {
    const list = new OrderListPage(page);
    await list.gotoExpectingNoHits("hittarsäkertingenting-" + Date.now());
    await expect(list.noOrders).toContainText("Hittade inga aktuella ordrar");
  });

  test("LIST-010: a value containing a colon is searchable when quoted", async ({ page }) => {
    const list = new OrderListPage(page);
    await list.goto('status:"05:Levererad"');

    await expect(list.row("ref-levererad-005").root).toBeVisible();
    for (const status of await list.visibleRows.getByTestId("order-status").allInnerTexts()) {
      expect(status.trim()).toBe("Levererad");
    }
  });
});
