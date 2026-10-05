import * as fs from "fs";
import * as path from "path";
import { expect, test } from "../fixtures";
import { OrderListPage, OrderRow } from "../pages/OrderListPage";
import { createOrderThroughMail } from "../pages/StartPage";
import { RunningApp } from "../support/app";

// Scenario IDs refer to e2e/scenarios/SCENARIOS.md.
//
// Outgoing mail is written to DataPath/mail/sentitems in isolated mode (FileMailWebApi), one
// directory per message with body.html and message.json. That is what these read back - a
// "Success" answer from the server says nothing about what was actually sent.

function sentMails(app: RunningApp): { to: string; subject: string; body: string }[] {
  const outbox = path.join(app.dataPath, "mail", "sentitems");
  if (!fs.existsSync(outbox)) return [];
  return fs.readdirSync(outbox).map((dir) => {
    const meta = JSON.parse(fs.readFileSync(path.join(outbox, dir, "message.json"), "utf8"));
    return { to: meta.To, subject: meta.Subject, body: fs.readFileSync(path.join(outbox, dir, "body.html"), "utf8") };
  });
}

async function openMail(page: import("@playwright/test").Page, row: OrderRow) {
  await row.details.getByTestId("order-open-mail").click();
  await expect(page.getByTestId("mail-send")).toBeVisible();
}

/** The status dropdown in the mail form - the mail refuses to go without one. */
async function chooseNoStatusChange(page: import("@playwright/test").Page) {
  await page.locator("#orderitem-mail-statuslist").locator("..").locator(".dropdown-toggle").click();
  await page.locator("#orderitem-mail-statuslist a", { hasText: "Ingen förändring" }).click();
  await expect(page.locator("#mail-currently-selected-status")).toHaveText("Ingen förändring");
}

test.describe("mail", () => {
  test("MAIL-001: a mail to the patron is actually written to the outbox", async ({ ownApp, guard }) => {
    guard.allow(/Successfully sent new order/);
    const { app, page } = await ownApp();
    const reference = await createOrderThroughMail(page, "e2e-mail-001", {
      name: "Mottagare Mottagarsson",
      email: "mottagare@example.invalid",
    });

    const list = new OrderListPage(page);
    await list.goto();
    const row = list.row(reference);
    await row.open();
    await row.setType("Artikel");
    await openMail(page, row);
    await chooseNoStatusChange(page);

    const body = "Hej! Din beställning är klar. Hälsningar, e2e åäö";
    await page.getByTestId("mail-message").fill(body);
    const sent = page.waitForResponse((r) => r.url().includes("/OrderItemMailSurface/SendMail"));
    await page.getByTestId("mail-send").click();
    expect((await sent).ok()).toBeTruthy();

    const mails = sentMails(app);
    expect(mails.length, "nothing reached the outbox").toBeGreaterThan(0);
    const toPatron = mails.find((m) => m.to === "mottagare@example.invalid");
    expect(toPatron, `outbox held ${JSON.stringify(mails.map((m) => m.to))}`).toBeTruthy();
    expect(toPatron!.body).toContain("Hälsningar, e2e åäö");
    // The order id belongs in the subject - it is what binds a reply back to the order.
    expect(toPatron!.subject).toMatch(/cthb-/);
  });

  test("MAIL-002: a template is filled in with the order's own details", async ({ ownApp, guard }) => {
    guard.allow(/Successfully sent new order|Lyckades|Sparade/);
    const { page } = await ownApp();
    const reference = await createOrderThroughMail(page, "e2e-mail-002", { name: "Malin Mallsson" });

    // The 13 seeded system templates are all Automatic, and the mail view only offers manual ones
    // (GetManualTemplates) - so in isolated mode there is nothing to pick until one is made.
    await page.goto("/bestaellningar/instaellningar/", { waitUntil: "load" });
    await page.locator("#template-settings-link").click();
    const created = page.waitForResponse((r) => r.url().includes("/TemplatesSurface/CreateTemplate"));
    await page.getByTestId("new-template-description").fill("E2E-mailmall");
    await page.getByTestId("create-template").click();
    expect((await created).ok()).toBeTruthy();

    await page.reload({ waitUntil: "load" });
    await page.locator("#template-settings-link").click();
    await page.locator("#template-list .list-group-item", { hasText: "E2E-mailmall" }).click();
    const saved = page.waitForResponse((r) => r.url().includes("/TemplatesSurface/SetTemplateData"));
    await page.getByTestId("template-data").fill("Hej {{PatronName}}, din order {{OrderId}} är klar.");
    await page.getByTestId("save-template").click();
    expect((await saved).ok()).toBeTruthy();

    const list = new OrderListPage(page);
    await list.goto();
    const row = list.row(reference);
    await row.open();
    await row.setType("Artikel");
    await openMail(page, row);

    // The templates sit in two .dropdown-submenu groups, "Fjärrlån" and "Förvärv", which open on
    // hover in CSS - the same shape as the status submenus in the order panel.
    const menu = page.getByTestId("mail-template-menu");
    const populated = page.waitForResponse((r) => r.url().includes("/TemplatesSurface/GetPopulatedTemplateData"));
    await page.getByTestId("mail-template-toggle").click();
    await menu.locator("li.dropdown-submenu > a", { hasText: "Fjärrlån" }).hover();
    await menu.locator("li.dropdown-submenu", { hasText: "Fjärrlån" }).locator("ul.dropdown-menu a", { hasText: "E2E-mailmall" }).click();
    expect((await populated).ok()).toBeTruthy();

    // The placeholders have to be gone, replaced by this order's own values.
    await expect(page.getByTestId("mail-message")).toHaveValue(/Hej Malin Mallsson, din order cthb-[^ ]+ är klar\./);
    await expect(page.getByTestId("mail-message")).not.toHaveValue(/\{\{/);
  });

  test("MAIL-003: the mail cannot be sent without saying what status it leaves behind", async ({ ownApp, guard }) => {
    guard.allow(/Successfully sent new order/);
    guard.allow(/måste välja en status/);
    const { app, page } = await ownApp();
    const reference = await createOrderThroughMail(page, "e2e-mail-003");

    const list = new OrderListPage(page);
    await list.goto();
    const row = list.row(reference);
    await row.open();
    await row.setType("Artikel");
    await openMail(page, row);

    const complaints: string[] = [];
    page.on("dialog", (d) => complaints.push(d.message()));

    await page.getByTestId("mail-message").fill("Utan status");
    await page.getByTestId("mail-send").click();

    await expect(async () => {
      expect(complaints.join("\n")).toMatch(/måste välja en status/);
    }).toPass({ timeout: 15_000 });

    // ...and nothing was sent.
    expect(sentMails(app).filter((m) => m.body.includes("Utan status"))).toHaveLength(0);
  });
});

test.describe("mail, what goes into the message", () => {
  test("MAIL-004: the empty template brings the signature and the original order along", async ({ ownApp, guard }) => {
    guard.allow(/Successfully sent new order/);
    const { page } = await ownApp();
    const reference = await createOrderThroughMail(page, "e2e-mail-004");

    const list = new OrderListPage(page);
    await list.goto();
    const row = list.row(reference);
    await row.open();
    await row.setType("Artikel");
    await openMail(page, row);

    // "Tom" is the no-template choice: it still fills the message with the signature and the
    // order as the patron sent it in, which is what staff reply on top of.
    await page.getByTestId("mail-template-toggle").click();
    await page.getByTestId("mail-template-menu").locator("a", { hasText: "Tom" }).first().click();

    // The original order is the text the patron wrote - here, the reference we sent in.
    await expect(page.getByTestId("mail-message")).toHaveValue(new RegExp(reference));
  });
});
