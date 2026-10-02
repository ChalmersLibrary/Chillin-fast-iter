import { expect, test } from "../fixtures";
import { OrderListPage } from "../pages/OrderListPage";
import { createOrderThroughMail } from "../pages/StartPage";

// Scenario IDs refer to e2e/scenarios/SCENARIOS.md.
//
// The endpoints the library system and the QR scanning call. Two of them are [AllowAnonymous]
// (SystemSurfaceController and PublicDataSurfaceController, released from the global [Authorize]
// in fas 0a); BookCirculationSurfaceController is not, and still needs a logged-in session.

test.describe("api, circulation", () => {
  test("API-001: scanning a loan sets the order to Utlånad", async ({ ownApp, guard }) => {
    guard.allow(/Successfully sent new order/);
    const { page } = await ownApp();
    const reference = await createOrderThroughMail(page, "e2e-api-001");

    const list = new OrderListPage(page);
    await list.goto();
    const row = list.row(reference);
    await row.open();
    await row.setType("Bok");
    const nodeId = await row.nodeId();
    await row.close();

    const response = await page.request.post(`/BookCirculationSurface/Loaned?nodeId=${nodeId}`);
    expect(response.ok()).toBeTruthy();
    expect((await response.json()).Success).toBeTruthy();

    const after = await list.find(reference);
    await expect(after.status).toHaveText("Utlånad");
  });

  test("API-002: scanning a return sets the order to Transport", async ({ ownApp, guard }) => {
    guard.allow(/Successfully sent new order/);
    const { page } = await ownApp();
    const reference = await createOrderThroughMail(page, "e2e-api-002");

    const list = new OrderListPage(page);
    await list.goto();
    const row = list.row(reference);
    await row.open();
    await row.setType("Bok");
    const nodeId = await row.nodeId();
    await row.close();

    const response = await page.request.post(`/BookCirculationSurface/Returned?nodeId=${nodeId}`);
    expect(response.ok()).toBeTruthy();
    expect((await response.json()).Success).toBeTruthy();

    const after = await list.find(reference);
    await expect(after.status).toHaveText("Transport");
  });

  test("API-003: an unknown order number is answered, not crashed on", async ({ page }) => {
    const response = await page.request.post("/BookCirculationSurface/Loaned?nodeId=999999");
    expect(response.status(), "the endpoint should answer rather than fall over").toBe(200);
    const body = await response.json();
    expect(body.Success).toBeFalsy();
    expect(body.Message).toBeTruthy();
  });

});

// A context made with browser.newContext() inherits this test's `use` options - including
// storageState. Without test.use({ role: "anonymous" }) these two would quietly run as superadmin
// and prove nothing. (That cost an hour: an "anonymous" probe that was really logged in made the
// order list and the circulation endpoint look wide open.)
test.describe("api, without logging in", () => {
  test.use({ role: "anonymous" });

  test("API-004: the circulation endpoints still require a login", async ({ browser, app }) => {
    // Unlike the other two machine-to-machine endpoints, BookCirculationSurfaceController carries
    // no [AllowAnonymous] - it is reached from a logged-in session, not straight from the
    // circulation system. Pinned here so a future change to that is a deliberate one.
    const anonymous = await browser.newContext({ baseURL: app.baseUrl });
    const response = await anonymous.request.post("/BookCirculationSurface/Loaned?nodeId=1", { maxRedirects: 0 });
    expect(response.status()).toBe(302);
    expect(response.headers()["location"]).toContain("LoginPage");
    await anonymous.close();
  });

  test("API-005: the Sierra patron endpoint answers without a login and allows cross-origin use", async ({ browser, app }) => {
    const anonymous = await browser.newContext({ baseURL: app.baseUrl });
    const response = await anonymous.request.get("/PublicDataSurface/GetChillinDataForSierraPatron?recordId=1&lang=sv");
    expect(response.status(), "this is a documented public API (ILL-status-api.md)").toBe(200);
    expect(response.headers()["access-control-allow-origin"]).toBe("*");
    await response.json(); // must be JSON, not an HTML login page
    await anonymous.close();
  });
});
