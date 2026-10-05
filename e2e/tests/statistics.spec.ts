import { expect, test } from "../fixtures";

// Scenario IDs refer to e2e/scenarios/SCENARIOS.md.
//
// Read-only: the statistics page only queries the searcher, so these share the worker's app.

const STATS = "/ChalmersILLStatisticsPage";

async function addVariable(page: import("@playwright/test").Page, name: string) {
  await page.getByTestId("stat-var-name").fill(name);
  await page.getByTestId("stat-save-variable").first().click();
  await expect(page.locator("#stat-variable-list")).toContainText(name);
}

test.describe("statistics", () => {
  test("STAT-001: a count variable produces a table with numbers in it", async ({ page }) => {
    await page.goto(STATS, { waitUntil: "load" });
    await expect(page.getByRole("heading", { name: "Statistik" })).toBeVisible();

    await addVariable(page, "E2E antal");

    const fetched = page.waitForResponse((r) => r.url().includes("/StatisticsSurface/GetData"));
    await page.getByTestId("stat-build-table").first().click();
    const response = await fetched;
    expect(response.ok()).toBeTruthy();
    expect((await response.json()).Success).toBeTruthy();

    await expect(page.getByTestId("stat-data-view")).toBeVisible();
    const table = page.locator("#data-table");
    await expect(table).toContainText("E2E antal");
    // At least one cell has to be a number - an empty table would otherwise pass.
    await expect(table.locator("td")).not.toHaveCount(0);
    const cells = await table.locator("td").allInnerTexts();
    expect(cells.some((c) => /^\d+$/.test(c.trim())), `cells were ${JSON.stringify(cells)}`).toBeTruthy();
  });

  test("STAT-002: the processing-time calculations can be chosen and come back", async ({ page }) => {
    await page.goto(STATS, { waitUntil: "load" });

    for (const calcType of ["Medelvärde av handläggningstid", "Medianvärde av handläggningstid"]) {
      await page.locator("#dropdownMenu1").first().click();
      await page.getByRole("menuitem", { name: calcType }).first().click();
      await expect(page.locator("#selected-calc-type").first()).toHaveText(calcType);

      await addVariable(page, "E2E " + calcType);
    }

    const fetched = page.waitForResponse((r) => r.url().includes("/StatisticsSurface/GetData"));
    await page.getByTestId("stat-build-table").first().click();
    expect((await (await fetched).json()).Success).toBeTruthy();
    await expect(page.locator("#data-table")).toContainText("Medelvärde");
    await expect(page.locator("#data-table")).toContainText("Medianvärde");
  });

  test("STAT-004: the table can be downloaded as CSV with the same numbers", async ({ page }) => {
    await page.goto(STATS, { waitUntil: "load" });
    await addVariable(page, "E2E csv");

    const fetched = page.waitForResponse((r) => r.url().includes("/StatisticsSurface/GetData"));
    await page.getByTestId("stat-build-table").first().click();
    await fetched;
    await expect(page.getByTestId("stat-data-view")).toBeVisible();

    const onScreen = (await page.locator("#data-table").allInnerTexts()).join(" ");

    const download = page.waitForEvent("download");
    await page.getByTestId("stat-export-csv").click();
    const file = await download;
    const stream = await file.createReadStream();
    const csv = (await new Promise<Buffer>((resolve, reject) => {
      const chunks: Buffer[] = [];
      stream.on("data", (c) => chunks.push(Buffer.from(c)));
      stream.on("end", () => resolve(Buffer.concat(chunks)));
      stream.on("error", reject);
    })).toString("utf8");

    expect(csv).toContain("E2E csv");
    // Every number on screen has to be in the file too.
    for (const n of onScreen.match(/\b\d+\b/g) ?? []) {
      expect(csv, `the CSV is missing ${n}`).toContain(n);
    }
  });
});

test.describe("statistics, filtered", () => {
  test("STAT-003: a filter on a variable narrows the result", async ({ page }) => {
    await page.goto(STATS, { waitUntil: "load" });

    // The filter panels are built from whatever values the search index actually holds, so wait
    // for them rather than assuming which ones exist. The heading has to match exactly: the
    // panels are "P-Typ", "Status", "Typ", "Ägandebibliotek", "Annulleringsorsak" and "Inköpt
    // Material", and a substring match on "Typ" picks "P-Typ", whose only value is "0".
    const typeFilter = page
      .locator("#stat-variable-options .col-md-4")
      .filter({ has: page.locator(".panel-heading", { hasText: /^\s*Typ\s*$/ }) });
    await expect(typeFilter).toBeVisible();

    // One variable counting everything...
    await addVariable(page, "E2E alla");

    // ...and one counting only books.
    await typeFilter.locator(".panel-body li button", { hasText: "Bok" }).first().click();
    await addVariable(page, "E2E bara bok");

    const fetched = page.waitForResponse((r) => r.url().includes("/StatisticsSurface/GetData"));
    await page.getByTestId("stat-build-table").first().click();
    const data = await (await fetched).json();
    expect(data.Success).toBeTruthy();

    const all = data.StatisticsData.find((v: any) => v.Name === "E2E alla");
    const books = data.StatisticsData.find((v: any) => v.Name === "E2E bara bok");
    expect(all, "the unfiltered variable is missing").toBeTruthy();
    expect(books, "the filtered variable is missing").toBeTruthy();

    const sum = (v: any) => (v.Values as number[]).reduce((a, b) => a + b, 0);
    expect(sum(books), "a filter must not widen the result").toBeLessThan(sum(all));
    expect(sum(books), "the filter removed everything, so it proves nothing").toBeGreaterThan(0);
  });
});
