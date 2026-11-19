import { expect, test } from "../fixtures";
import type { Page } from "@playwright/test";

// Scenario IDs refer to e2e/scenarios/SCENARIOS.md.
//
// Claudeborgen (claudeborgen/DESIGN.md) on the start page. In isolated mode Claude is FakeClaude
// inside the app: each round walks through its phases on a timer (a few seconds each), and
// markers in the text steer it - "[fråga]" makes it ask instead of deliver, "[krasch]" makes the
// round crash. Nothing here reaches a real Claude or pushes anything.
//
// The chat boxes post without reloading the page, and the conversation page follows the round
// live (SignalR). Both are checked by setting a marker on `window` that a reload would wipe.

test.use({ role: "objektspecialist" });

const ROUND = { timeout: 90_000 };

async function startConversation(page: Page, title: string, body: string): Promise<number> {
  await page.goto("/");
  await page.getByTestId("claude-start-toggle").click();
  await page.locator("#claude-title").fill(title);
  await page.locator("#claude-body").fill(body);
  await Promise.all([page.waitForURL(/\/Claude\/Conversation\/\d+/), page.getByTestId("claude-start-form").locator("button[type=submit]").click()]);
  return Number(page.url().match(/Conversation\/(\d+)/)![1]);
}

async function markNoReload(page: Page) {
  await page.evaluate(() => ((window as any).__noReload = true));
}

async function expectNoReload(page: Page, what: string) {
  expect(await page.evaluate(() => (window as any).__noReload === true), `${what}: the page reloaded`).toBe(true);
}

/** Sends from a chat box and checks it happened in place: the box empties, the page stays. */
async function sendInPlace(page: Page, send: () => Promise<void>, what: string) {
  await markNoReload(page);
  await send();
  await expect(page.locator("[data-claude-send] textarea").first(), `${what}: the box wasn't emptied`).toHaveValue("");
  await expectNoReload(page, what);
}

const conversationState = (page: Page) => page.getByTestId("claude-conversation-state");
/** Delivered rounds in the conversation's round table - grows by one each time a round goes green. */
const greenRounds = (page: Page) => page.getByTestId("claude-rounds").locator("tr").filter({ hasText: "Gröna tester" });

test.describe("claude", () => {
  test("CLAUDE-001: an idea is talked through in the start page chat and becomes work", async ({ page }) => {
    await page.goto("/");
    const box = page.getByTestId("claude-idea-form").locator("textarea");

    // Chat convention: Shift+Enter is a new line, Enter sends.
    await box.pressSequentially("Tänk om man kunde se hur länge");
    await box.press("Shift+Enter");
    await box.pressSequentially("en beställning har legat i samma status?");
    expect(await box.inputValue()).toContain("\n");
    expect(page.url(), "Shift+Enter sent the message").not.toContain("idea=");

    await sendInPlace(page, () => box.press("Enter"), "First idea message");
    expect(page.url(), "the address should point at the new idea").toMatch(/[?]idea=\d+/);
    const chat = page.getByTestId("claude-idea-chat");
    await expect(chat).toHaveAttribute("data-state", "WaitingForOwner", ROUND);

    await box.fill("Det är mest för handläggarna, så de ser vad som fastnat.");
    await sendInPlace(page, () => page.getByTestId("claude-idea-form").locator("button[type=submit]").click(), "Idea reply");
    await expect(chat.locator(":scope > div")).toHaveCount(4, ROUND);
    await expect(chat).toHaveAttribute("data-state", "WaitingForOwner", ROUND);

    await Promise.all([page.waitForURL(/\/Claude\/Conversation\/\d+/), page.getByTestId("claude-convert-form").locator("button[type=submit]").click()]);
    await expect(conversationState(page)).toHaveAttribute("data-state", "WaitingForOwner", ROUND);
  });

  test("CLAUDE-002: a round is followed live, and the finished change lands in the start page changelog", async ({ page, guard }) => {
    // Typing in the changelog box and then clicking done is two posts racing: the box saves itself
    // on blur (claude.js) and the done form carries the same text. When done wins, the save meets a
    // finished conversation and gets 409 - unseen by the user, and the text arrives anyway (checked
    // on the start page below).
    guard.allow(/409 POST \/Claude\/ChangelogText\//);
    await startConversation(page, "Exportknapp", "Lägg till en exportknapp på beställningslistan.");
    await markNoReload(page);

    await expect(page.getByTestId("claude-status")).toContainText("jobbar på", { timeout: 30_000 });
    await expect(conversationState(page)).toHaveAttribute("data-state", "WaitingForOwner", ROUND);
    await expect(page.getByTestId("claude-rounds")).toContainText("Gröna tester", { timeout: 30_000 });
    await expectNoReload(page, "Following the round");

    // Claude suggests the changelog text; the owner may rewrite it before marking the work done.
    await expect(page.locator("#claude-changelog")).toHaveValue(/^\(Fejkat förslag\)/);
    await page.locator("#claude-changelog").fill("Exportknapp på beställningslistan");
    await Promise.all([page.waitForEvent("load"), page.getByTestId("claude-done-form").locator("button[type=submit]").click()]);

    await page.goto("/");
    await expect(page.getByTestId("claude-changelog-entry").filter({ hasText: "Exportknapp på beställningslistan" })).toHaveCount(1);
  });

  test("CLAUDE-003: Claude asks instead of guessing, and the owner's own changelog text survives the next round", async ({ page }) => {
    await startConversation(page, "Knapp", "Lägg till en knapp [fråga]");
    await expect(page.getByTestId("claude-question")).toBeVisible(ROUND);

    const reply = page.locator("#claude-reply");
    await reply.fill("Överst, tack.");
    await sendInPlace(page, () => page.getByTestId("claude-reply-form").locator("button[type=submit]").click(), "Conversation reply");
    await expect(conversationState(page)).toHaveAttribute("data-state", "WaitingForOwner", ROUND);
    // Claude's new suggestion moves into the box live, without a reload.
    const changelog = page.locator("#claude-changelog");
    await expect(changelog).toHaveValue("(Fejkat förslag) Överst, tack.", { timeout: 30_000 });

    // The owner's own text is saved when the box loses focus...
    const saved = page.waitForResponse((r) => r.url().includes("/Claude/ChangelogText/"));
    await changelog.fill("Knapp överst på sidan");
    await reply.click();
    expect((await saved).ok()).toBeTruthy();

    // ...and Claude's next round doesn't overwrite it.
    const before = await greenRounds(page).count();
    await reply.fill("Gör den grön också.");
    await sendInPlace(page, () => page.getByTestId("claude-reply-form").locator("button[type=submit]").click(), "Second conversation reply");
    await expect(greenRounds(page)).toHaveCount(before + 1, ROUND);
    await expect(conversationState(page)).toHaveAttribute("data-state", "WaitingForOwner", ROUND);
    await expect(changelog).toHaveValue("Knapp överst på sidan");
  });

  test("CLAUDE-004: a crashed round is shown to IT, who can pause development; Viewer only reads", async ({ page, newSession, guard }) => {
    guard.accept(/Pausa utvecklingen/);
    const crashId = await startConversation(page, "Kraschtest", "Det här varvet kraschar [krasch]");
    await expect(page.getByTestId("claude-needs-it")).toBeVisible(ROUND);

    const it = await newSession("objektspecialistit");
    await it.goto("/");
    await expect(it.getByTestId("claude-attention")).toContainText("Kraschtest");
    await expect(it.getByTestId("claude-start-form"), "IT shouldn't get the start form").toHaveCount(0);

    // IT switches development off (e.g. to work on the branch by hand) and back on.
    await Promise.all([it.waitForEvent("load"), it.getByTestId("claude-pause-development").click()]);
    await expect(it.getByTestId("claude-development-paused")).toBeVisible();
    await Promise.all([it.waitForEvent("load"), it.getByTestId("claude-resume-development").click()]);
    await expect(it.getByTestId("claude-development-paused")).toHaveCount(0);

    await it.goto(`/Claude/Conversation/${crashId}`);
    await expect(it.getByTestId("claude-reply-form")).toBeVisible();
    await expect(it.getByTestId("claude-done-form"), "only the owner marks work done").toHaveCount(0);

    const viewer = await newSession("viewer");
    await viewer.goto(`/Claude/Conversation/${crashId}`);
    await expect(viewer.getByTestId("claude-conversation-state")).toBeVisible();
    await expect(viewer.locator("form[data-testid^=claude-]"), "Viewer shouldn't get any form").toHaveCount(0);
  });
});
