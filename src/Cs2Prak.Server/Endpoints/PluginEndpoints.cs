using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using Cs2Prak.Core;
using Cs2Prak.Core.Plugins;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cs2Prak.Server.Endpoints;

public static class PluginEndpoints
{
    private static readonly ConcurrentDictionary<string, JobRunner> Jobs = new();

    private const string AllJobKey = "__all__";

    private static readonly JobRunner Configure = new();

    public sealed record PluginInfo(
        string id, string name, string description, string github_url,
        bool installed, string? local_version, bool is_dependency);

    public sealed class ToggleBody
    {
        [JsonPropertyName("enabled")] public bool Enabled { get; set; }
    }

    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/plugins", ListPlugins);
        app.MapGet("/api/plugins/latest", LatestVersions);
        app.MapPost("/api/plugins/{pluginId}/download", DownloadPlugin);
        app.MapGet("/api/plugins/{pluginId}/download/status", DownloadStatus);
        app.MapPost("/api/plugins/install-all", InstallAll);

        app.MapGet("/api/plugins/installed", () => Results.Json(InstalledPlugins.List()));
        app.MapPost("/api/plugins/installed/{folder}/toggle", Toggle);

        app.MapPost("/api/configure", ConfigureServer);
        app.MapGet("/api/configure/status", () => Status(Configure));
    }

    private static IResult ListPlugins()
    {
        var state = PluginState.Load();

        var result = PluginCatalog.All.Select(p => new PluginInfo(
            id: p.Id,
            name: p.Name,
            description: p.Description,
            github_url: p.ReleasesUrl,
            installed: p.IsInstalled,
            local_version: PluginState.LocalVersion(p, state),
            is_dependency: p.IsDependency));

        return Results.Json(result);
    }

    private static async Task<IResult> LatestVersions()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(12));

        var lookups = PluginCatalog.All.Select(async p =>
        {
            var release = await GitHubReleases.LatestAsync(
                p.GitHub, TimeSpan.FromSeconds(8), p.GitHubTagPrefix, ct: cts.Token);
            return (p.Id, release?.TagName);
        });

        var results = await Task.WhenAll(lookups);

        var map = new Dictionary<string, string?>();
        foreach (var (id, tag) in results) map[id] = tag;
        return Results.Json(map);
    }

    private static IResult DownloadPlugin(string pluginId, HttpRequest request)
    {
        if (PluginCatalog.Find(pluginId) is null)
            return Results.Json(new { ok = false, message = "Unknown plugin" }, statusCode: 404);

        var osPref = PluginInstaller.NormaliseOs(request.Query["os"]);
        var job = Jobs.GetOrAdd(pluginId, _ => new JobRunner());

        return job.TryStart(log => PluginInstaller.Install(pluginId, log, osPref), $"plugin-{pluginId}")
            ? Results.Json(new { ok = true })
            : Results.Json(new { ok = false, message = "Download already running" });
    }

    private static IResult DownloadStatus(string pluginId) =>
        Jobs.TryGetValue(pluginId, out var job)
            ? Status(job)
            : Results.Json(new { running = false, log = Array.Empty<string>(), exitCode = (int?)null });

    private static IResult InstallAll(HttpRequest request)
    {
        if (!File.Exists(AppPaths.Cs2Exe))
            return Results.Json(new { ok = false, message = "Create the server first." }, statusCode: 400);

        var osPref = PluginInstaller.NormaliseOs(request.Query["os"]);
        var job = Jobs.GetOrAdd(AllJobKey, _ => new JobRunner());

        return job.TryStart(log => PluginInstaller.InstallAll(log, osPref), "plugin-install-all")
            ? Results.Json(new { ok = true })
            : Results.Json(new { ok = false, message = "Auto-install already running" });
    }

    private static IResult Toggle(string folder, ToggleBody? body)
    {
        var result = InstalledPlugins.Toggle(folder, body?.Enabled ?? false);
        return result.Ok
            ? Results.Json(new { ok = true })
            : Results.Json(new { ok = false, message = result.Message }, statusCode: result.Status);
    }

    private static IResult ConfigureServer() =>
        Configure.TryStart(ServerConfigurator.ConfigureServer, "configure")
            ? Results.Json(new { ok = true })
            : Results.Json(new { ok = false, message = "Configure already in progress" });

    private static IResult Status(JobRunner runner) => Results.Json(new
    {
        running = runner.Running,
        log = runner.Log,
        exitCode = runner.ExitCode,
    });
}
