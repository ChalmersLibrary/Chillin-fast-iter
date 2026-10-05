import { ChildProcess, spawn } from "child_process";
import * as fs from "fs";
import * as net from "net";
import * as os from "os";
import * as path from "path";
import { writeMembersFile } from "./members";

const APP_DIR = path.resolve(__dirname, "../../Chalmers.ILL");
// The built apphost, started directly. `dotnet run` would add a ~70-120 MB wrapper process that
// does nothing but wait, which matters on a ~1.8 GB devcontainer shared with Chromium.
const APP_BINARY = path.join(APP_DIR, "bin/Debug/net10.0/Chalmers.ILL");

export interface RunningApp {
  baseUrl: string;
  /** Per-run throwaway data root: orders/, members.json, mail outbox etc. Safe to read in tests. */
  dataPath: string;
  stop(): Promise<void>;
  /**
   * Stops and starts the app again on the same port and the same data, leaving browser windows
   * open. For the reconnect half of the SignalR check: SignalR's withAutomaticReconnect should
   * pick the connection back up on its own.
   */
  restart(): Promise<void>;
}

function freePort(): Promise<number> {
  return new Promise((resolve, reject) => {
    const srv = net.createServer();
    srv.listen(0, "127.0.0.1", () => {
      const port = (srv.address() as net.AddressInfo).port;
      srv.close(() => resolve(port));
    });
    srv.on("error", reject);
  });
}

async function waitUntilReady(baseUrl: string, proc: ChildProcess, logFile: string, timeoutMs: number) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    if (proc.exitCode !== null) {
      throw new Error(`App exited early (code ${proc.exitCode}). Log:\n${fs.readFileSync(logFile, "utf8").slice(-3000)}`);
    }
    try {
      const res = await fetch(baseUrl + "/ChalmersILLLoginPage", { redirect: "manual" });
      if (res.status === 200) return;
    } catch {
      /* not listening yet */
    }
    await new Promise((r) => setTimeout(r, 500));
  }
  throw new Error(`App not ready after ${timeoutMs} ms. Log:\n${fs.readFileSync(logFile, "utf8").slice(-3000)}`);
}

/**
 * Starts the already-built app (see global-setup.ts) in isolated mode against a fresh DataPath.
 * Isolated mode seeds the demo orders itself at startup (DevDataSeeder); accounts come from here.
 * Everything - orders, the in-memory search index, locks - lives in this one process, so a test
 * that needs pristine state starts its own app rather than trying to reset a shared one.
 */
export async function startApp(label: string): Promise<RunningApp> {
  const port = await freePort();
  return startAppOnPort(label, port);
}

async function startAppOnPort(label: string, port: number, existingDataPath?: string): Promise<RunningApp> {
  // Must be "localhost": ChalmersILL.cshtml/ChalmersILLLoginPage.cshtml redirect any other host to
  // https://<LiveServer> (see the isNotLocalhost check). 127.0.0.1 gets you a 302 to "https://xxx".
  const baseUrl = `http://localhost:${port}`;
  const dataPath = existingDataPath ?? fs.mkdtempSync(path.join(os.tmpdir(), `chillin-e2e-${label}-`));
  writeMembersFile(dataPath);

  const logFile = path.join(dataPath, "app.log");
  const log = fs.openSync(logFile, "a");
  const proc = spawn(APP_BINARY, [], {
    cwd: APP_DIR, // content root - wwwroot/Views are resolved relative to it
    detached: true, // own process group, so stop() reliably takes the whole thing down
    stdio: ["ignore", log, log],
    env: {
      ...process.env,
      ASPNETCORE_ENVIRONMENT: "Development",
      ASPNETCORE_URLS: baseUrl,
      Chillin__Isolated: "true",
      Chillin__DataPath: dataPath,
      Chillin__BaseUrl: baseUrl + "/",
      // The start page's "NY BESTÄLLNING!"-form and the poll button (the chilli icon at the
      // bottom left) are how an order is created through the real path: mail -> inbox -> poll.
      // They are hidden unless this is on, and appsettings.json leaves it off.
      Chillin__ShowManualMailFetchingTools: "true",
      // Must be a real value, not appsettings.json's "******" placeholder: it is what marks a
      // message as addressed to Chillin's own mailbox, which is what FileMailWebApi delivers
      // back to the inbox instead of only filing under sentitems.
      Chillin__ChalmersIllSenderAddress: "chillin@isolated.invalid",
      Chillin__MicrosoftGraphApiUserId: "chillin@isolated.invalid",
      // Without this, a processed mail stays in the inbox and the *next* poll turns it into a
      // second order - every poll re-imports everything still lying there. appsettings.json
      // defaults it to false, which is why two createOrderThroughMail() calls on the same app
      // produced a duplicate of the first order. See SCENARIOS.md, "Öppna frågor".
      Chillin__ChalmersIllArchiveProcessedMails: "true",
    },
  });

  const app: RunningApp = {
    baseUrl,
    dataPath,
    async restart() {
      await stopProcess();
      // Same port, same data, same members: to the open browser windows this is the one server
      // going away and coming back, which is what the reconnect check needs.
      const again = await startAppOnPort(label, port, dataPath);
      app.stop = again.stop;
      app.restart = again.restart;
    },
    async stop() {
      await stopProcess();
      if (!process.env.E2E_KEEP_DATA) fs.rmSync(dataPath, { recursive: true, force: true });
    },
  };

  async function stopProcess() {
    if (proc.exitCode === null && proc.pid) {
      try {
        process.kill(-proc.pid, "SIGTERM");
      } catch {
        /* already gone */
      }
      await new Promise((r) => setTimeout(r, 500));
    }
    fs.closeSync(log);
  }

  try {
    await waitUntilReady(baseUrl, proc, logFile, 90_000);
  } catch (e) {
    // The fixture never gets an app to stop() if startup throws - don't leak the process.
    if (proc.pid) try { process.kill(-proc.pid, "SIGTERM"); } catch { /* already gone */ }
    throw e;
  }
  return app;
}
