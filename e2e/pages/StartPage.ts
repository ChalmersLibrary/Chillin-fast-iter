import { expect, Page } from "@playwright/test";

/**
 * The start page (/), which carries the manual mail tools when
 * Chillin:ShowManualMailFetchingTools is on (the e2e harness turns it on; see support/app.ts).
 *
 * This is how an order comes into being for real: the form sends a mail to Chillin's own mailbox,
 * and the poll button (the chilli icon at the bottom left, normally the cron server's job) reads
 * the inbox and turns the mail into an order. Tests that need an order of their own go through
 * the same two steps rather than fabricating one behind the app's back.
 */
export class StartPage {
  constructor(readonly page: Page) {}

  async goto() {
    await this.page.goto("/", { waitUntil: "domcontentloaded" });
    await expect(this.page.getByTestId("new-order-submit")).toBeVisible();
  }

  /** Fills in and submits the "NY BESTÄLLNING!" form. The app confirms with an alert. */
  async submitNewOrder(order: { message: string; name: string; email: string; cardNo: string; library?: "Z" | "Zl" | "Za" }) {
    await this.page.getByTestId("new-order-message").fill(order.message);
    await this.page.getByTestId("new-order-name").fill(order.name);
    await this.page.getByTestId("new-order-email").fill(order.email);
    await this.page.getByTestId("new-order-cardno").fill(order.cardNo);
    if (order.library) await this.page.getByTestId("new-order-library").selectOption(order.library);

    const sent = this.page.waitForResponse((r) => r.url().includes("/OrderItemMailSurface/SendMailForNewOrder"));
    await this.page.getByTestId("new-order-submit").click();
    expect((await sent).ok()).toBeTruthy();
  }

  /** Runs the mail poll, the way the cron server does. Returns once the server has answered. */
  async pollMail() {
    const polled = this.page.waitForResponse((r) => r.url().includes("/SystemSurface/Update"));
    await this.page.getByTestId("poll-mail").click();
    expect((await polled).ok()).toBeTruthy();
  }
}

/**
 * Creates an order for this test to own, through the whole real path, and returns its reference
 * (which is also its text in the order list - the mail body becomes both OriginalOrder and
 * Reference, see FileOrderItemManager.CreateOrderItemInDbFromMailQueueModel).
 *
 * Use it with a page from the `ownApp` fixture: polling runs the daily housekeeping too, which
 * would reach into other scenarios' orders on the shared app.
 *
 * The caller must declare the form's confirmation dialog: `guard.allow(/Successfully sent new order/)`.
 */
export async function createOrderThroughMail(
  page: Page,
  label: string,
  patron: { name?: string; email?: string; cardNo?: string; library?: "Z" | "Zl" | "Za" } = {}
): Promise<string> {
  const start = new StartPage(page);
  await start.goto();

  const reference = `${label}-${Date.now()}`;
  await start.submitNewOrder({
    message: reference,
    name: patron.name ?? "Testa Testsson",
    email: patron.email ?? "testa.testsson@example.invalid",
    cardNo: patron.cardNo ?? "9876543210",
    library: patron.library,
  });
  await start.pollMail();

  return reference;
}
