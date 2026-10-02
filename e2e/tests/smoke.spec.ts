import { expect, test } from "../fixtures";
import { LoginPage } from "../pages/LoginPage";

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

  test("SMOKE-006: every page in the menu renders", async ({ page }) => {
    // The menu's "Statistik" link points at Chillin:StatisticsUrl, which is empty in isolated
    // mode, so the pages are reached by their own routes rather than by clicking the menu.
    for (const path of ["/", "/bestaellningar/", "/bestaellningar/instaellningar/", "/ChalmersILLStatisticsPage"]) {
      const response = await page.goto(path, { waitUntil: "load" });
      expect(response?.status(), `GET ${path}`).toBe(200);
      await expect(page.locator("nav.navbar")).toBeVisible();
    }
    // The guard fails the test on any 404 behind these pages - that is how a case-sensitive
    // static-file path would show up on Linux (regression c1062c2).
  });
});

test.describe("smoke, anonymous", () => {
  test.use({ role: "anonymous" });

  test("SMOKE-003: a wrong password says so on the login page (regression 180134d)", async ({ page }) => {
    const login = new LoginPage(page);
    await login.goto();
    await login.submit("superadmin", "fel-losenord");

    // The bug was that this path answered in a way the browser reported as a network error
    // instead of rendering the page with its message.
    await expect(login.error).toContainText("Felaktig inloggning");
    await expect(page.getByTestId("login-submit")).toBeVisible();
  });

  test("SMOKE-004: a protected page sends an anonymous visitor to the login page", async ({ page }) => {
    await page.goto("/bestaellningar/", { waitUntil: "load" });
    await expect(page).toHaveURL(/ChalmersILLLoginPage/);
    await expect(page.getByTestId("login-submit")).toBeVisible();
  });
});

test.describe("smoke, logging out", () => {
  test("SMOKE-005: logging out ends the session and the order list is closed again", async ({ page }) => {
    await page.goto("/bestaellningar/", { waitUntil: "load" });
    await page.goto("/ChalmersILLLogoutPage", { waitUntil: "load" });

    await page.goto("/bestaellningar/", { waitUntil: "load" });
    await expect(page).toHaveURL(/ChalmersILLLoginPage/);
  });
});
