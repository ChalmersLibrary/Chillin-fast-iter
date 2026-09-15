using System;
using System.Threading.Tasks;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chalmers.ILL.Tests
{
    // Confirms Playwright can actually launch the devcontainer's system Chromium (see
    // .devcontainer/Dockerfile) - the first, smallest possible check at the "brytpunkt efter
    // fas 2" (TODO-remove-dotnet-framework.md). Deliberately not testing the app itself yet -
    // that needs the app running as a separate process, real integration-test territory, not
    // appropriate for this fast unit test run. Follow-up work: a harness that starts the app and
    // screenshots login/start page for use throughout fas 3-8.
    //
    // Skips (Inconclusive, not Failed) rather than breaking `dotnet test` wherever Chromium isn't
    // installed - e.g. this session's container, before the Dockerfile change here is applied and
    // rebuilt.
    [TestClass]
    public class BrowserSmokeTest
    {
        [TestMethod]
        public async Task Chromium_CanLaunchAndNavigateToBlankPage()
        {
            var executablePath = Environment.GetEnvironmentVariable("CHROMIUM_EXECUTABLE_PATH");
            if (string.IsNullOrEmpty(executablePath) || !System.IO.File.Exists(executablePath))
            {
                Assert.Inconclusive("CHROMIUM_EXECUTABLE_PATH is not set or the binary doesn't exist - " +
                    "devcontainer needs a rebuild with chromium in .devcontainer/Dockerfile.");
                return;
            }

            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                ExecutablePath = executablePath,
                Args = new[] { "--no-sandbox" }
            });

            var page = await browser.NewPageAsync();
            await page.GotoAsync("about:blank");

            Assert.AreEqual("about:blank", page.Url);
        }
    }
}
