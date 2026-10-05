import { expect, test } from "../fixtures";
import { LoginPage } from "../pages/LoginPage";
import { PASSWORD } from "../support/members";

// Scenario IDs refer to e2e/scenarios/SCENARIOS.md.
//
// Everything here writes to the app's own data - members.json, templates, chillin texts - so each
// scenario runs on its own app. On the shared one, changing a password or deleting an account
// would pull the rug out from under every other scenario in the worker.

const SETTINGS = "/bestaellningar/instaellningar/";

test.describe("settings", () => {
  test("SET-001: a changed password is the one that works at the next login", async ({ ownApp }) => {
    const { page } = await ownApp();
    await page.goto(SETTINGS, { waitUntil: "load" });

    const newPassword = "nytt-losenord-åäö";
    await page.locator("#CurrentPassword").fill(PASSWORD);
    await page.locator("#NewPassword").fill(newPassword);
    await Promise.all([
      page.waitForNavigation({ waitUntil: "load" }),
      page.getByRole("button", { name: "Ändra lösenord" }).click(),
    ]);
    await expect(page.locator(".alert-success")).toContainText("Ditt lösenord är nu ändrat");

    // The real proof is a fresh login, not the message.
    await page.goto("/ChalmersILLLogoutPage", { waitUntil: "load" });
    const login = new LoginPage(page);
    await login.goto();
    await login.submit("superadmin", PASSWORD);
    await expect(login.error, "the old password must stop working").toContainText("Felaktig inloggning");

    await login.submit("superadmin", newPassword);
    await login.expectLoggedIn();
  });

  test("SET-002: the wrong current password is refused", async ({ ownApp }) => {
    const { page } = await ownApp();
    await page.goto(SETTINGS, { waitUntil: "load" });

    await page.locator("#CurrentPassword").fill("det-här-är-fel");
    await page.locator("#NewPassword").fill("spelar-ingen-roll");
    await Promise.all([
      page.waitForNavigation({ waitUntil: "load" }),
      page.getByRole("button", { name: "Ändra lösenord" }).click(),
    ]);
    await expect(page.locator(".alert-danger")).toContainText("Fel lösenord");

    // And the old password still works.
    await page.goto("/ChalmersILLLogoutPage", { waitUntil: "load" });
    const login = new LoginPage(page);
    await login.goto();
    await login.submit("superadmin", PASSWORD);
    await login.expectLoggedIn();
  });

  test("SET-004: the templates tab works when there are templates, and lists them", async ({ ownApp, guard }) => {
    guard.allow(/Lyckades med att skapa mall|Skapade ny mall/);
    const { page } = await ownApp();
    await page.goto(SETTINGS, { waitUntil: "load" });
    await page.locator("#template-settings-link").click();
    await expect(page.getByTestId("create-template")).toBeVisible();

    // The 13 system templates are seeded, so the list must not be empty (edbb71f was the crash
    // when it was).
    await expect(page.locator("#template-list .list-group-item").first()).toBeVisible();

    const created = page.waitForResponse((r) => r.url().includes("/TemplatesSurface/CreateTemplate"));
    await page.getByTestId("new-template-description").fill("E2E-mall");
    await page.getByTestId("create-template").click();
    expect((await created).ok()).toBeTruthy();

    await page.reload({ waitUntil: "load" });
    await page.locator("#template-settings-link").click();
    await expect(page.locator("#template-list")).toContainText("E2E-mall");
  });

  test("SET-005: a changed chillin text is kept", async ({ ownApp, guard }) => {
    guard.allow(/Sparade|Lyckades/);
    const { page } = await ownApp();
    await page.goto(SETTINGS, { waitUntil: "load" });
    await page.getByRole("link", { name: "Text", exact: true }).click();
    await expect(page.getByTestId("chillin-text-checkin")).toBeVisible();

    const text = "Incheckad av e2e åäö";
    const saved = page.waitForResponse((r) => r.url().includes("ChillinText"));
    await page.getByTestId("chillin-text-checkin").fill(text);
    await page.getByTestId("chillin-text-save").click();
    expect((await saved).ok()).toBeTruthy();

    await page.reload({ waitUntil: "load" });
    await page.getByRole("link", { name: "Text", exact: true }).click();
    await expect(page.getByTestId("chillin-text-checkin")).toHaveValue(text);
  });
});

test.describe("settings, accounts", () => {
  test("SET-007: a new account can log in", async ({ ownApp, guard }) => {
    guard.allow(/Lyckades|Skapade/);
    const { page } = await ownApp();
    await page.goto(SETTINGS, { waitUntil: "load" });
    await page.locator("#member-settings-link").click();

    const created = page.waitForResponse((r) => r.url().includes("/MemberAdminSurface/CreateMember"));
    await page.getByTestId("new-member-login").fill("e2e-nykomling");
    await page.getByTestId("new-member-password").fill("e2e-password");
    await page.getByTestId("new-member-roles").fill("Desk, Administrator");
    await page.getByTestId("create-member").click();
    expect((await created).ok()).toBeTruthy();

    await page.goto("/ChalmersILLLogoutPage", { waitUntil: "load" });
    const login = new LoginPage(page);
    await login.goto();
    await login.submit("e2e-nykomling", "e2e-password");
    await login.expectLoggedIn();
  });

  test("SET-009: a deleted account can no longer log in", async ({ ownApp, guard }) => {
    guard.allow(/Lyckades|Skapade|Tog bort/);
    const { page } = await ownApp();
    await page.goto(SETTINGS, { waitUntil: "load" });
    await page.locator("#member-settings-link").click();

    // "admin" exists in every app the harness starts (support/members.ts).
    const row = page.locator('#member-admin-table tr[data-login="admin"]');
    await expect(row).toBeVisible();
    // Deleting asks for confirmation, and the guard answers no to every dialog unless told.
    guard.accept(/Ta bort kontot/);
    const deleted = page.waitForResponse((r) => r.url().includes("/MemberAdminSurface/DeleteMember"));
    await row.getByTestId("delete-member").click();
    expect((await deleted).ok()).toBeTruthy();

    await page.goto("/ChalmersILLLogoutPage", { waitUntil: "load" });
    const login = new LoginPage(page);
    await login.goto();
    await login.submit("admin", PASSWORD);
    await expect(login.error, "a deleted account must not get in").toContainText("Felaktig inloggning");
  });
});

test.describe("settings, accounts in use", () => {
  test("SET-008: changed roles take effect at the account's next login", async ({ ownApp, guard }) => {
    guard.allow(/Lyckades|Skapade|Sparade/);
    const { page } = await ownApp();
    await page.goto(SETTINGS, { waitUntil: "load" });
    await page.locator("#member-settings-link").click();

    // A read-only account to begin with.
    const created = page.waitForResponse((r) => r.url().includes("/MemberAdminSurface/CreateMember"));
    await page.getByTestId("new-member-login").fill("e2e-befordrad");
    await page.getByTestId("new-member-password").fill("e2e-password");
    await page.getByTestId("new-member-roles").fill("Viewer");
    await page.getByTestId("create-member").click();
    expect((await created).ok()).toBeTruthy();

    // As a Viewer it may not change an order - asked straight out, past the hidden buttons.
    const viewer = await page.context().browser()!.newContext({ baseURL: page.url().split("/bestaellningar")[0] });
    const viewerPage = await viewer.newPage();
    const viewerLogin = new LoginPage(viewerPage);
    await viewerLogin.goto();
    await viewerLogin.submit("e2e-befordrad", "e2e-password");
    const asViewer = await viewerPage.request.get(
      "/OrderItemStatusSurface/SetOrderItemStatus?orderNodeId=1&statusId=1",
      { maxRedirects: 0 }
    );
    expect(asViewer.status(), "a Viewer must not be able to change a status").toBe(302);
    await viewer.close();

    // Promote the account.
    await page.goto(SETTINGS, { waitUntil: "load" });
    await page.locator("#member-settings-link").click();
    const row = page.locator('#member-admin-table tr[data-login="e2e-befordrad"]');
    const rolesSaved = page.waitForResponse((r) => r.url().includes("/MemberAdminSurface/SetMemberRoles"));
    await row.locator(".member-roles-input").fill("Administrator");
    await row.getByTestId("save-member-roles").click();
    expect((await rolesSaved).ok()).toBeTruthy();

    // The same account, logging in afresh, now gets through.
    const admin = await page.context().browser()!.newContext({ baseURL: page.url().split("/bestaellningar")[0] });
    const adminPage = await admin.newPage();
    const adminLogin = new LoginPage(adminPage);
    await adminLogin.goto();
    await adminLogin.submit("e2e-befordrad", "e2e-password");
    await adminLogin.expectLoggedIn();
    const asAdmin = await adminPage.request.get("/OrderItemSurface/GetOrderItem?nodeId=1", { maxRedirects: 0 });
    expect(asAdmin.status(), "the promoted account should be let in").toBe(200);
    await admin.close();
  });

  test("SET-010: an error in account admin releases the busy overlay (regression f1bd8bf)", async ({ ownApp, guard }) => {
    guard.allow(/Lyckades|Skapade/);
    const { page } = await ownApp();
    await page.goto(SETTINGS, { waitUntil: "load" });
    await page.locator("#member-settings-link").click();

    // Creating an account that already exists is refused by the server. The bug was that the
    // overlay put up before the request was never taken down again, leaving the page dead.
    const complaints: string[] = [];
    page.on("dialog", (d) => complaints.push(d.message()));
    guard.allow(/finns redan|existerar|Error/i);

    const refused = page.waitForResponse((r) => r.url().includes("/MemberAdminSurface/CreateMember"));
    await page.getByTestId("new-member-login").fill("superadmin"); // already there
    await page.getByTestId("new-member-password").fill("e2e-password");
    await page.getByTestId("create-member").click();
    const response = await refused;
    expect(response.ok()).toBeTruthy();
    expect((await response.json()).Success, "creating a duplicate account should be refused").toBeFalsy();

    // The user is told...
    await expect(async () => {
      expect(complaints.join("\n"), "nothing told the user it failed").not.toBe("");
    }).toPass({ timeout: 15_000 });

    // ...and the page is usable again.
    await expect(page.locator("#lockscreen")).toBeHidden();
    await expect(page.getByTestId("create-member")).toBeEnabled();
  });
});
