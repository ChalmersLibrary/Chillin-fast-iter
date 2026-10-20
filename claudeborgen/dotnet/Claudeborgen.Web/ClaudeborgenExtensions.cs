using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

namespace Claudeborgen
{
    // How a host app plugs Claudeborgen in (DESIGN.md, "Gränsen mot värdappen"):
    //
    //   var settings = ClaudeborgenSettings.FromConfiguration(configuration, isolated, dataPath);
    //   services.AddClaudeborgen(settings);
    //   services.AddSingleton<IClaudeborgenHost>(new MyHost(...));
    //   ...
    //   app.MapClaudeborgen();
    //
    // plus rendering "~/Views/Claudeborgen/_StartPageSection.cshtml" (and, if wanted,
    // "~/Views/Claudeborgen/_Changelog.cshtml") where the app's changelog lives.
    public static class ClaudeborgenExtensions
    {
        public const string HubPath = "/claudeborgenHub";

        // Registers instances rather than types, so every ServiceProvider built from `services`
        // shares the one ClaudeborgenService (heartbeat state lives in it).
        public static ClaudeborgenService AddClaudeborgen(this IServiceCollection services, ClaudeborgenSettings settings)
        {
            var service = new ClaudeborgenService(new ConversationStore(settings.DataDirectory), settings);
            services.AddSingleton(settings);
            services.AddSingleton(service);

            if (settings.Mode == ClaudeborgenMode.InAppFake)
            {
                services.AddSingleton(sp => new FakeClaude(service, sp.GetService<Microsoft.Extensions.Logging.ILogger<FakeClaude>>()));
                services.AddHostedService(sp => sp.GetRequiredService<FakeClaude>());
            }

            return service;
        }

        // Call after the app is built: the hub's IHubContext only exists then.
        public static IEndpointRouteBuilder MapClaudeborgen(this IEndpointRouteBuilder endpoints)
        {
            var services = endpoints.ServiceProvider;
            services.GetRequiredService<ClaudeborgenService>().SetBroadcaster(
                new SignalRClaudeborgenBroadcaster(services.GetRequiredService<IHubContext<ClaudeborgenHub>>()));
            endpoints.MapHub<ClaudeborgenHub>(HubPath);
            return endpoints;
        }
    }
}
