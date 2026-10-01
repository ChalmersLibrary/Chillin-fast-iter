import { expect, Locator, Page } from "@playwright/test";

/**
 * The order list (/bestaellningar). Every selector the tests need lives here, so when the markup
 * changes there is one place to fix. Elements are found via data-testid (see README), except
 * where the visible text *is* the thing being tested.
 */
export class OrderListPage {
  constructor(readonly page: Page) {}

  async goto(query?: string) {
    await this.page.goto(query ? `/bestaellningar?query=${encodeURIComponent(query)}` : "/bestaellningar", { waitUntil: "domcontentloaded" });
    await expect(this.page.getByTestId("order-row").first()).toBeVisible();
  }

  async reload() {
    await this.page.reload({ waitUntil: "domcontentloaded" });
    await expect(this.page.getByTestId("order-row").first()).toBeVisible();
  }

  /** The row whose reference contains the given text. Seed orders have unique ones, e.g. "ref-ny-001". */
  row(reference: string): OrderRow {
    const root = this.page
      .getByTestId("order-row")
      .filter({ has: this.page.getByTestId("order-reference").filter({ hasText: reference }) });
    return new OrderRow(this.page, root);
  }
}

export class OrderRow {
  constructor(readonly page: Page, readonly root: Locator) {}

  get reference() { return this.root.getByTestId("order-reference"); }
  get type() { return this.root.getByTestId("order-type"); }
  get status() { return this.root.getByTestId("order-status"); }

  /** The panel that opens under the row when it is clicked. Only one order is open at a time. */
  get details() { return this.page.getByTestId("order-details"); }

  async open() {
    await this.reference.click();
    await expect(this.details).toBeVisible();
  }

  /** Changes the order type through the real dropdown and waits for the server to confirm it. */
  async setType(typeName: string) {
    const menu = this.details.getByTestId("order-type-menu");
    const saved = this.page.waitForResponse((r) => r.url().includes("/OrderItemTypeSurface/SetOrderItemType"));
    // Opening an order takes its lock, and the resulting SignalR notification makes the page
    // re-render the details - which closes a dropdown that was just opened. The retry covers
    // that (and slow machines) by starting over whenever the menu vanishes underneath the click.
    await expect(async () => {
      if (!(await menu.isVisible())) await this.details.getByTestId("order-type-toggle").click({ timeout: 10_000 });
      await menu.getByTestId("order-type-option").filter({ hasText: typeName }).click({ timeout: 5_000 });
    }).toPass({ timeout: 60_000 });
    expect((await saved).ok()).toBeTruthy();
  }

  /** Opens "Referens", replaces the text and saves, waiting for the server to confirm. */
  async setReference(text: string) {
    // The action panel is a *sibling* of order-details, not a child of it (see
    // Chalmers.ILL.OrderItem.cshtml), so it has to be looked up from the page.
    const panel = this.page.getByTestId("order-action-panel");
    const saved = this.page.waitForResponse((r) => r.url().includes("/OrderItemReferenceSurface/SetReference"));
    // Same re-render hazard as setType: start over if the panel is replaced mid-edit.
    await expect(async () => {
      if (!(await panel.getByTestId("reference-input").isVisible())) {
        await this.details.getByTestId("order-open-reference").click({ timeout: 10_000 });
      }
      await panel.getByTestId("reference-input").fill(text, { timeout: 5_000 });
      await panel.getByTestId("reference-save").click({ timeout: 5_000 });
    }).toPass({ timeout: 60_000 });
    expect((await saved).ok()).toBeTruthy();
  }
}
