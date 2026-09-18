using System;
using System.Linq;
using Chalmers.ILL.Configuration;
using Chalmers.ILL.Connections;
using Chalmers.ILL.Mail;
using Chalmers.ILL.MediaItems;
using Chalmers.ILL.OrderItems;
using Chalmers.ILL.Patron;
using Chalmers.ILL.Repositories;
using Chalmers.ILL.Services;
using Chalmers.ILL.Templates;
using Chalmers.ILL.UmbracoApi;
using Microsoft.Extensions.DependencyInjection;

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
            typeof(IChillinOrderConfiguration),
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

        private static bool IsRealValue(string value) =>
            !string.IsNullOrEmpty(value) && !PlaceholderValues.Contains(value);
    }
}
