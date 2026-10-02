import { expect, test } from "../fixtures";
import { OrderListPage } from "../pages/OrderListPage";

// Scenario IDs refer to e2e/scenarios/SCENARIOS.md.
//
// These read rather than write, so they share the worker's app. Where a scenario does try to
// change something, the point is that the server refuses - so nothing is left behind either.
//
// The role tiers (ViewerReadOnlyFilter): a real role is untouched; "Viewer" only reaches actions
// marked [AllowViewer]; no roles at all is blocked from everything, [AllowViewer] included.

test.describe("roles, as a viewer", () => {
  test.use({ role: "viewer" });

  test("ROLE-001: the order list can be read, but no button that changes an order is usable", async ({ page, guard }) => {
    guard.allow(/403 GET \/OrderItemSurface\/LockOrderItem/); // see ROLE-008

    const list = new OrderListPage(page);
    await list.goto();
    await expect(page.getByTestId("order-row").first()).toBeVisible();

    // The panel has to be open for there to be anything to hide - asserting on a closed list
    // would pass without proving anything.
    await list.row("ref-infodisk-014").open();

    // ChalmersILL.cshtml hides every .btn inside .editmode for accounts without the Administrator
    // role. They are still in the DOM, so count the *visible* ones.
    await expect(page.locator(".editmode .btn").filter({ visible: true })).toHaveCount(0);
  });

  test("ROLE-003: a viewer reaches the settings page but cannot change anything there", async ({ page }) => {
    // The page is deliberately [AllowViewer]: the password tab is self-service, so even a
    // read-only account has to be able to open it.
    await page.goto("/bestaellningar/instaellningar/", { waitUntil: "load" });
    await expect(page.getByRole("heading", { name: "Inställningar" })).toBeVisible();
    await expect(page.locator("#member-settings-link")).toHaveCount(0);

    // Reading a template is allowed; writing one is not.
    expect((await page.request.get("/TemplatesSurface/RenderEditTemplatesAction", { maxRedirects: 0 })).status()).toBe(200);
    // CreateTemplate is [HttpPost]; asking over GET is turned away by routing (405) before
    // authorization ever runs, which would prove nothing about the role.
    const write = await page.request.post("/TemplatesSurface/CreateTemplate", {
      form: { description: "e2e", acquisition: "false" },
      maxRedirects: 0,
    });
    expect(write.status(), "a viewer must not be able to create a template").toBe(302);
    expect(write.headers()["location"]).toContain("AccessDenied");
  });

  test("ROLE-005: the server refuses a change from a viewer, not just the interface", async ({ page }) => {
    const list = new OrderListPage(page);
    await list.goto();
    const nodeId = await list.row("ref-infodisk-014").nodeId();

    // Asked directly, bypassing the hidden buttons entirely. maxRedirects: 0 matters: cookie
    // authentication answers a Forbid with a 302 to the access-denied page, and following it
    // would report a cheerful 200 that looks exactly like the change having been allowed.
    const response = await page.request.get(
      `/OrderItemStatusSurface/SetOrderItemStatus?orderNodeId=${nodeId}&statusId=1`,
      { maxRedirects: 0 }
    );
    expect(response.status(), "a viewer must not be able to change a status").toBe(302);
    expect(response.headers()["location"]).toContain("AccessDenied");
  });

  test("ROLE-008: a viewer can open an order and read its details", async ({ page, guard }) => {
    // Opening an order tries to take its lock, and LockOrderItem is deliberately not
    // [AllowViewer] - a Viewer shouldn't hold an edit lock. The client has no failure branch for
    // it, so the 403 passes unnoticed. That gap is known and accepted (see the Viewer work in
    // c15db91/41102d7); it is declared here rather than hidden.
    guard.allow(/403 GET \/OrderItemSurface\/LockOrderItem/);

    const list = new OrderListPage(page);
    await list.goto();
    const row = list.row("ref-infodisk-014");
    await row.open();

    // RenderOrderItem is [AllowViewer], so the details themselves come through. The order's
    // fields sit in a sibling .order-details box, not in the action panel.
    await expect(page.locator(".editmode")).toContainText("Order ID");
    await expect(page.locator(".editmode")).toContainText("Hanna Öqvist");
  });
});

test.describe("roles, with no roles at all", () => {
  test.use({ role: "roleless" });

  // The access-denied page uses the main layout, so it loads chalmers.ill.js, whose document.ready
  // asks for GetLocksForCurrentMember - which is exactly what an account with no roles may not do.
  // Every load of the page therefore fires one 403 that nothing reacts to. Harmless, but it is the
  // page's own doing, and it is declared here rather than quietly filtered out.
  const deniedPageAsksForLocks = /403 GET \/OrderItemSurface\/GetLocksForCurrentMember/;

  test("ROLE-002: every page is closed to an account without roles", async ({ page, guard }) => {
    guard.allow(deniedPageAsksForLocks);
    for (const path of ["/bestaellningar/", "/bestaellningar/instaellningar/", "/ChalmersILLStatisticsPage"]) {
      await page.goto(path, { waitUntil: "load" });
      await expect(page, `GET ${path}`).toHaveURL(/AccessDenied/);
    }
  });

  test("ROLE-006: the access-denied page is a real page, not a redirect loop (regression 6add6f9)", async ({ page, guard }) => {
    guard.allow(deniedPageAsksForLocks);
    const response = await page.goto("/bestaellningar/", { waitUntil: "load" });
    expect(response?.status()).toBe(200);
    await expect(page.locator(".alert-danger")).toBeVisible();

    // The page itself has to be [AllowAnonymous], or an account with no roles cannot even reach
    // the page explaining that it has no access. Logging out has to work for the same reason.
    const loggedOut = await page.goto("/ChalmersILLLogoutPage", { waitUntil: "load" });
    expect(loggedOut?.status()).toBe(200);
    await expect(page).not.toHaveURL(/AccessDenied/);
  });
});

test.describe("roles, anonymous", () => {
  test.use({ role: "anonymous" });

  test("ROLE-007: the login page and static files are reachable without logging in (regression 6add6f9)", async ({ page }) => {
    const login = await page.goto("/ChalmersILLLoginPage", { waitUntil: "load" });
    expect(login?.status()).toBe(200);
    await expect(page.getByTestId("login-submit")).toBeVisible();

    for (const asset of ["/css/chalmers.ill.main.css", "/scripts/chalmers.ill.js"]) {
      const response = await page.request.get(asset);
      expect(response.status(), `GET ${asset}`).toBe(200);
    }
  });
});

test.describe("roles, administrator without SuperAdmin", () => {
  test.use({ role: "admin" });

  test("ROLE-009: the accounts tab is neither shown nor usable", async ({ page }) => {
    await page.goto("/bestaellningar/instaellningar/", { waitUntil: "load" });
    await expect(page.locator("#member-settings-link")).toHaveCount(0);

    const response = await page.request.get("/MemberAdminSurface/RenderMemberAdminAction", { maxRedirects: 0 });
    expect(response.status(), "account administration is SuperAdmin only").toBe(302);
    expect(response.headers()["location"]).toContain("AccessDenied");
  });
});

test.describe("roles, superadmin", () => {
  test("ROLE-010: the accounts tab is shown and lists the accounts", async ({ page }) => {
    await page.goto("/bestaellningar/instaellningar/", { waitUntil: "load" });
    await expect(page.locator("#member-settings-link")).toBeVisible();

    const response = await page.request.get("/MemberAdminSurface/RenderMemberAdminAction");
    expect(response.status()).toBe(200);
    expect(await response.text()).toContain("superadmin");
  });
});
