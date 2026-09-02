using System.Net;
using Cs2Prak.Core;
using Cs2Prak.Server.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Cs2Prak.Server;

public static class ApiHost
{
    private static readonly HashSet<string> LoopbackHosts =
        new(StringComparer.OrdinalIgnoreCase) { "127.0.0.1", "localhost", "[::1]", "::1" };

    public static WebApplication Create()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = AppPaths.Root,
        });

        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.None);

        builder.WebHost.UseKestrel(o =>
        {
            o.Listen(IPAddress.Loopback, AppInfo.Port);
            o.AddServerHeader = false;
            o.Limits.MaxRequestBodySize = null;
        });

        builder.Services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.PropertyNamingPolicy = null;
            o.SerializerOptions.DictionaryKeyPolicy = null;
        });

        var app = builder.Build();

        app.Use(GuardLoopbackOnly);

        MapAssets(app);

        PageEndpoints.Map(app);
        ServerEndpoints.Map(app);
        InstallEndpoints.Map(app);
        PluginEndpoints.Map(app);
        SkinsEndpoints.Map(app);
        BindsEndpoints.Map(app);
        FaceitEndpoints.Map(app);
        UninstallEndpoints.Map(app);
        DemoEndpoints.Map(app);
        UpdateEndpoints.Map(app);

        return app;
    }

    private static async Task GuardLoopbackOnly(HttpContext ctx, RequestDelegate next)
    {
        var host = ctx.Request.Host.Host;
        if (!LoopbackHosts.Contains(host))
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            await ctx.Response.WriteAsync("Forbidden");
            return;
        }

        var method = ctx.Request.Method;
        if (method is not ("GET" or "HEAD" or "OPTIONS"))
        {
            var origin = ctx.Request.Headers.Origin.ToString();
            if (!string.IsNullOrEmpty(origin))
            {
                var originHost = Uri.TryCreate(origin, UriKind.Absolute, out var u) ? u.Host : "";
                if (!LoopbackHosts.Contains(originHost))
                {
                    ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await ctx.Response.WriteAsync("Forbidden");
                    return;
                }
            }
        }

        await next(ctx);
    }

    private static void MapAssets(WebApplication app)
    {
        if (Directory.Exists(AppPaths.StaticDir))
        {
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(AppPaths.StaticDir),
                RequestPath = "/static",
                ServeUnknownFileTypes = false,
            });
        }

        if (Directory.Exists(AppPaths.MapsDir))
        {
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(AppPaths.MapsDir),
                RequestPath = "/maps",
                ServeUnknownFileTypes = false,
            });
        }
    }

    public static async Task<bool> WaitUntilReady(TimeSpan timeout)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var r = await http.GetAsync(AppInfo.HomeUrl);
                if ((int)r.StatusCode < 500) return true;
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }
            await Task.Delay(150);
        }
        return false;
    }
}
