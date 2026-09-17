using Chalmers.ILL;
using Chalmers.ILL.Members;
using Chalmers.ILL.OrderItems;
using Chalmers.ILL.SignalR;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using System;
using System.IO;

// Replaces Global.asax.cs (Application_Start) and EventHandlers/OwinStartup.cs (Startup.Configuration)
// with a single minimal-hosting entry point (fas 2). OwinStartup did three things:
// - DbMigrator (EF6 migrations) - gone entirely now that fas 7 removed the database.
// - Bootstrapper.Initialise(), which set up MVC's DependencyResolver - now builder.Services
//   directly (see Bootstrapper.cs).
// - app.MapSignalR() - now app.MapHub<NotificationHub>(...) below.
// Application_Start's log4net config, filter/route/view-engine registration are wired in below too.

// The parameterless XmlConfigurator.Configure() discovers its config via Web.config's
// <log4net configSource="..."> section - which Kestrel never reads (that was already
// questionable under System.Web, since it relied on ConfigurationManager resolving against the
// *entry* assembly's config file). Pointed at the file explicitly instead.
log4net.Config.XmlConfigurator.Configure(new FileInfo(Path.Combine(AppContext.BaseDirectory, "Config", "log4net.config")));

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    // Web.config's <requestLimits maxAllowedContentLength> and <httpRuntime maxRequestLength>
    // were both 1 GB (consistent, if accidentally so). Kestrel's default is only 30 MB, which
    // would silently break uploads of larger attachments (fas 2).
    options.Limits.MaxRequestBodySize = 1024L * 1024 * 1024;
});

Bootstrapper.RegisterTypes(builder.Services);

builder.Services.AddHttpContextAccessor();

builder.Services.AddSingleton<FileMembershipProvider>();
builder.Services.AddSingleton<FileRoleProvider>();

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        // Web.config's <authentication mode="Forms" loginUrl="~/ChalmersILLLoginPage" ...> had
        // neither requireSSL nor a real cookie name ("yourAuthCookie" - an obvious template
        // placeholder). Fixed here rather than carried over, per fas 3.
        options.LoginPath = "/ChalmersILLLoginPage";
        options.Cookie.Name = "ChalmersILLAuth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
    });
builder.Services.AddAuthorization();

builder.Services.AddControllersWithViews(FilterConfig.RegisterGlobalFilters);
builder.Services.Configure<RazorViewEngineOptions>(ViewEngineConfig.RegisterViewEngines);

builder.Services.AddSignalR().AddJsonProtocol(options =>
{
    // Classic SignalR 2 serialized OrderItemNotification (NodeId, EditedBy, ...) as PascalCase,
    // and chalmers.ill.js still reads value.NodeId etc. ASP.NET Core SignalR defaults to
    // camelCase, which would silently break the client with no error - see fas 5.
    options.PayloadSerializerOptions.PropertyNamingPolicy = null;
});

var app = builder.Build();

// Notifier needs IHubContext<NotificationHub>, only available once SignalR is registered and
// the app is built - see the comment in Bootstrapper.RegisterTypes.
if (app.Services.GetRequiredService<Chalmers.ILL.OrderItems.IOrderItemManager>() is FileOrderItemManager fileOrderItemManager)
{
    fileOrderItemManager.SetNotifier(app.Services.GetRequiredService<Chalmers.ILL.SignalR.INotifier>());
}

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Error");
}

// SystemSurfaceController's cron-server IP check reads Connection.RemoteIpAddress - behind
// Azure App Service's front-end that's the proxy's IP unless X-Forwarded-For is folded in here
// first (fas 2: "HTTP_X_FORWARDED_FOR -> ForwardedHeaders-middleware, inte manuell header-parsning").
// Fas 10: the default KnownProxies/KnownNetworks only trust loopback, but App Service's front-end
// is neither loopback nor a known, stable address - left at the default, the middleware silently
// rejects the header and RemoteIpAddress stays the front-end's own address, which denies the cron
// server (and any real client IP check) without any error anywhere. Cleared per the standard
// App Service pattern; the port on "ip:port" is still stripped correctly, that parsing is the
// middleware's own, not something done here.
var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};
forwardedHeadersOptions.KnownNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeadersOptions);

app.UseStaticFiles();
// Assets still live under Scripts/, Css/, images/ (project root) rather than wwwroot/ - moving
// them is fas 5's asset-strategy item. Served explicitly here in the meantime.
foreach (var (path, requestPath) in new[] { ("Scripts", "/Scripts"), ("Css", "/Css"), ("images", "/images") })
{
    var full = System.IO.Path.Combine(builder.Environment.ContentRootPath, path);
    if (System.IO.Directory.Exists(full))
    {
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(full),
            RequestPath = requestPath
        });
    }
}

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

RouteConfig.RegisterRoutes(app);
app.MapHub<NotificationHub>("/notificationHub");

app.Run();

// Makes the top-level statements' implicit Program class visible to
// WebApplicationFactory<Program> in the test project (fas 6, isolerat läge steg A) - it's
// internal by default.
public partial class Program { }
