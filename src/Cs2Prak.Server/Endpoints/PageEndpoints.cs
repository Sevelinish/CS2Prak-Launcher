using System.Net.Mime;
using Cs2Prak.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cs2Prak.Server.Endpoints;

public static class PageEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/", Index);
        app.MapPost("/heartbeat", Heartbeat);
    }

    private static IResult Index()
    {
        var html = File.ReadAllText(AppPaths.IndexHtml);
        return Results.Content(html, MediaTypeNames.Text.Html);
    }

    private static IResult Heartbeat()
    {
        LastHeartbeat = DateTime.UtcNow;
        return Results.Json(new { ok = true });
    }

    public static DateTime LastHeartbeat = DateTime.UtcNow;
}
