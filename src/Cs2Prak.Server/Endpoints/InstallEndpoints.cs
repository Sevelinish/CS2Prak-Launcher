using System.Diagnostics;
using System.Text.Json.Serialization;
using Cs2Prak.Core;
using Cs2Prak.Core.Uninstall;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cs2Prak.Server.Endpoints;

public static class InstallEndpoints
{
    private const string RemoveToken = "REMOVE";

    private static readonly JobRunner Install = new();

    private static readonly JobRunner Update = new();

    private static readonly JobRunner Remove = new();

    public sealed class RemoveBody
    {
        [JsonPropertyName("confirm")] public string? Confirm { get; set; }
    }

    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/server/install", () => Start(Install, SteamCmd.InstallServer,
                                                       "server-install", "Install already in progress"));
        app.MapPost("/api/server/use-existing", () => Start(Install, Overlay.BuildFromExisting,
                                                            "server-overlay", "Operation already in progress"));
        app.MapPost("/api/server/rebuild", Rebuild);
        app.MapGet("/api/server/install/status", () => Status(Install));

        app.MapPost("/update", () => Start(Update, SteamCmd.UpdateServer,
                                           "server-update", "Update already in progress"));
        app.MapGet("/update/status", () => Status(Update));

        app.MapGet("/api/server/update-check", UpdateCheck);
        app.MapPost("/api/open-csgo", OpenCsgo);

        app.MapGet("/api/server/remove/preview", RemovePreview);
        app.MapPost("/api/server/remove", RemoveServer);
        app.MapGet("/api/server/remove/status", () => Status(Remove));

        app.MapGet("/api/server/health", () => Results.Json(new
        {
            issues = ServerHealth.Check(repair: false),
        }));
    }

    private static IResult RemovePreview() => Results.Json(new
    {
        installed = Directory.Exists(AppPaths.ServerRoot),
        path = AppPaths.ServerRoot,
        size = Uninstaller.ServerSize(),
        running = Cs2ServerProcess.IsRunning,
        confirm = RemoveToken,
    });

    private static IResult RemoveServer(RemoveBody? body)
    {
        if (!Directory.Exists(AppPaths.ServerRoot))
            return Results.Json(new { ok = false, message = "No server to remove." }, statusCode: 400);

        if (body?.Confirm != RemoveToken)
            return Results.Json(new { ok = false, message = "Missing confirmation." }, statusCode: 400);

        if (Install.Running)
            return Results.Json(new { ok = false, message = "Operation already in progress" });

        return Start(Remove, Uninstaller.RemoveServer, "server-remove", "Removal already running");
    }

    private static IResult Start(JobRunner runner, Func<JobLog, int> work, string name, string busy) =>
        runner.TryStart(work, name)
            ? Results.Json(new { ok = true })
            : Results.Json(new { ok = false, message = busy });

    private static IResult Rebuild()
    {
        if (Install.Running)
            return Results.Json(new { ok = false, message = "Operation already in progress" });

        if (!File.Exists(AppPaths.Cs2Exe))
            return Results.Json(new { ok = false, message = "Create the server first." }, statusCode: 400);

        return Start(Install, Overlay.Rebuild, "server-rebuild", "Operation already in progress");
    }

    private static IResult Status(JobRunner runner) => Results.Json(new
    {
        running = runner.Running,
        log = runner.Log,
        exitCode = runner.ExitCode,
    });

    private static IResult UpdateCheck()
    {
        var installed = File.Exists(AppPaths.Cs2Exe);
        return Results.Json(new
        {
            installed,
            outdated = installed && Overlay.IsStale(),
            current = Overlay.RetailBuildId(),
            built = Overlay.BuiltBuildId(),
        });
    }

    private static IResult OpenCsgo()
    {
        if (!Directory.Exists(AppPaths.CsgoBase))
            return Results.Json(new { ok = false, message = "CS2 server not installed yet." }, statusCode: 400);

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer",
                Arguments = $"\"{AppPaths.CsgoBase}\"",
                UseShellExecute = false,
            });
        }
        catch (Exception e)
        {
            return Results.Json(new { ok = false, message = e.Message }, statusCode: 500);
        }
        return Results.Json(new { ok = true });
    }
}
