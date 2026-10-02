import { expect, test } from "../fixtures";
import { OrderListPage } from "../pages/OrderListPage";
import { createOrderThroughMail } from "../pages/StartPage";

// Scenario IDs refer to e2e/scenarios/SCENARIOS.md.
//
// What these prove is narrower than it looks: Notifier sends over Clients.All, which only reaches
// clients connected to the *same* instance. There is no backplane, so on more than one instance
// these updates would silently not arrive - see "Fastställda designbeslut" in
// TODO-remove-dotnet-framework.md. One instance is a hard precondition, not a scaling detail.

test.describe("realtime", () => {
  test("RT-001: a change in one window shows up in another without reloading", async ({ ownApp, guard }) => {
    guard.allow(/Successfully sent new order/);
    const { page, newSession } = await ownApp();
    const reference = await createOrderThroughMail(page, "e2e-rt-001");

    const editor = new OrderListPage(page);
    await editor.goto();

    // A second user is just watching the list - no reload after this point.
    const watcherPage = await newSession("admin");
    const watcher = new OrderListPage(watcherPage);
    await watcher.goto();
    await expect(watcher.row(reference).type).toHaveText("");

    const row = editor.row(reference);
    await row.open();
    await row.setType("Artikel");

    // The watcher's row has to change on its own.
    await expect(watcher.row(reference).type, "the watching window never got the update").toHaveText("Artikel", {
      timeout: 30_000,
    });
  });

  test("RT-002: the pending counter in the menu follows a new order arriving", async ({ ownApp, guard }) => {
    guard.allow(/Successfully sent new order/);
    const { page, newSession } = await ownApp();

    // This window just watches the order list, and must not be navigated after this point - the
    // whole point is that the counter changes without a reload.
    const list = new OrderListPage(page);
    await list.goto();
    const counter = page.locator("#pending-order-counter");
    const before = Number((await counter.innerText()).trim());

    // Someone else, in their own session, sends in an order and polls the mailbox.
    const other = await newSession("admin");
    const reference = await createOrderThroughMail(other, "e2e-rt-002");

    await expect(counter, "the watching window's counter never moved").toHaveText(String(before + 1), {
      timeout: 30_000,
    });

    // And the new order is really there, for the next reader of this test.
    await list.goto();
    await expect(list.row(reference).status).toHaveText("Ny");
  });
});

test.describe("realtime, across a restart", () => {
  test("RT-003: updates come back on their own after the server restarts", async ({ ownApp, guard }) => {
    guard.allow(/Successfully sent new order/);
    // The connection drops when the server goes away; SignalR says so on the console before
    // withAutomaticReconnect takes over.
    guard.allow(/notificationHub|SignalR|WebSocket|Server timeout|connection was lost|ERR_CONNECTION/i);

    const { app, page, newSession } = await ownApp();
    const reference = await createOrderThroughMail(page, "e2e-rt-003");

    const editorPage = page;
    const editor = new OrderListPage(editorPage);
    await editor.goto();

    const watcherPage = await newSession("admin");
    const watcher = new OrderListPage(watcherPage);
    await watcher.goto();

    // The windows stay open while the server goes away and comes back.
    await app.restart();

    // withAutomaticReconnect has to re-establish the hub connection by itself.
    await expect(async () => {
      const state = await watcherPage.evaluate(() => (window as any).notificationHubConnection?.state ?? "missing");
      expect(state, "the hub never reconnected").toBe("Connected");
    }).toPass({ timeout: 60_000 });

    // And a change made after the restart must still arrive without a reload.
    await editor.reload();
    const row = editor.row(reference);
    await row.open();
    await row.setType("Bok");

    await expect(watcher.row(reference).type, "no update arrived after the reconnect").toHaveText("Bok", {
      timeout: 30_000,
    });
  });
});
