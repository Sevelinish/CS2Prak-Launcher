using System.Text.Json.Nodes;
using Cs2Prak.Core.Binds;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cs2Prak.Server.Endpoints;

public static class BindsEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/binds/catalog", () => Results.Json(BindsGenerator.Catalog));
        app.MapPost("/api/binds/generate", Generate);
    }

    private static async Task<IResult> Generate(HttpRequest request)
    {
        var body = await JsonNode.ParseAsync(request.Body) as JsonObject ?? [];
        var result = BindsGenerator.Generate(body["binds"] as JsonArray ?? []);

        return Results.Json(new
        {
            ok = true,
            count = result.Count,
            written = result.Written,
            path = result.Path,
            content = result.Content,
        });
    }
}
