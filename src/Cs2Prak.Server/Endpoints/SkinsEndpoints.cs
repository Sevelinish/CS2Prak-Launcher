using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Cs2Prak.Core.Skins;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cs2Prak.Server.Endpoints;

public static partial class SkinsEndpoints
{
    [GeneratedRegex("^[0-9]{1,32}$")]
    private static partial Regex SteamId();

    public sealed class HltvBody
    {
        [JsonPropertyName("url")] public string? Url { get; set; }
    }

    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/catalogue/skins", () => Catalogue(() => Catalogues.SkinsByWeapon));
        app.MapGet("/api/catalogue/gloves", () => Catalogue(() => Catalogues.Gloves));
        app.MapGet("/api/catalogue/agents", () => Catalogue(() => Catalogues.Agents));
        app.MapGet("/api/catalogue/stickers", () => Catalogue(() => Catalogues.Stickers));

        app.MapGet("/api/player/{steamid}", GetPlayer);
        app.MapPost("/api/player/{steamid}/save", SavePlayer);

        app.MapPost("/api/hltv/skins", Hltv);
    }

    private static IResult Catalogue(Func<JsonNode> pick)
    {
        Catalogues.RefreshIfChanged();
        return Results.Json(pick());
    }

    private static IResult GetPlayer(string steamid)
    {
        if (!SteamId().IsMatch(steamid))
            return Results.Json(new { ok = false, message = "Invalid SteamID" }, statusCode: 400);

        try
        {
            return Results.Json(PlayerLoadout.Read(steamid));
        }
        catch (Exception e)
        {
            return Results.Json(new { ok = false, message = e.Message }, statusCode: 500);
        }
    }

    private static async Task<IResult> SavePlayer(string steamid, HttpRequest request)
    {
        if (!SteamId().IsMatch(steamid))
            return Results.Json(new { ok = false, message = "Invalid SteamID" }, statusCode: 400);

        try
        {
            var body = await JsonNode.ParseAsync(request.Body) as JsonObject ?? [];
            PlayerLoadout.Save(steamid, body);
            return Results.Json(new { ok = true });
        }
        catch (Exception e)
        {
            return Results.Json(new { ok = false, message = e.Message }, statusCode: 500);
        }
    }

    private static IResult Hltv(HltvBody? body)
    {
        var result = HltvImport.FromUrl(body?.Url?.Trim());
        if (!result.Ok) return Results.Json(new { ok = false, reason = result.Reason });

        return Results.Json(new JsonObject
        {
            ["ok"] = true,
            ["player"] = result.Player,
            ["count"] = result.Count,
            ["matched"] = result.Matched,
            ["unmatched"] = result.Unmatched,
        });
    }
}
