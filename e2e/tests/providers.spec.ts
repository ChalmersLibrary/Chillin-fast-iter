import { expect, test } from "../fixtures";
import { OrderListPage } from "../pages/OrderListPage";
import { createOrderThroughMail } from "../pages/StartPage";

// Scenario IDs refer to e2e/scenarios/SCENARIOS.md.
//
// Providers are not a list anyone maintains: the choices come from AggregatedProviders(), which
// reads back the provider names already set on orders. "Adding a provider" therefore means using
// one on an order - after which it is offered on the next one.

test.describe("providers", () => {
  test("SET-006: a provider name used on one order is offered on the next", async ({ ownApp, guard }) => {
    guard.allow(/Successfully sent new order/);
    // The isolated FOLIO fake knows nothing about this patron, so saving provider data asks
    // whether to go ahead anyway. That is the app being careful, not an error - say yes.
    guard.accept(/Vill du fortsätta med beställningen ändå/);
    const { page } = await ownApp();
    const first = await createOrderThroughMail(page, "e2e-set-006-a");
    const second = await createOrderThroughMail(page, "e2e-set-006-b");

    const providerName = "E2E Leverantör " + Date.now();
    const list = new OrderListPage(page);
    await list.goto();

    const row = list.row(first);
    await row.open();
    await row.setType("Bok");
    await row.details.getByTestId("order-open-provider").click();
    await expect(page.getByTestId("provider-name")).toBeVisible();

    // It is not in the list to begin with - which otherwise holds the three fixed ones.
    await expect(page.getByTestId("provider-list")).toContainText("Libris");
    await expect(page.getByTestId("provider-list")).not.toContainText(providerName);

    // "Genomför ny beställning", not "Spara utan att ändra status": AggregatedProviders() only
    // counts orders that have left Ny/Annullerad/Inköpt/Överförd, so a name saved on an order
    // still in Ny would never show up in the list. Same rule in both searchers.
    const saved = page.waitForResponse((r) => r.url().includes("/OrderItemProviderSurface/SetProvider"));
    await page.getByTestId("provider-name").fill(providerName);
    await page.getByRole("button", { name: "Genomför ny beställning" }).click();
    expect((await saved).ok()).toBeTruthy();

    // On another order it is now one of the choices.
    await list.goto();
    const other = list.row(second);
    await other.open();
    await other.setType("Bok");
    await other.details.getByTestId("order-open-provider").click();
    await expect(page.getByTestId("provider-list")).toContainText(providerName);
  });

  test("DELIV-006: the lending library's due date can be set and is kept", async ({ ownApp, guard }) => {
    guard.allow(/Successfully sent new order|Lyckades/);
    const { page } = await ownApp();
    const reference = await createOrderThroughMail(page, "e2e-deliv-006");

    const list = new OrderListPage(page);
    await list.goto();
    const row = list.row(reference);
    await row.open();
    await row.setType("Bok");

    await row.details.getByTestId("order-loan-period-toggle").click();
    await row.details.getByTestId("order-open-provider-due-date").click();
    await expect(page.getByTestId("provider-due-date")).toBeVisible();

    const dueDate = "2027-03-04 12:00";
    const saved = page.waitForResponse((r) => r.url().includes("/OrderItemProviderReturnDateSurface/ChangeReturnDate"));
    await page.getByTestId("provider-due-date").fill(dueDate);
    await page.getByTestId("provider-due-date-save").click();
    expect((await saved).ok()).toBeTruthy();

    // Read it back from a fresh render of the order, not from the form we just typed into.
    await list.goto();
    const again = list.row(reference);
    await again.open();
    await again.details.getByTestId("order-loan-period-toggle").click();
    await again.details.getByTestId("order-open-provider-due-date").click();
    await expect(page.getByTestId("provider-due-date")).toHaveValue(/2027-03-04/);
  });
});
