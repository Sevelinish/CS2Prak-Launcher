using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Cs2Prak.Core.Demos;
using Cs2Prak.Core.Faceit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cs2Prak.Server.Endpoints;

public static partial class FaceitEndpoints
{
    [GeneratedRegex("^[0-9]+$")]
    private static partial Regex Digits();

    [GeneratedRegex(@"\d+")]
    private static partial Regex Numbers();

    public sealed class KeyBody
    {
        [JsonPropertyName("key")] public string? Key { get; set; }
    }

    public sealed class ProfileBody
    {
        [JsonPropertyName("profile")] public string? Profile { get; set; }
    }

    public sealed class MatchBody
    {
        [JsonPropertyName("matchId")] public string? MatchId { get; set; }
    }

    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/faceit/key", () => Results.Json(new { set = FaceitApi.Key() is not null }));
        app.MapPost("/api/faceit/key", SetKey);
        app.MapGet("/api/faceit/avatar", Avatar);
        app.MapPost("/api/faceit/matches", Matches);
        app.MapPost("/api/faceit/load", Load);
    }

    private static IResult SetKey(KeyBody? body)
    {
        var result = FaceitApi.SetKey(body?.Key);
        return result.Ok
            ? Results.Json(new { ok = true })
            : Results.Json(new { ok = false, message = result.Message });
    }

    private static IResult Avatar(HttpRequest request)
    {
        var steamId = request.Query["steamid"].ToString().Trim();
        if (steamId.Length == 0 || !Digits().IsMatch(steamId))
            return Results.Json(new { ok = false }, statusCode: 400);

        var profile = FaceitAvatars.Lookup(steamId);
        if (profile is null) return Results.Json(new { ok = false, reason = "no-key" });

        return Results.Json(new
        {
            ok = profile.url.Length > 0,
            profile.url,
            profile.nick,
            profile.lvl,
            profile.elo,
        });
    }

    private static async Task<IResult> Matches(ProfileBody? body)
    {
        var key = FaceitApi.Key();
        if (key is null) return Results.Json(new { ok = false, needKey = true });

        var nickname = FaceitApi.Nickname(body?.Profile);
        if (nickname.Length == 0)
            return Results.Json(new { ok = false, message = "Enter your FACEIT nickname or profile link" });

        JsonObject? player;
        try
        {
            player = FaceitApi.Get("/players", key, new() { ["nickname"] = nickname, ["game"] = "cs2" });
        }
        catch (FaceitHttpException e)
        {
            return e.Status switch
            {
                HttpStatusCode.NotFound =>
                    Results.Json(new { ok = false, message = $"Player \"{nickname}\" not found" }),
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                    Results.Json(new { ok = false, needKey = true }),
                _ => Results.Json(new { ok = false, message = $"FACEIT returned {e.Code}" }),
            };
        }
        catch (Exception)
        {
            return Results.Json(new { ok = false, message = "Could not reach FACEIT" });
        }

        var playerId = player?["player_id"]?.GetValue<string>();

        JsonObject? history;
        try
        {
            history = FaceitApi.Get($"/players/{playerId}/history", key,
                new() { ["game"] = "cs2", ["limit"] = "20" });
        }
        catch (Exception)
        {
            return Results.Json(new { ok = false, message = "Could not load match history" });
        }

        var items = (history?["items"] as JsonArray ?? []).OfType<JsonObject>().ToList();
        var summaries = new JsonObject?[items.Count];

        await Parallel.ForAsync(0, items.Count,
            new ParallelOptions { MaxDegreeOfParallelism = 8 },
            (i, _) =>
            {
                summaries[i] = Summarise(items[i], playerId, key);
                return ValueTask.CompletedTask;
            });

        var matches = new JsonArray();
        foreach (var summary in summaries)
            if (summary is not null) matches.Add(summary);

        return Results.Json(new JsonObject
        {
            ["ok"] = true,
            ["player"] = new JsonObject
            {
                ["nickname"] = player?["nickname"]?.GetValue<string>() ?? nickname,
                ["avatar"] = player?["avatar"]?.GetValue<string>() ?? "",
            },
            ["matches"] = matches,
        });
    }

    private static JsonObject? Summarise(JsonObject item, string? playerId, string key)
    {
        var matchId = item["match_id"]?.GetValue<string>();

        JsonObject? stats;
        try { stats = FaceitApi.Get($"/matches/{matchId}/stats", key); }
        catch (Exception) { return null; }

        if ((stats?["rounds"] as JsonArray)?.FirstOrDefault() is not JsonObject round) return null;

        var roundStats = round["round_stats"] as JsonObject ?? [];
        var score = Numbers().Matches(roundStats["Score"]?.GetValue<string>() ?? "");
        var (scoreA, scoreB) = score.Count >= 2
            ? (int.Parse(score[0].Value), int.Parse(score[1].Value))
            : (0, 0);

        int? kills = null, deaths = null;
        string? playerTeam = null;

        foreach (var team in (round["teams"] as JsonArray ?? []).OfType<JsonObject>())
        {
            foreach (var player in (team["players"] as JsonArray ?? []).OfType<JsonObject>())
            {
                if (player["player_id"]?.GetValue<string>() != playerId) continue;
                var playerStats = player["player_stats"] as JsonObject ?? [];
                kills = ReadInt(playerStats["Kills"]);
                deaths = ReadInt(playerStats["Deaths"]);
                playerTeam = team["team_id"]?.GetValue<string>();
            }
        }

        if (kills is null) return null;

        var finished = item["finished_at"]?.GetValue<long>();

        return new JsonObject
        {
            ["matchId"] = matchId,
            ["map"] = roundStats["Map"]?.GetValue<string>() ?? "",
            ["scoreA"] = scoreA,
            ["scoreB"] = scoreB,
            ["kills"] = kills,
            ["deaths"] = deaths,
            ["kd"] = ((double)kills / Math.Max(1, deaths ?? 0)).ToString("0.00", CultureInfo.InvariantCulture),
            ["win"] = roundStats["Winner"]?.GetValue<string>() == playerTeam,
            ["date"] = finished.HasValue ? finished.Value * 1000 : null,
            ["hasDemo"] = true,
        };
    }

    private static int ReadInt(JsonNode? node) => node switch
    {
        JsonValue v when v.TryGetValue(out long l) => (int)l,
        JsonValue v when v.TryGetValue(out string? s) && int.TryParse(s, out var parsed) => parsed,
        _ => 0,
    };

    private static IResult Load(MatchBody? body)
    {
        var key = FaceitApi.Key();
        if (key is null) return Results.Json(new { ok = false, needKey = true });

        if (!DemoParsing.Available)
            return Results.Json(new { ok = false, message = DemoParsing.Unavailable });

        var matchId = (body?.MatchId ?? "").Trim();
        if (matchId.Length == 0) return Results.Json(new { ok = false, message = "No match selected" });

        JsonObject? match;
        try { match = FaceitApi.Get($"/matches/{matchId}", key); }
        catch (Exception) { return Results.Json(new { ok = false, message = "Could not load match details" }); }

        var urls = DemoUrls(match);
        if (urls.Count == 0)
            return Results.Json(new { ok = false, message = "Demo is not available for this match (expired)" });

        string? signed;
        try
        {
            signed = FaceitApi.DownloadUrl(urls[0], key);
        }
        catch (FaceitHttpException e)
        {
            return e.Status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                ? Results.Json(new
                {
                    ok = false,
                    message = "Your FACEIT key has no Downloads API access — "
                              + "recreate the key with the Downloads scope",
                })
                : Results.Json(new { ok = false, message = $"FACEIT download API error {e.Code}" });
        }
        catch (Exception e)
        {
            return Results.Json(new { ok = false, message = $"Could not get download link: {e.Message}" });
        }

        var stamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var downloaded = Path.Combine(Path.GetTempPath(), $"cs2prak_fc_{stamp}.bin");
        string? raw = null;

        try
        {
            var errors = FaceitApi.DownloadDemo(FaceitApi.DemoHosts(signed ?? urls[0]), downloaded);
            if (errors.Count > 0)
                return Results.Json(new
                {
                    ok = false,
                    message = "Demo download failed — " + string.Join(" | ", errors),
                });

            try
            {
                raw = DemoArchive.ToRawDemo(downloaded);
            }
            catch (Exception e)
            {
                return Results.Json(new { ok = false, message = $"Demo decompress failed: {e.Message}" });
            }

            var parsed = DemoParsing.Run(raw);
            return Results.Json(new JsonObject
            {
                ["ok"] = true,
                ["key"] = parsed.Key,
                ["meta"] = parsed.Meta,
            });
        }
        catch (Exception e)
        {
            return Results.Json(new { ok = false, message = e.Message }, statusCode: 400);
        }
        finally
        {
            Delete(downloaded);
            if (raw is not null && raw != downloaded) Delete(raw);
        }
    }

    private static List<string> DemoUrls(JsonObject? match) => match?["demo_url"] switch
    {
        JsonValue v when v.TryGetValue(out string? s) && IsHttp(s) => [s!],
        JsonArray a => a.OfType<JsonValue>()
            .Select(v => v.TryGetValue(out string? s) ? s : null)
            .Where(IsHttp).Select(s => s!).ToList(),
        _ => [],
    };

    private static bool IsHttp(string? url) =>
        url is not null && url.StartsWith("http", StringComparison.Ordinal);

    private static void Delete(string path)
    {
        try { File.Delete(path); } catch (Exception) { }
    }
}
