using Cs2Prak.Core;
using Cs2Prak.Core.Uninstall;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Text.Json.Serialization;

namespace Cs2Prak.Server.Endpoints;

public static class UninstallEndpoints
{
    private static readonly JobRunner Job = new();

    public sealed class ConfirmBody
    {
        [JsonPropertyName("confirm")] public string? Confirm { get; set; }
    }

    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/uninstall/preview", Preview);
        app.MapPost("/api/uninstall", Start);
        app.MapGet("/api/uninstall/status", Status);
    }

    private static IResult Preview()
    {
        var blocked = Uninstaller.Blocked();

        var items = Uninstaller.Targets().Select(target =>
        {
            var exists = Path.Exists(target.path) || FileLinks.IsLink(target.path);
            return new
            {
                target.key,
                target.path,
                target.kind,
                exists,
                target.nested,
                size = exists && !target.nested ? Uninstaller.SizeOnDisk(target.path) : 0,
            };
        }).ToList();

        return Results.Json(new
        {
            ok = blocked is null,
            blocked,
            confirm = Uninstaller.ConfirmToken,
            items,
            total = items.Sum(i => i.size),
        });
    }

    private static IResult Start(ConfirmBody? body)
    {
        if (Uninstaller.Blocked() is { } blocked)
            return Results.Json(new { ok = false, message = blocked }, statusCode: 403);

        if (body?.Confirm != Uninstaller.ConfirmToken)
            return Results.Json(new { ok = false, message = "Missing confirmation." }, statusCode: 400);

        if (!Job.TryStart(Uninstaller.Run, "uninstall"))
            return Results.Json(new { ok = false, message = "Uninstall already running" }, statusCode: 409);

        _ = Task.Run(async () =>
        {
            while (Job.Running) await Task.Delay(200);
            if (Job.ExitCode != 0) return;

            await Task.Delay(1200);
            Uninstaller.LaunchScript();
            AppLifetime.Shutdown();
        });

        return Results.Json(new { ok = true });
    }

    private static IResult Status() => Results.Json(new
    {
        running = Job.Running,
        done = Job.ExitCode is not null,
        error = Job.ExitCode is not null and not 0 ? Job.Log.LastOrDefault() : null,
        log = Job.Log,
    });
}
