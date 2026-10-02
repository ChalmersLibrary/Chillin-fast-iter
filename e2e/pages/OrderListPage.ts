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

  /**
   * Searches for one order and waits for its row. Needed for any order that has left the default
   * list: that list only holds the pending statuses, so an order set to Annullerad, Inköpt,
   * Levererad and so on is reachable only through a search.
   *
   * The reference is quoted because it contains hyphens, which the query parser reads as NOT.
   */
  async find(reference: string): Promise<OrderRow> {
    await this.page.goto(`/bestaellningar?query=${encodeURIComponent('"' + reference + '"')}`, { waitUntil: "domcontentloaded" });
    const row = this.row(reference);
    await expect(row.root).toBeVisible();
    return row;
  }

  /** Like goto(), but for a search that is expected to find nothing. */
  async gotoExpectingNoHits(query: string) {
    await this.page.goto(`/bestaellningar?query=${encodeURIComponent(query)}`, { waitUntil: "domcontentloaded" });
    await expect(this.noOrders).toBeVisible();
  }

  async reload() {
    await this.page.reload({ waitUntil: "domcontentloaded" });
    await expect(this.page.getByTestId("order-row").first()).toBeVisible();
  }

  get searchHeading() { return this.page.getByTestId("search-heading"); }
  get noOrders() { return this.page.getByTestId("no-orders"); }
  /** Every row currently *visible*. The filters hide rows rather than remove them. */
  get visibleRows() { return this.page.getByTestId("order-row").filter({ visible: true }); }

  /**
   * A status or library filter button. They are identified by their `value`, which is the CSS
   * class the client-side filter matches on (".chillin-status-01", ".Huvudbiblioteket", "" for
   * "Alla") - a stabler handle than the Swedish label next to it.
   */
  statusFilter(value: string) {
    return this.page.getByTestId("status-filter-buttons").locator(`button[value="${value}"]`);
  }

  libraryFilter(value: string) {
    return this.page.getByTestId("library-filter-buttons").locator(`button[value="${value}"]`);
  }

  /** The number in a filter button's badge. */
  async filterCount(button: Locator): Promise<number> {
    return Number((await button.locator(".badge").innerText()).trim());
  }

  /** Clicks a filter and waits for the list to settle (the filter animates rows in and out). */
  async applyFilter(button: Locator) {
    await button.click();
    await expect(button).toHaveClass(/active/);
    // The slide animation runs for 400ms; without settling, a count can be read mid-flight.
    await this.page.waitForTimeout(600);
  }

  /** The references of the rows that are visible right now, in list order. */
  async visibleReferences(): Promise<string[]> {
    const texts = await this.visibleRows.getByTestId("order-reference").allInnerTexts();
    return texts.map((t) => t.trim());
  }

  /** The row whose reference contains the given text. Seed orders have unique ones, e.g. "ref-ny-001". */
  row(reference: string): OrderRow {
    const root = this.page
      .getByTestId("order-row")
      .filter({ has: this.page.getByTestId("order-reference").filter({ hasText: reference }) });
    return new OrderRow(this.page, root);
  }
}

/** Matches an element whose whole text is `label`, with surrounding whitespace ignored. */
function exactly(label: string): RegExp {
  return new RegExp(`^\\s*${label.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")}\\s*$`);
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

  /**
   * Picks an item out of one of the order panel's dropdowns and waits for the server to confirm.
   *
   * Opening an order takes its lock, and the resulting SignalR notification makes the page
   * re-render the details - which closes a dropdown that was just opened. The retry covers that
   * (and slow machines) by starting over whenever the menu vanishes underneath the click.
   *
   * `path` is the chain of items to walk inside the menu; the status menu nests a submenu under
   * "Annullerad" and "Inköpt", everything else is one level. A submenu step is marked `open`,
   * because .dropdown-submenu opens on hover in CSS - clicking its parent does nothing.
   */
  private async chooseFromMenu(
    what: { toggle: string; menu: string; confirms: string },
    path: { testId: string; label: string; open?: boolean }[]
  ) {
    const menu = this.details.getByTestId(what.menu);
    const saved = this.page.waitForResponse((r) => r.url().includes(what.confirms));
    await expect(async () => {
      if (!(await menu.isVisible())) await this.details.getByTestId(what.toggle).click({ timeout: 10_000 });
      for (const step of path) {
        // Exact text, not substring: "Bok" would otherwise also match "E-bok" in the
        // purchased-material submenu, and the click would fail on two matches.
        const item = menu.getByTestId(step.testId).filter({ hasText: exactly(step.label) });
        if (step.open) await item.hover({ timeout: 5_000 });
        else await item.click({ timeout: 5_000 });
      }
    }).toPass({ timeout: 60_000 });
    expect((await saved).ok()).toBeTruthy();
  }

  /** Changes the order type through the real dropdown and waits for the server to confirm it. */
  async setType(typeName: string) {
    await this.chooseFromMenu(
      { toggle: "order-type-toggle", menu: "order-type-menu", confirms: "/OrderItemTypeSurface/SetOrderItemType" },
      [{ testId: "order-type-option", label: typeName }]
    );
  }

  /** Changes the status. Only the statuses the app currently allows are in the menu. */
  async setStatus(statusLabel: string) {
    await this.chooseFromMenu(
      { toggle: "order-status-toggle", menu: "order-status-menu", confirms: "/OrderItemStatusSurface/SetOrderItemStatus" },
      [{ testId: "order-status-option", label: statusLabel }]
    );
  }

  /** "Annullerad" is a submenu: the status cannot be set without saying why. */
  async cancelWithReason(reason: string) {
    await this.chooseFromMenu(
      { toggle: "order-status-toggle", menu: "order-status-menu", confirms: "SetOrderItemStatus?orderNodeId=" },  // ...&cancellationReasonId=, same endpoint as a plain status change
      [{ testId: "order-status-submenu", label: "Annullerad", open: true }, { testId: "order-cancellation-reason-option", label: reason }]
    );
  }

  /** "Inköpt" is a submenu too: it needs the kind of material that was bought. */
  async markPurchased(material: string) {
    await this.chooseFromMenu(
      { toggle: "order-status-toggle", menu: "order-status-menu", confirms: "SetOrderItemStatus?orderNodeId=" },  // ...&purchasedMaterialId=, same endpoint as a plain status change
      [{ testId: "order-status-submenu", label: "Inköpt", open: true }, { testId: "order-purchased-material-option", label: material }]
    );
  }

  async setDeliveryLibrary(libraryName: string) {
    await this.chooseFromMenu(
      {
        toggle: "order-delivery-library-toggle",
        menu: "order-delivery-library-menu",
        confirms: "/OrderItemDeliveryLibrarySurface/SetOrderItemDeliveryLibrary",
      },
      [{ testId: "order-delivery-library-option", label: libraryName }]
    );
  }

  async setPurchaseLibrary(libraryName: string) {
    await this.chooseFromMenu(
      {
        toggle: "order-purchase-library-toggle",
        menu: "order-purchase-library-menu",
        confirms: "/OrderItemPurchaseLibrarySurface/SetOrderItemPurchaseLibrary",
      },
      [{ testId: "order-purchase-library-option", label: libraryName }]
    );
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
