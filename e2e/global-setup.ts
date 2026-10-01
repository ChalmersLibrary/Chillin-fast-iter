import { execFileSync } from "child_process";
import * as path from "path";

// Build once, up front, so every worker starts the prebuilt app binary (and so a build
// error fails the run with the compiler output instead of a confusing "app not ready").
export default function globalSetup() {
  if (process.env.E2E_SKIP_BUILD) return;
  execFileSync("dotnet", [
      "build",
      path.resolve(__dirname, "../Chalmers.ILL/Chalmers.ILL.csproj"),
      "-c", "Debug",
      "-v", "minimal",
      // Don't leave the Roslyn/MSBuild servers resident (~160 MB+) - the tests need the RAM.
      "-p:UseSharedCompilation=false",
      "-nodeReuse:false",
    ], {
    stdio: "inherit",
  });
  execFileSync("dotnet", ["build-server", "shutdown"], { stdio: "ignore" });
}
