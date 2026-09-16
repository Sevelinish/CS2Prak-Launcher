using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Cs2Prak.Core;
using Cs2Prak.Core.Highlights;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cs2Prak.Server.Endpoints;

public static class HighlightEndpoints
{
    private static readonly JobRunner Setup = new();

    private static readonly string[] DemoExtensions = [".dem"];

    public sealed class FindBody
    {
        [JsonPropertyName("demo")] public string? Demo { get; set; }
        [JsonPropertyName("source")] public string? Source { get; set; }
        [JsonPropertyName("kinds")] public string[]? Kinds { get; set; }
        [JsonPropertyName("selection")] public JsonObject? Selection { get; set; }
        [JsonPropertyName("overrides")] public JsonObject? Overrides { get; set; }
    }

    public sealed class DemoBody
    {
        [JsonPropertyName("demo")] public string? Demo { get; set; }
    }

    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/highlights/status", Status);
        app.MapGet("/api/highlights/probe", Probe);
        app.MapPost("/api/highlights/install", Install);
        app.MapGet("/api/highlights/install/status", () => Results.Json(new
        {
            running = Setup.Running,
            log = Setup.Log,
            exitCode = Setup.ExitCode,
        }));

        app.MapPost("/api/highlights/start", Start);
        app.MapPost("/api/highlights/stop", Stop);

        app.MapGet("/api/highlights/demos", Demos);
        app.MapPost("/api/highlights/upload", Upload);
        app.MapPost("/api/highlights/inspect", Inspect);
        app.MapPost("/api/highlights/players", Players);
        app.MapPost("/api/highlights/find", Find);

        app.MapPost("/api/highlights/preview", Preview);
        app.MapPost("/api/highlights/record", Record);
        app.MapGet("/api/highlights/job", () => Results.Json(HighlightJobs.View()));
        app.MapPost("/api/highlights/cancel", () => Results.Json(new
        {
            ok = true,
            job = HighlightJobs.Cancel(),
        }));
        app.MapPost("/api/highlights/reveal", Reveal);
        app.MapGet("/api/highlights/output", () => Call("output.list", new JsonObject(),
                                                        TimeSpan.FromSeconds(30)));
        app.MapGet("/api/highlights/video", Video);

        app.MapGet("/api/highlights/config", ReadConfig);
        app.MapPost("/api/highlights/config", WriteConfig);
    }

    public sealed class PatchBody
    {
        [JsonPropertyName("patch")] public JsonObject? Patch { get; set; }
    }

    private static IResult ReadConfig()
    {
        try
        {
            var described = HighlighterClient.Call("config.describe", null, TimeSpan.FromSeconds(30));
            var current = HighlighterClient.Call("config.get", null, TimeSpan.FromSeconds(30));

            return Results.Json(new JsonObject
            {
                ["ok"] = true,
                ["fields"] = described["fields"]?.DeepClone(),
                ["config"] = current["config"]?.DeepClone(),
                ["configFile"] = current["configFile"]?.DeepClone(),
            });
        }
        catch (HighlighterException e)
        {
            return Results.Json(new { ok = false, code = e.Code, message = e.Message }, statusCode: 400);
        }
        catch (Exception e)
        {
            return Results.Json(new { ok = false, message = e.Message }, statusCode: 500);
        }
    }

    private static IResult WriteConfig(PatchBody? body)
    {
        if (body?.Patch is not { } patch)
            return Results.Json(new { ok = false, message = "Nothing to save." }, statusCode: 400);

        return Call("config.patch", new JsonObject
        {
            ["patch"] = patch.DeepClone(),
            ["persist"] = true,
        }, TimeSpan.FromSeconds(30));
    }

    private static string? _outputRoot;

    private static bool Allowed(string full)
    {
        if (HighlighterInstall.Home() is { } home && FileLinks.IsUnder(full, home)) return true;

        _outputRoot ??= HighlighterClient
            .TryCall("output.list", new JsonObject(), TimeSpan.FromSeconds(30))
            ?["directory"]?.GetValue<string>();

        return _outputRoot is { Length: > 0 } root && FileLinks.IsUnder(full, root);
    }

    private static IResult Video(HttpRequest request)
    {
        var path = request.Query["path"].ToString();

        if (path.Length == 0) return Results.NotFound();

        string full;
        try { full = Path.GetFullPath(path); }
        catch (Exception) { return Results.NotFound(); }

        if (!Allowed(full) || !File.Exists(full)) return Results.NotFound();

        var type = Path.GetExtension(full).ToLowerInvariant() switch
        {
            ".mp4" => "video/mp4",
            ".mkv" => "video/x-matroska",
            ".mov" => "video/quicktime",
            _ => null,
        };
        if (type is null) return Results.NotFound();

        return Results.File(full, type, enableRangeProcessing: true);
    }

    private static JsonObject RecordRequest(FindBody body)
    {
        var request = new JsonObject
        {
            ["demo"] = body.Demo,
            ["source"] = string.Equals(body.Source, "grenades", StringComparison.OrdinalIgnoreCase)
                ? "grenades"
                : "highlights",
            ["selection"] = body.Selection?.DeepClone() ?? new JsonObject(),
        };
        if (body.Overrides is { } overrides) request["overrides"] = overrides.DeepClone();
        return request;
    }

    private static IResult Preview(FindBody? body)
    {
        if (body?.Demo is not { Length: > 0 })
            return Results.Json(new { ok = false, message = "Choose a demo first." }, statusCode: 400);

        return Call("plan.preview", RecordRequest(body), TimeSpan.FromMinutes(4));
    }

    private static IResult Record(FindBody? body)
    {
        if (body?.Demo is not { Length: > 0 })
            return Results.Json(new { ok = false, message = "Choose a demo first." }, statusCode: 400);

        if (HighlightJobs.Busy)
            return Results.Json(new { ok = false, message = "A recording is already running." },
                                statusCode: 409);

        if (Cs2ServerProcess.IsRunning)
            return Results.Json(new
            {
                ok = false,
                message = "Stop your practice server first — HighlighterCS2 needs CS2 to itself.",
            }, statusCode: 409);

        try
        {
            var request = RecordRequest(body);
            request["label"] = "CS2Prak-Launcher";
            return Results.Json(new { ok = true, job = HighlightJobs.Submit(request) });
        }
        catch (HighlighterException e)
        {
            return Results.Json(new { ok = false, code = e.Code, message = e.Message },
                                statusCode: 400);
        }
        catch (Exception e)
        {
            return Results.Json(new { ok = false, message = e.Message }, statusCode: 500);
        }
    }

    private static IResult Reveal(DemoBody? body)
    {
        var payload = new JsonObject();
        if (body?.Demo is { Length: > 0 } demo) payload["demo"] = demo;

        return Call("output.reveal", payload, TimeSpan.FromSeconds(30));
    }

    private static IResult Status()
    {
        var installed = HighlighterInstall.IsInstalled;

        var payload = new JsonObject
        {
            ["installed"] = installed,
            ["version"] = HighlighterInstall.InstalledVersion(),
            ["home"] = HighlighterInstall.Home(),
            ["running"] = HighlighterProcess.IsRunning,
            ["repo"] = $"https://github.com/{HighlighterInstall.GitHub}",
        };

        if (!installed) return Results.Json(payload);

        try
        {
            payload["probe"] = HighlighterClient.Call("system.probe", null, TimeSpan.FromSeconds(30));
            payload["running"] = HighlighterProcess.IsRunning;
        }
        catch (HighlighterException e)
        {
            payload["error"] = e.Message;
            payload["errorCode"] = e.Code;
        }
        catch (Exception e)
        {
            payload["error"] = e.Message;
        }

        return Results.Json(payload);
    }

    private static IResult Probe()
    {
        if (!HighlighterInstall.IsInstalled)
            return Results.Json(new { ok = false, installed = false });

        // A background refresh must never cold-start the plugin: the caller only wants to
        // know whether the tools showed up, and Ensure() would spawn a 24 MB process for it.
        if (!HighlighterProcess.IsRunning)
            return Results.Json(new { ok = false, installed = true, running = false });

        try
        {
            return Results.Json(new JsonObject
            {
                ["ok"] = true,
                ["installed"] = true,
                ["running"] = true,
                ["probe"] = HighlighterClient.Call("system.probe", null, TimeSpan.FromSeconds(30)),
            });
        }
        catch (Exception e)
        {
            return Results.Json(new { ok = false, message = e.Message });
        }
    }

    private static IResult Install()
    {
        if (Setup.Running)
            return Results.Json(new { ok = false, message = "Install already running" });

        HighlighterClient.Shutdown();

        return Setup.TryStart(HighlighterInstall.Install, "highlighter-install")
            ? Results.Json(new { ok = true })
            : Results.Json(new { ok = false, message = "Install already running" });
    }

    private static IResult Start()
    {
        try
        {
            HighlighterProcess.Ensure();
            return Results.Json(new { ok = true, data = HighlighterClient.Handshake() });
        }
        catch (Exception e)
        {
            return Results.Json(new { ok = false, message = e.Message }, statusCode: 500);
        }
    }

    private static IResult Stop()
    {
        HighlighterClient.Shutdown();
        return Results.Json(new { ok = true });
    }

    private static IResult Demos(HttpRequest request)
    {
        var payload = new JsonObject();
        var search = request.Query["search"].ToString();
        if (search.Length > 0) payload["search"] = search;

        return Call("demos.list", payload, TimeSpan.FromSeconds(30));
    }

    private static IResult Inspect(DemoBody? body)
    {
        if (body?.Demo is not { Length: > 0 } demo)
            return Results.Json(new { ok = false, message = "Choose a demo first." }, statusCode: 400);

        return Call("demos.inspect", new JsonObject { ["demo"] = demo }, TimeSpan.FromMinutes(4));
    }

    private static IResult Players(DemoBody? body)
    {
        if (body?.Demo is not { Length: > 0 } demo)
            return Results.Json(new { ok = false, message = "Choose a demo first." }, statusCode: 400);

        return Call("match.players", new JsonObject { ["demo"] = demo }, TimeSpan.FromMinutes(4));
    }

    private static IResult Find(FindBody? body)
    {
        if (body?.Demo is not { Length: > 0 } demo)
            return Results.Json(new { ok = false, message = "Choose a demo first." }, statusCode: 400);

        var grenades = string.Equals(body.Source, "grenades", StringComparison.OrdinalIgnoreCase);

        var payload = new JsonObject
        {
            ["demo"] = demo,
            ["selection"] = body.Selection?.DeepClone() ?? new JsonObject(),
        };

        if (grenades && body.Kinds is { Length: > 0 } kinds)
            payload["kinds"] = new JsonArray(kinds.Select(k => (JsonNode)k!).ToArray());

        return Call(grenades ? "grenades.find" : "highlights.find", payload, TimeSpan.FromMinutes(4));
    }

    private static async Task<IResult> Upload(HttpRequest request)
    {
        var name = Path.GetFileName(request.Query["name"].ToString().Trim());

        if (name.Length == 0 || name.Length > 180)
            return Results.Json(new { ok = false, message = "Bad file name." }, statusCode: 400);

        if (!DemoExtensions.Any(e => name.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
            return Results.Json(
                new { ok = false, message = "HighlighterCS2 records raw .dem files only." },
                statusCode: 400);

        if (HighlighterInstall.Home() is not { } home)
            return Results.Json(new { ok = false, message = "Install HighlighterCS2 first." },
                                statusCode: 400);

        var demos = Path.Combine(home, "demos");
        AppPaths.EnsureDir(demos);

        var target = Path.Combine(demos, name);
        if (!FileLinks.IsUnder(target, demos))
            return Results.Json(new { ok = false, message = "Bad file name." }, statusCode: 400);

        try
        {
            await using (var file = File.Create(target))
                await request.Body.CopyToAsync(file, 1 << 20);
        }
        catch (Exception e)
        {
            return Results.Json(new { ok = false, message = $"Could not save the demo: {e.Message}" },
                                statusCode: 500);
        }

        return Results.Json(new { ok = true, name, path = target });
    }

    private static IResult Call(string command, JsonObject payload, TimeSpan timeout)
    {
        try
        {
            return Results.Json(new JsonObject
            {
                ["ok"] = true,
                ["data"] = HighlighterClient.Call(command, payload, timeout),
            });
        }
        catch (HighlighterException e)
        {
            return Results.Json(new { ok = false, code = e.Code, message = e.Message },
                                statusCode: 400);
        }
        catch (Exception e)
        {
            return Results.Json(new { ok = false, message = e.Message }, statusCode: 500);
        }
    }
}
