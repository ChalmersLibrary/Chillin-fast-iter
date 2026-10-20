using System;
using System.Linq;
using Claudeborgen;
using Chalmers.ILL.Configuration;
using Chalmers.ILL.Connections;
using Chalmers.ILL.Mail;
using Chalmers.ILL.MediaItems;
using Chalmers.ILL.OrderItems;
using Chalmers.ILL.Patron;
using Chalmers.ILL.Repositories;
using Chalmers.ILL.Services;
using Chalmers.ILL.Templates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Chalmers.ILL.Isolated
{
    // Guardrail #2 from fas 6, isolerat läge steg A: crash rather than run wrong. Run from both
    // Bootstrapper (at startup) and tests (IsolationGuardTest), so the same allowlist backs both -
    // an allowlist ages better than an enumeration of "today's fake types" would, since any new seam
    // just needs adding to SeamInterfaces to be covered.
    public static class IsolationGuard
    {
        // Every interface Bootstrapper branches on between Live and Isolated registrations.
        public static readonly Type[] SeamInterfaces =
        {
            typeof(IMailWebApi),
            typeof(IMediaItemManager),
            typeof(IOrderItemSearcher),
            typeof(ITemplateService),
            typeof(IChillinTextRepository),
            typeof(IPatronDataProvider),
            typeof(IAffiliationDataProvider),
            typeof(IPersonDataProvider),
            typeof(IFolioConnection),
            typeof(IFolioItemService),
            typeof(IFolioRepository),
            typeof(IFolioService),
            typeof(IFolioInstanceService),
            typeof(IFolioHoldingService),
            typeof(IFolioCirculationService),
            typeof(IFolioUserService),
        };

        // "A real secret was cloned from production" detector, not "any value at all" - appsettings.json
        // already uses "******"/"xxx" as explicit not-a-real-value placeholders (fas 6), so those (and
        // null/empty) don't count as filled in.
        private static readonly (string Name, Func<IChillinConfiguration, string> Get)[] SecretKeys =
        {
            ("MicrosoftGraphClientSecret", c => c.MicrosoftGraphClientSecret),
            ("ChalmersIllExchangePassword", c => c.ChalmersIllExchangePassword),
            ("FolioPassword", c => c.FolioPassword),
            ("PatronCacheSolrBasicAuthPassword", c => c.PatronCacheSolrBasicAuthPassword),
            ("LibPSearchApiKey", c => c.LibPSearchApiKey),
        };

        private static readonly string[] PlaceholderValues = { "******", "xxx" };

        public static void Verify(IServiceProvider services, IChillinConfiguration config)
        {
            foreach (var seam in SeamInterfaces)
            {
                var instance = services.GetRequiredService(seam);
                var isIsolatedType = instance.GetType().Namespace == typeof(IsolationGuard).Namespace;

                if (config.Isolated && !isIsolatedType)
                {
                    throw new InvalidOperationException(
                        $"Isolerat läge är påslaget men {seam.Name} löser ut {instance.GetType().FullName}, " +
                        $"inte en typ i {typeof(IsolationGuard).Namespace} - en riktig integration skulle ha använts.");
                }

                if (!config.Isolated && isIsolatedType)
                {
                    throw new InvalidOperationException(
                        $"Isolerat läge är avstängt men {seam.Name} löser ut {instance.GetType().FullName}, " +
                        $"en fejk ur {typeof(IsolationGuard).Namespace} - den skulle inte vara registrerad i Live.");
                }
            }

            VerifyClaudeborgen(services, config);

            if (config.Isolated)
            {
                var filledInSecrets = SecretKeys
                    .Where(k => IsRealValue(k.Get(config)))
                    .Select(k => k.Name)
                    .ToList();

                if (filledInSecrets.Count > 0)
                {
                    throw new InvalidOperationException(
                        "Isolerat läge är påslaget men följande hemlighetsnycklar är ifyllda med riktiga " +
                        "värden: " + string.Join(", ", filledInSecrets) + ". Det troligaste felet är att " +
                        "produktionens App Settings klonats till den här appen - isolerat läge ska aldrig " +
                        "köra med riktiga hemligheter tillgängliga.");
                }
            }
        }

        // Claudeborgen: the real channel (agent API to a real runner) must never be open on the
        // isolated fast test server, and the fakes must never run anywhere else - see
        // claudeborgen/DESIGN.md, "Kanalen och testservern" / "Fejkad Claude". Not a seam interface
        // like the ones above (the fakes live in the Claudeborgen package, not in this namespace),
        // so checked on its own.
        private static void VerifyClaudeborgen(IServiceProvider services, IChillinConfiguration config)
        {
            var settings = services.GetService<ClaudeborgenSettings>();
            if (settings == null)
                return;

            var inAppFakeRunning = services.GetServices<IHostedService>().OfType<FakeClaude>().Any();

            if (config.Isolated && !settings.IsFake)
            {
                throw new InvalidOperationException(
                    $"Isolerat läge är påslaget men Claudeborgen är i läge {settings.Mode} - agent-API:t mot en " +
                    "riktig runner får aldrig vara öppet på den isolerade testservern.");
            }

            if (!config.Isolated && settings.IsFake)
            {
                throw new InvalidOperationException(
                    $"Isolerat läge är avstängt men Claudeborgen är i fejkläget {settings.Mode} - fejkerna ska bara köra i isolerat läge.");
            }

            if (inAppFakeRunning != (settings.Mode == ClaudeborgenMode.InAppFake))
            {
                throw new InvalidOperationException(
                    $"Claudeborgen är i läge {settings.Mode} men FakeClaude {(inAppFakeRunning ? "körs" : "körs inte")} i appen.");
            }
        }

        private static bool IsRealValue(string value) =>
            !string.IsNullOrEmpty(value) && !PlaceholderValues.Contains(value);
    }
}
