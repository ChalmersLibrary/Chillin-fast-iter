import * as fs from "fs";
import * as path from "path";
import { expect, test } from "../fixtures";
import { OrderListPage } from "../pages/OrderListPage";
import { createOrderThroughMail } from "../pages/StartPage";
import { RunningApp } from "../support/app";

// Scenario IDs refer to e2e/scenarios/SCENARIOS.md.
//
// The delivery flow ends in an e-mail to the patron, so these read the outbox back the same way
// the MAIL scenarios do - a status change with no mail, or a mail with no status change, would
// otherwise look like success.

function sentMails(app: RunningApp): { to: string; subject: string; body: string }[] {
  const outbox = path.join(app.dataPath, "mail", "sentitems");
  if (!fs.existsSync(outbox)) return [];
  return fs.readdirSync(outbox).map((dir) => {
    const meta = JSON.parse(fs.readFileSync(path.join(outbox, dir, "message.json"), "utf8"));
    return { to: meta.To, subject: meta.Subject, body: fs.readFileSync(path.join(outbox, dir, "body.html"), "utf8") };
  });
}

test.describe("delivery", () => {
  test("DELIV-002: delivering an article mails the patron and moves the order on", async ({ ownApp, guard }) => {
    guard.allow(/Successfully sent new order/);
    const { app, page } = await ownApp();
    const reference = await createOrderThroughMail(page, "e2e-deliv-002", {
      name: "Levine Leveranssson",
      email: "levine@example.invalid",
    });

    const list = new OrderListPage(page);
    await list.goto();
    const row = list.row(reference);
    await row.open();
    await row.setType("Artikel");

    await row.details.getByTestId("order-open-delivery").click();
    // Which delivery type comes up is the app's choice, not the test's: for a patron whose
    // address could not be looked up it picks "Direktleverans via post" rather than e-post.
    // Every delivery-type partial has the same Leverera! button, so take whichever is rendered.
    await expect(page.getByTestId("deliver-now")).toBeVisible();
    const deliveryType = await page.locator("#active-delivery-type").first().innerText();

    const delivered = page.waitForResponse((r) => r.url().includes("/OrderItemDeliverySurface/Deliver"));
    await page.getByTestId("deliver-now").click();
    const response = await delivered;
    expect(response.ok()).toBeTruthy();
    expect((await response.json()).Success, "the delivery was refused").toBeTruthy();

    // The patron got a mail...
    const toPatron = sentMails(app).filter((m) => m.to === "levine@example.invalid");
    expect(toPatron.length, `no mail reached the patron (delivery type: ${deliveryType})`).toBeGreaterThan(0);

    // ...and the order moved on. Which status depends on the delivery type the app chose.
    const after = await list.find(reference);
    await expect(after.status, `delivery type: ${deliveryType}`).not.toHaveText("Ny");
  });

  test("DELIV-004: returning an order sets it to Återsänd", async ({ ownApp, guard }) => {
    guard.allow(/Successfully sent new order/);
    const { page } = await ownApp();
    const reference = await createOrderThroughMail(page, "e2e-deliv-004");

    const list = new OrderListPage(page);
    await list.goto();
    const row = list.row(reference);
    await row.open();
    await row.setType("Bok");

    await row.details.getByTestId("order-open-return").click();
    await expect(page.getByTestId("return-confirm")).toBeVisible();

    const returned = page.waitForResponse((r) => r.url().includes("/OrderItemReturnSurface/ReturnItem"));
    await page.getByTestId("return-confirm").click();
    expect((await returned).ok()).toBeTruthy();

    const after = await list.find(reference);
    await expect(after.status).toHaveText("Återsänd");
  });

});
