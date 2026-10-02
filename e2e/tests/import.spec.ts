import { expect, test } from "../fixtures";
import { OrderListPage } from "../pages/OrderListPage";
import { createOrderThroughMail } from "../pages/StartPage";

// Scenario IDs refer to e2e/scenarios/SCENARIOS.md.
//
// Uploading goes through a hidden <input type="file"> in the delivery view, read with a FileReader
// and posted as a data URL to /ImportDocumentSurface/ImportFromData. setInputFiles drives the real
// input, so the whole client path runs - not just the endpoint.

const A_TINY_PDF = Buffer.from(
  "%PDF-1.4\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n2 0 obj<</Type/Pages/Kids[]/Count 0>>endobj\n" +
    "trailer<</Root 1 0 R>>\n%%EOF\n"
);

test.describe("import", () => {
  test("IMPORT-001: an uploaded document is attached to the order and counted", async ({ ownApp, guard }) => {
    guard.allow(/Successfully sent new order/);
    const { page } = await ownApp();
    const reference = await createOrderThroughMail(page, "e2e-import-001");

    const list = new OrderListPage(page);
    await list.goto();
    const row = list.row(reference);
    await row.open();
    await row.setType("Artikel");

    // The counter on the Leverans button starts at zero.
    await expect(row.details.getByTestId("order-open-delivery")).toContainText("0");
    await row.details.getByTestId("order-open-delivery").click();
    await expect(page.getByTestId("deliver-now")).toBeVisible();

    // Pick e-post explicitly. For a patron with no address the app opens "Direktleverans via
    // post", whose view has no attachments group at all - the upload would succeed server-side
    // with nowhere to show it.
    await page.locator("#delivery-type-dropdown").first().click();
    await page.locator('a[data-delivery-type="epost"]').first().click();
    await expect(page.locator("#hidden-file-upload")).toBeAttached();

    const uploaded = page.waitForResponse((r) => r.url().includes("/ImportDocumentSurface/ImportFromData"));
    await page.locator("#hidden-file-upload").setInputFiles({
      name: "e2e-artikel.pdf",
      mimeType: "application/pdf",
      buffer: A_TINY_PDF,
    });
    const response = await uploaded;
    expect(response.ok()).toBeTruthy();
    expect((await response.json()).Success, "the upload was refused").toBeTruthy();

    // The attachment shows up by name, and the counter follows.
    await expect(page.locator(".btn-attach-attachment", { hasText: "e2e-artikel" })).toBeVisible();

    await list.goto();
    const again = list.row(reference);
    await again.open();
    await expect(again.details.getByTestId("order-open-delivery")).toContainText("1");
  });

  test("IMPORT-003: uploading needs the order's lock", async ({ ownApp, guard }) => {
    guard.allow(/Successfully sent new order/);
    const { page, newSession } = await ownApp();
    const reference = await createOrderThroughMail(page, "e2e-import-003");

    const list = new OrderListPage(page);
    await list.goto();
    const row = list.row(reference);
    await row.open(); // this session now holds the lock
    const nodeId = await row.nodeId();

    // Someone else tries to attach a document to the same order.
    const other = await newSession("admin");
    const response = await other.request.post("/ImportDocumentSurface/ImportFromData", {
      form: {
        orderItemNodeId: nodeId,
        filename: "smyger-in.pdf",
        data: "data:application/pdf;base64," + A_TINY_PDF.toString("base64"),
      },
    });
    expect(response.ok()).toBeTruthy(); // the refusal is a normal JSON answer
    const body = await response.json();
    expect(body.Success, "a locked order must not accept an attachment from someone else").toBeFalsy();
    expect(body.Message).toContain("låst av");
  });
});
