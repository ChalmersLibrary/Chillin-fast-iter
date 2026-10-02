import { expect, test } from "../fixtures";
import { OrderListPage } from "../pages/OrderListPage";
import { createOrderThroughMail } from "../pages/StartPage";

// Scenario IDs refer to e2e/scenarios/SCENARIOS.md. This file owns ref-mottagen-009 (LOCK-001) and
// ref-infodisk-014 is smoke's - everything else here makes its own order.
//
// The second user is "admin": ChalmersILL.cshtml hides every .btn inside .editmode for accounts
// without the Administrator role, so only an Administrator can see "Överta låset" at all.

test.describe("locks", () => {
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

  test("LOCK-002: reloading the page gives up the locks it holds (regression 556b30c)", async ({ ownApp, guard }) => {
    guard.allow(/Successfully sent new order/);
    const { page } = await ownApp();
    const reference = await createOrderThroughMail(page, "e2e-lock-002");

    const editor = new OrderListPage(page);
    await editor.goto();
    await editor.row(reference).open();

    // Opening took the lock.
    const held = await (await page.request.get("/OrderItemSurface/GetLocksForCurrentMember")).json();
    expect(held.List.length, "opening an order should take its lock").toBeGreaterThan(0);

    // The order list gives up every lock the current member holds when it loads.
    await editor.reload();

    const response = await page.request.get(`/OrderItemSurface/GetLocksForCurrentMember`);
    expect(response.ok()).toBeTruthy();
    const body = await response.json();
    expect(body.Success).toBeTruthy();
    expect(body.List, "no locks should be left after a reload").toEqual([]);
  });

  test("LOCK-003: taking over a lock moves it, and the previous editor is told", async ({ ownApp, guard }) => {
    guard.allow(/Successfully sent new order/);
    const { page, newSession } = await ownApp();
    const reference = await createOrderThroughMail(page, "e2e-lock-003");

    const editor = new OrderListPage(page);
    await editor.goto();
    await editor.row(reference).open();

    // The previous editor is told through a SignalR-driven alert. The guard dismisses dialogs and
    // would fail the test on an unexpected one, so declare it - and listen in to read the text.
    const toldTheEditor: string[] = [];
    page.on("dialog", (d) => toldTheEditor.push(d.message()));
    guard.allow(/tagit över låset/);
    // The one taking the lock gets a confirmation of its own.
    guard.allow(/Du har tagit över låset/);

    const other = new OrderListPage(await newSession("admin"));
    await other.goto();
    const otherRow = other.row(reference);
    await otherRow.open();
    await expect(otherRow.details.getByTestId("order-take-over-lock")).toBeVisible();
    await otherRow.details.getByTestId("order-take-over-lock").click();

    // The taker now holds the lock and can edit.
    await expect(otherRow.details.getByTestId("order-type-toggle")).toBeVisible();
    // ...and the one who lost it finds out.
    await expect(async () => {
      expect(toldTheEditor.join("\n")).toMatch(/har tagit över låset på den här ordern från dig/);
    }).toPass({ timeout: 30_000 });
  });

  test("LOCK-004: closing an order releases its lock for everyone else", async ({ ownApp, guard }) => {
    guard.allow(/Successfully sent new order/);
    const { page } = await ownApp();
    const reference = await createOrderThroughMail(page, "e2e-lock-004");

    const editor = new OrderListPage(page);
    await editor.goto();
    const row = editor.row(reference);
    await row.open();
    await row.close();

    const response = await page.request.get(`/OrderItemSurface/GetLocksForCurrentMember`);
    const body = await response.json();
    expect(body.List, "closing an order should give up its lock").toEqual([]);
  });

  test("LOCK-005: the server refuses a change from someone who does not hold the lock", async ({ ownApp, guard }) => {
    guard.allow(/Successfully sent new order/);
    const { page, newSession } = await ownApp();
    const reference = await createOrderThroughMail(page, "e2e-lock-005");

    const editor = new OrderListPage(page);
    await editor.goto();
    const row = editor.row(reference);
    await row.open();
    const nodeId = await row.nodeId();

    // The other session asks the server directly, bypassing the hidden buttons entirely: before
    // RequiresOrderLockFilter (c8d5ad8) the lock only decided which HTML was rendered, never what
    // could actually be saved.
    const other = await newSession("admin");
    const response = await other.request.get(
      `/OrderItemTypeSurface/SetOrderItemType?orderNodeId=${nodeId}&typeId=1`
    );
    expect(response.ok()).toBeTruthy(); // the refusal is a normal JSON answer, not an error status
    const body = await response.json();
    expect(body.Success).toBeFalsy();
    expect(body.Message).toContain("låst av");
  });
});
