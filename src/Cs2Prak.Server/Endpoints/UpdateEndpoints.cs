using Cs2Prak.Core;
using Cs2Prak.Core.Update;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cs2Prak.Server.Endpoints;

public static class UpdateEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/update/status", () => Results.Json(UpdateState.Current));

        app.MapPost("/api/update/seen", () =>
        {
            UpdateState.Current.seen = true;
            return Results.Json(new { ok = true });
        });

        app.MapPost("/api/update/download", () =>
        {
            if (!UpdateState.Current.available)
                return Results.Json(new { ok = false, message = "No update available" }, statusCode: 400);

            Updater.StartDownload();
            return Results.Json(new { ok = true });
        });

        app.MapPost("/api/update/apply", Apply);
    }

    private static IResult Apply()
    {
        if (!Updater.IsStaged)
            return Results.Json(new { ok = false, message = "Nothing staged" }, statusCode: 400);

        var timer = new System.Timers.Timer(600) { AutoReset = false };
        timer.Elapsed += (_, _) =>
        {
            Cs2ServerProcess.Kill();
            Updater.ApplyStaged();
            AppLifetime.Shutdown();
        };
        timer.Start();

        return Results.Json(new { ok = true });
    }
}
