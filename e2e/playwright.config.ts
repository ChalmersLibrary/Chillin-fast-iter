import { defineConfig } from "@playwright/test";

// The sandbox/devcontainer is single-core: Chromium + the app compete for one CPU, so everything
// is slow and timing-sensitive. Generous timeouts, one worker, waitUntil "load" (never
// "networkidle" - SignalR keeps a connection open forever).
const executablePath = process.env.CHROMIUM_EXECUTABLE_PATH;

export default defineConfig({
  testDir: "./tests",
  globalSetup: "./global-setup.ts",
  timeout: 240_000,
  expect: { timeout: 30_000 },
  workers: Number(process.env.E2E_WORKERS ?? 1),
  retries: 0, // a flaky e2e test is a finding to understand, not something to paper over
  reporter: [["list"], ["html", { open: "never" }]],
  use: {
    testIdAttribute: "data-testid",
    viewport: { width: 1280, height: 900 },
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
    navigationTimeout: 120_000,
    launchOptions: {
      executablePath,
      // The devcontainer has ~1.8 GB RAM in total, shared with the app and Playwright itself, so
      // Chromium is run as lean as it goes - otherwise page cache thrashes and a login page takes
      // minutes. These flags drop background services and extra processes, not any page behaviour.
      args: [
        "--no-sandbox",
        "--disable-dev-shm-usage",
        "--disable-gpu",
        "--disable-software-rasterizer",
        "--disable-extensions",
        "--disable-background-networking",
        "--disable-component-update",
        "--disable-sync",
        "--disable-default-apps",
        "--no-zygote",
        "--renderer-process-limit=1",
        "--mute-audio",
        // Fewer processes: network service in the browser process, no per-site renderer split.
        "--enable-features=NetworkServiceInProcess",
        "--disable-features=site-per-process,IsolateOrigins,Translate,OptimizationHints,MediaRouter",
        "--js-flags=--max-old-space-size=192",
      ],
    },
  },
});
