using System.Globalization;
using System.IO.Compression;
using System.Net.Mime;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Cs2Prak.Core;
using Cs2Prak.Core.Demos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cs2Prak.Server.Endpoints;

public static class DemoEndpoints
{
    private static readonly string[] DemoExtensions = [".dem", ".dem.gz", ".dem.zst", ".gz", ".zst"];

    public sealed class TeleportBody
    {
        [JsonPropertyName("sp")] public double[]? Position { get; set; }
        [JsonPropertyName("sa")] public double[]? Angles { get; set; }
        [JsonPropertyName("kind")] public string? Kind { get; set; }
        [JsonPropertyName("who")] public string? Who { get; set; }
    }

    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/demo/upload", Upload);
        app.MapPost("/api/demo/enqueue", Enqueue);

        app.MapGet("/api/demo/queue", () => Results.Json(new { queue = ParseQueue.Snapshot() }));
        app.MapPost("/api/demo/queue/clear", () =>
        {
            ParseQueue.ClearFinished();
            return Results.Json(new { ok = true });
        });

        app.MapGet("/api/demo/library", () => Results.Json(new { library = DemoLibrary.List() }));
        app.MapDelete("/api/demo/library/{key}", Forget);

        app.MapGet("/api/demo/data/{key}", Data);
        app.MapGet("/api/demo/stats/{key}", Stats);
        app.MapGet("/api/demo/voice/{key}/{clip}.wav", Voice);

        app.MapPost("/api/demo/advanced/upload", AdvancedUpload);
        app.MapGet("/api/demo/advanced/analyze", AdvancedAnalyze);

        app.MapPost("/api/demo/nade-export", ExportTeleport);
    }

    private static async Task<IResult> Upload(HttpRequest request)
    {
        var name = request.Query["name"].ToString().ToLowerInvariant();
        if (name.Length == 0) name = "demo.dem";
        if (!DemoExtensions.Any(e => name.EndsWith(e, StringComparison.Ordinal)))
            return Results.Json(
                new { ok = false, message = "Please choose a .dem (or .dem.gz / .dem.zst) file" },
                statusCode: 400);

        if (!DemoParsing.Available)
            return Results.Json(new { ok = false, message = DemoParsing.Unavailable }, statusCode: 400);

        var staged = Path.Combine(Path.GetTempPath(),
            $"cs2prak_upload_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}.bin");

        string? raw = null;
        try
        {
            await Receive(request.Body, staged);

            try { raw = DemoArchive.ToRawDemo(staged); }
            catch (Exception e)
            {
                return Results.Json(new { ok = false, message = $"Could not read demo: {e.Message}" },
                                    statusCode: 400);
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
            Delete(staged);
            if (raw is not null && raw != staged) Delete(raw);
        }
    }

    private static async Task<IResult> Enqueue(HttpRequest request)
    {
        var name = request.Query["name"].ToString();
        if (name.Length == 0) name = "demo.dem";
        var lower = name.ToLowerInvariant();

        if (!DemoExtensions.Append(".zip").Any(e => lower.EndsWith(e, StringComparison.Ordinal)))
            return Results.Json(new { ok = false, message = "Drop .dem / .dem.gz / .dem.zst / .zip" },
                                statusCode: 400);

        AppPaths.EnsureDir(ParseQueue.StageDir);
        var stamp = ParseQueue.NextStageName();
        var staged = Path.Combine(ParseQueue.StageDir, $"{stamp}.bin");
        await Receive(request.Body, staged);

        if (!lower.EndsWith(".zip", StringComparison.Ordinal))
        {
            ParseQueue.Enqueue(name, staged);
            return Results.Json(new { ok = true, queued = new[] { name } });
        }

        var queued = new List<string>();
        try
        {
            using var zip = ZipFile.OpenRead(staged);
            foreach (var entry in zip.Entries)
            {
                if (entry.Name.Length == 0) continue;
                var entryName = entry.FullName.ToLowerInvariant();
                if (!entryName.EndsWith(".dem", StringComparison.Ordinal)
                    && !entryName.EndsWith(".dem.gz", StringComparison.Ordinal)
                    && !entryName.EndsWith(".dem.zst", StringComparison.Ordinal)) continue;

                var extracted = Path.Combine(ParseQueue.StageDir, $"{stamp}_{queued.Count}.bin");
                entry.ExtractToFile(extracted, overwrite: true);
                ParseQueue.Enqueue(entry.Name, extracted);
                queued.Add(entry.Name);
            }
        }
        catch (Exception e)
        {
            Delete(staged);
            return Results.Json(new { ok = false, message = $"Bad zip: {e.Message}" }, statusCode: 400);
        }

        Delete(staged);
        return queued.Count == 0
            ? Results.Json(new { ok = false, message = "No demos found in the zip" }, statusCode: 400)
            : Results.Json(new { ok = true, queued });
    }

    private static async Task Receive(Stream body, string destination)
    {
        await using var file = File.Create(destination);
        await body.CopyToAsync(file, 1 << 20);
    }

    private static IResult Forget(string key)
    {
        if (!DemoCache.IsSafeKey(key)) return Results.Json(new { ok = false }, statusCode: 400);
        DemoLibrary.Remove(key);
        return Results.Json(new { ok = true });
    }

    private static IResult Data(string key)
    {
        if (!DemoCache.IsSafeKey(key)) return Results.Json(new { ok = false }, statusCode: 404);

        var path = DemoCache.DataPath(key);
        if (!File.Exists(path))
            return Results.Json(new { ok = false, message = "Not parsed" }, statusCode: 404);

        return Results.File(path, MediaTypeNames.Application.Json);
    }

    private static IResult Stats(string key)
    {
        if (!DemoCache.IsSafeKey(key)) return Results.Json(new { ok = false }, statusCode: 404);

        var path = DemoCache.DataPath(key);
        if (!File.Exists(path))
            return Results.Json(new { ok = false, message = "Not parsed" }, statusCode: 404);

        JsonNode blob;
        using (var stream = File.OpenRead(path))
        {
            if (JsonNode.Parse(stream) is not { } parsed)
                return Results.Json(new { ok = false, message = "Not parsed" }, statusCode: 404);
            blob = parsed;
        }

        var rounds = blob["rounds"]?.AsArray() ?? [];
        var last = rounds.Count > 0 ? rounds[^1] : null;

        return Results.Json(new
        {
            ok = true,
            map = blob["map"]?.GetValue<string>(),
            scout = ScoutMetrics.Build(blob),
            players = blob["players"],
            stats = blob["stats"],
            teamA = blob["teamA"],
            teamB = blob["teamB"],
            teamAName = blob["teamAName"],
            teamBName = blob["teamBName"],
            rounds = rounds.Count,
            sa = Score(last, "sa"),
            sb = Score(last, "sb"),
        });
    }

    private static int Score(JsonNode? round, string field) =>
        round?[field] is JsonValue value && value.TryGetValue<double>(out var v) ? (int)v : 0;

    private static IResult Voice(string key, string clip)
    {
        if (!DemoCache.IsSafeKey(key) || !int.TryParse(clip, out var n) || n < 0)
            return Results.Json(new { ok = false }, statusCode: 404);

        var path = Path.Combine(DemoCache.VoiceDir(key), $"{n}.wav");
        return File.Exists(path)
            ? Results.File(path, "audio/wav")
            : Results.Json(new { ok = false, message = "No voice clip" }, statusCode: 404);
    }

    private static async Task<IResult> AdvancedUpload(HttpRequest request)
    {
        var name = request.Query["name"].ToString().ToLowerInvariant();
        if (name.Length == 0) name = "demo.dem";
        if (!DemoExtensions.Any(e => name.EndsWith(e, StringComparison.Ordinal)))
            return Results.Json(
                new { ok = false, message = "Choose a .dem (.dem.gz / .dem.zst ok)" },
                statusCode: 400);

        var upload = Path.Combine(Path.GetTempPath(),
            $"cs2prak_adv_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.bin");

        string? raw = null;
        try
        {
            await Receive(request.Body, upload);

            try { raw = DemoArchive.ToRawDemo(upload); }
            catch (Exception e)
            {
                return Results.Json(new { ok = false, message = $"Could not read demo: {e.Message}" },
                                    statusCode: 400);
            }

            var id = AdvancedStaging.Stage(raw);
            var staged = AdvancedStaging.Find(id);
            if (staged is null)
                return Results.Json(new { ok = false, message = "Could not stage demo" },
                                    statusCode: 400);

            var (map, players) = AdvancedStaging.Roster(staged);
            return Results.Json(new
            {
                ok = true,
                id,
                map,
                players = players.Select(p => new
                {
                    steamid = p.SteamId, name = p.Name, team = p.Team, clan = p.Clan,
                }),
            });
        }
        catch (Exception e)
        {
            return Results.Json(new { ok = false, message = $"Parse failed: {e.Message}" },
                                statusCode: 400);
        }
        finally
        {
            Delete(upload);

            if (raw is not null && raw != upload && File.Exists(raw)) Delete(raw);
        }
    }

    private static IResult AdvancedAnalyze(HttpRequest request)
    {
        var id = request.Query["id"].ToString().Trim();
        var steamId = request.Query["steamid"].ToString().Trim();

        if (steamId.Length is 0 or > 32 || !steamId.All(char.IsAsciiDigit))
            return Results.Json(new { ok = false, message = "Bad request" }, statusCode: 400);

        if (AdvancedStaging.Find(id) is not { } staged)
            return Results.Json(new { ok = false, message = "Demo expired — re-upload it" },
                                statusCode: 404);

        try
        {
            return Results.Json(AdvancedAnalysis.Analyze(staged, steamId));
        }
        catch (Exception e)
        {
            return Results.Json(new { ok = false, message = $"Analysis failed: {e.Message}" },
                                statusCode: 500);
        }
    }

    private static readonly Dictionary<string, (string File, string Label)> TeleportConfigs = new()
    {
        ["nade"] = ("expNade.cfg", "nade lineup"),
        ["pos"] = ("expPos.cfg", "player position"),
    };

    private static IResult ExportTeleport(TeleportBody? body)
    {
        var (fileName, label) = TeleportConfigs.GetValueOrDefault(body?.Kind ?? "",
                                                                 TeleportConfigs["nade"]);

        if (body?.Position is not { Length: 3 } position || body.Angles is not { Length: 2 } angles)
            return Results.Json(new { ok = false, message = "No lineup data" }, statusCode: 400);

        if (position.Concat(angles).Any(v => double.IsNaN(v) || double.IsInfinity(v)))
            return Results.Json(new { ok = false, message = "Bad lineup data" }, statusCode: 400);

        var command = fileName[..^4];
        var who = ConsoleSafe(body.Who);

        var content = new StringBuilder()
            .Append("sv_cheats 1\n")
            .Append(CultureInfo.InvariantCulture, $"setpos {N(position[0])} {N(position[1])} {N(position[2])}\n")
            .Append(CultureInfo.InvariantCulture, $"setang {N(angles[0])} {N(angles[1])} 0\n")
            .Append($"echo \"[cs2prak] teleported to {label}")
            .Append(who.Length > 0 ? $" - {who}" : "")
            .Append($" (exec {command})\"\n")
            .ToString();

        var cfgDir = SteamLocator.FindClientCfgDir();
        if (cfgDir is null)
            return Results.Json(new { ok = false, message = "CS2 cfg folder not found" }, statusCode: 404);

        try
        {
            var path = Path.Combine(cfgDir, fileName);
            File.WriteAllText(path, content.Replace("\n", "\r\n"), new UTF8Encoding(false));
            return Results.Json(new { ok = true, path, cmd = command });
        }
        catch (Exception e)
        {
            return Results.Json(new { ok = false, message = e.Message }, statusCode: 500);
        }
    }

    private static string N(double value)
    {
        var text = value.ToString("R", CultureInfo.InvariantCulture);
        return text.AsSpan().IndexOfAny('.', 'E', 'e') < 0 && !text.Contains('∞')
            ? text + ".0"
            : text;
    }

    private static string ConsoleSafe(string? who)
    {
        var trimmed = (who ?? "");
        if (trimmed.Length > 48) trimmed = trimmed[..48];

        var kept = new string(trimmed.Where(c => c >= 32 && c < 127 && c != '"' && c != '\\').ToArray());
        return string.Join(' ', kept.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static void Delete(string path)
    {
        try { File.Delete(path); } catch (Exception) { }
    }
}
