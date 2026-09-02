using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Cs2Prak.Core;
using Cs2Prak.Core.Plugins;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cs2Prak.Server.Endpoints;

public static partial class ServerEndpoints
{
    public sealed class LaunchBody
    {
        [JsonPropertyName("map")] public string? Map { get; set; }
    }

    public sealed class AdminBody
    {
        [JsonPropertyName("steamid")] public string? SteamId { get; set; }
    }

    private static readonly JsonSerializerOptions AdminJson = new() { WriteIndented = true };

    [GeneratedRegex(@"^7656119\d{10}$")]
    private static partial Regex SteamId64();

    public static Action? BeforeLaunch;

    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/launch", Launch);
        app.MapPost("/stop", Stop);
        app.MapGet("/status", Status);
        app.MapGet("/api/server/status", ServerStatus);
        app.MapGet("/api/server/connect-info", ConnectInfo);
        app.MapGet("/api/skins/ready", SkinsReady);
        app.MapGet("/api/admin", AdminGet);
        app.MapPost("/api/admin", AdminSet);
    }

    private static IResult Launch(LaunchBody? body)
    {
        if (Cs2ServerProcess.IsRunning)
            return Results.Json(new { ok = false, message = "Server already running" }, statusCode: 400);

        if (!File.Exists(AppPaths.Cs2Exe))
            return Results.Json(
                new { ok = false, message = "CS2 server not installed. Use the Download tab first." },
                statusCode: 400);

        var map = body?.Map ?? "de_dust2";
        if (!Cs2ServerProcess.IsValidMapName(map))
            return Results.Json(new { ok = false, message = "Invalid map name" }, statusCode: 400);

        BeforeLaunch?.Invoke();

        var error = Cs2ServerProcess.Launch(map);
        return error is null
            ? Results.Json(new { ok = true, message = $"Server launched on {map}" })
            : Results.Json(new { ok = false, message = error });
    }

    private static IResult Stop() =>
        Cs2ServerProcess.Kill()
            ? Results.Json(new { ok = true })
            : Results.Json(new { ok = false, message = "Server not running" }, statusCode: 400);

    private static IResult Status() => Results.Json(new { running = Cs2ServerProcess.IsRunning });

    private static IResult ServerStatus() => Results.Json(new { installed = File.Exists(AppPaths.Cs2Exe) });

    private static IResult ConnectInfo() => Results.Json(new { ip = LocalIp(), port = 27015 });

    private static IResult AdminGet()
    {
        try
        {
            if (JsonNode.Parse(File.ReadAllText(AppPaths.AdminsJson)) is JsonObject admins)
            {
                foreach (var (_, value) in admins)
                {
                    if (value is not JsonObject entry) continue;
                    if (entry["identity"]?.GetValue<string>() is { Length: > 0 } identity)
                        return Results.Json(new { steamid = identity });
                }
            }
        }
        catch (Exception) { }

        return Results.Json(new { steamid = "" });
    }

    private static async Task<IResult> AdminSet(HttpRequest request)
    {
        var body = await request.ReadFromJsonAsync<AdminBody>();
        var steamId = (body?.SteamId ?? "").Trim();

        if (!SteamId64().IsMatch(steamId))
            return Results.Json(new { ok = false, message = "Invalid SteamID64" }, statusCode: 400);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AppPaths.AdminsJson)!);

            var admins = new JsonObject
            {
                ["Server Admin"] = new JsonObject
                {
                    ["identity"] = steamId,
                    ["flags"] = new JsonArray("@css/root"),
                },
            };
            File.WriteAllText(AppPaths.AdminsJson, admins.ToJsonString(AdminJson));

            return Results.Json(new { ok = true });
        }
        catch (Exception e)
        {
            return Results.Json(new { ok = false, message = e.Message }, statusCode: 500);
        }
    }

    private static IResult SkinsReady()
    {
        var server = File.Exists(AppPaths.Cs2Exe);
        var weaponpaints = PluginCatalog.Find("weaponpaints")?.IsInstalled ?? false;

        return Results.Json(new
        {
            server,
            weaponpaints,
            ready = server && weaponpaints,
        });
    }

    private static string LocalIp()
    {
        try
        {
            using var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            s.Connect("8.8.8.8", 80);
            return (s.LocalEndPoint as System.Net.IPEndPoint)?.Address.ToString() ?? "127.0.0.1";
        }
        catch (SocketException) { return "127.0.0.1"; }
        catch (ObjectDisposedException) { return "127.0.0.1"; }
    }
}
