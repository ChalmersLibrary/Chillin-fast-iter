import { test as base, expect, Browser, BrowserContext, Page, TestInfo } from "@playwright/test";
import { RunningApp, startApp } from "./support/app";
import { PASSWORD, Role } from "./support/members";

export { expect };

/**
 * Collects things that are never supposed to happen in a healthy page - JS errors, failed
 * requests to the app itself, unexpected dialogs - and fails the test at teardown if any did.
 * It exists because most real bugs in this app were *silent* (a 404 on a script, a JSON field
 * with the wrong casing, a handler that threw) and a scenario that only checks the happy path
 * sails straight past them.
 *
 * A test that deliberately provokes one (e.g. "when the save fails, the busy overlay goes
 * away") declares it with `guard.allow(/pattern/)`.
 */
export class Guard {
  private problems: string[] = [];
  private allowed: RegExp[] = [];
  constructor(private origin: string) {}

  allow(pattern: RegExp) {
    this.allowed.push(pattern);
  }

  watch(page: Page) {
    const where = () => {
      try { return new URL(page.url()).pathname; } catch { return page.url(); }
    };
    page.on("pageerror", (e) => this.add(`[${where()}] uncaught error: ${e.message}`));
    page.on("console", (m) => {
      if (m.type() !== "error") return;
      // "Failed to load resource" carries no URL; the response listener below reports it with one.
      if (m.text().startsWith("Failed to load resource")) return;
      this.add(`[${where()}] console.error: ${m.text()}`);
    });
    page.on("response", (r) => {
      if (r.status() >= 400 && r.url().startsWith(this.origin)) {
        this.add(`[${where()}] ${r.status()} ${r.request().method()} ${r.url().slice(this.origin.length)}`);
      }
    });
    // Same-origin only: external hosts (CDNs, fonts) are unreachable from the sandbox by design.
    page.on("requestfailed", (r) => {
      if (r.url().startsWith(this.origin) && !(r.failure()?.errorText ?? "").includes("ERR_ABORTED")) {
        this.add(`[${where()}] request failed ${r.method()} ${r.url().slice(this.origin.length)}: ${r.failure()?.errorText}`);
      }
    });
    // A dialog blocks headless Chromium forever unless dismissed, so always dismiss - but record it.
    page.on("dialog", (d) => {
      this.add(`[${where()}] ${d.type()} dialog: ${d.message()}`);
      d.dismiss().catch(() => {});
    });
  }

  // SignalR tries WebSockets first and silently falls back to long polling. When the machine is
  // slow the negotiated connection can expire before the socket opens (404) - the app still works.
  private static readonly benign = [/WebSocket connection to '[^']*\/notificationHub\?[^']*' failed/, /Failed to start the transport 'WebSockets'/];

  private add(problem: string) {
    if (Guard.benign.some((b) => b.test(problem))) return;
    if (!this.allowed.some((a) => a.test(problem))) this.problems.push(problem);
  }

  report(): string[] {
    return this.problems;
  }
}

type TestFixtures = {
  /** Which account the default `page`/`context` is logged in as. Set per file/describe with test.use({ role }). */
  role: Role | "anonymous";
  storageState: any;
  guard: Guard;
  /** Opens an additional, independent browser session (own cookies) - for multi-user scenarios. */
  newSession: (role: Role | "anonymous") => Promise<Page>;
};

type WorkerFixtures = {
  app: RunningApp;
  loginState: (role: Role) => Promise<any>;
};

// Contexts made by hand with browser.newContext() don't inherit the timeouts from playwright.config
// (only the test runner's own `context` does), and this devcontainer is single-core and slow.
function tune(context: BrowserContext) {
  context.setDefaultTimeout(60_000);
  context.setDefaultNavigationTimeout(120_000);
  return context;
}

// The first request to each page makes the app JIT-compile its views, which takes a minute on this
// single-core box - longer than a test's navigation timeout. Do it once, with a generous timeout.
const warmed = new WeakSet<RunningApp>();
async function warmUp(context: BrowserContext, app: RunningApp, role: Role) {
  if (warmed.has(app) || role === "roleless") return;
  warmed.add(app);
  for (const path of ["/bestaellningar", "/OrderItemSurface/RenderOrderItem?nodeId=1"]) {
    await context.request.get(path, { timeout: 150_000 }).catch(() => {});
  }
}

async function login(browser: Browser, app: RunningApp, role: Role) {
  const context = tune(await browser.newContext({ baseURL: app.baseUrl }));
  const page = await context.newPage();
  page.on("dialog", (d) => d.dismiss().catch(() => {}));
  try {
    // The very first navigation of a cold Chromium on this box occasionally never completes;
    // a second attempt on the same page then goes through quickly.
    await page.goto("/ChalmersILLLoginPage", { waitUntil: "domcontentloaded", timeout: 60_000 }).catch(() =>
      page.goto("/ChalmersILLLoginPage", { waitUntil: "domcontentloaded" })
    );
    await page.getByTestId("login-username").fill(role);
    await page.getByTestId("login-password").fill(PASSWORD);
    await Promise.all([
      page.waitForURL((u) => !u.pathname.includes("LoginPage"), { waitUntil: "load" }),
      page.getByTestId("login-submit").click(),
    ]);
  } catch (e) {
    // Fixture-time failures get no screenshot from the runner, so say what the page showed.
    const body = await page.locator("body").innerText({ timeout: 2000 }).catch(() => "(no body)");
    throw new Error(`Login as "${role}" failed at ${page.url()}: ${(e as Error).message}\nPage text: ${body.slice(0, 500)}`);
  }
  await warmUp(context, app, role);
  const state = await context.storageState();
  await context.close();
  return state;
}

export const test = base.extend<TestFixtures, WorkerFixtures>({
  app: [
    async ({}, use, workerInfo) => {
      const app = await startApp(`w${workerInfo.workerIndex}`);
      await use(app);
      await app.stop();
    },
    { scope: "worker", timeout: 180_000 },
  ],

  // Log in once per role per worker, then reuse the cookies.
  loginState: [
    async ({ browser, app }, use) => {
      const cache = new Map<Role, Promise<any>>();
      await use((role) => {
        if (!cache.has(role)) cache.set(role, login(browser, app, role));
        return cache.get(role)!;
      });
    },
    { scope: "worker" },
  ],

  baseURL: async ({ app }, use) => use(app.baseUrl),

  role: ["superadmin", { option: true }],

  // The first login of a worker pays for Chromium start-up plus the app's cold JIT on one shared
  // core, which on its own can eat most of a test's 60 s - so it gets its own budget.
  storageState: [
    async ({ role, loginState }, use) => {
      await use(role === "anonymous" ? undefined : await loginState(role));
    },
    { timeout: 180_000 },
  ],

  guard: [
    async ({ context, app }, use, testInfo: TestInfo) => {
      const guard = new Guard(app.baseUrl);
      context.pages().forEach((p) => guard.watch(p));
      context.on("page", (p) => guard.watch(p));
      await use(guard);
      const problems = guard.report();
      if (problems.length === 0) return;
      await testInfo.attach("guard-problems", { body: problems.join("\n"), contentType: "text/plain" });
      // If the test already failed for its own reasons, don't pile a second failure on top of it.
      if (testInfo.status === testInfo.expectedStatus) {
        expect(problems, "unexpected page problems (JS errors / failed requests / dialogs)").toEqual([]);
      }
    },
    { auto: true },
  ],

  newSession: async ({ browser, app, loginState, guard }, use) => {
    const contexts: BrowserContext[] = [];
    await use(async (role) => {
      const ctx = tune(
        await browser.newContext({
          baseURL: app.baseUrl,
          viewport: { width: 1280, height: 900 },
          storageState: role === "anonymous" ? undefined : await loginState(role),
        })
      );
      contexts.push(ctx);
      ctx.on("page", (p) => guard.watch(p));
      return ctx.newPage();
    });
    await Promise.all(contexts.map((c) => c.close()));
  },
});
