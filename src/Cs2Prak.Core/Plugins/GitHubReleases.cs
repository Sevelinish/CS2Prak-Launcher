using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Cs2Prak.Core.Plugins;

public sealed record ReleaseAsset(string Name, string DownloadUrl, long Size);

public sealed record Release(string TagName, IReadOnlyList<ReleaseAsset> Assets, string Body);

public static class GitHubReleases
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("cs2prak/1.0");
        return http;
    }

    public static async Task<Release?> LatestAsync(string repo, TimeSpan timeout,
                                                   string? tagPrefix = null,
                                                   bool byVersion = false,
                                                   CancellationToken ct = default)
    {
        if (tagPrefix is null && !byVersion)
        {
            var one = await GetJsonAsync($"https://api.github.com/repos/{repo}/releases/latest", timeout, ct);
            if (one is JsonObject obj && Parse(obj) is { Assets.Count: > 0 } release) return release;
        }

        var listed = await GetJsonAsync($"https://api.github.com/repos/{repo}/releases?per_page=100", timeout, ct);
        if (listed is not JsonArray array) return null;

        var stable = new List<(Release Release, string Published)>();
        var pre = new List<(Release Release, string Published)>();

        foreach (var node in array)
        {
            if (node is not JsonObject rel) continue;
            if (rel["draft"]?.GetValue<bool>() == true) continue;

            var parsed = Parse(rel);
            if (parsed is null || parsed.Assets.Count == 0) continue;

            if (tagPrefix is not null &&
                !parsed.TagName.TrimStart('v').StartsWith(tagPrefix, StringComparison.Ordinal))
                continue;

            var published = rel["published_at"]?.GetValue<string>() ?? "";
            var isPre = rel["prerelease"]?.GetValue<bool>() == true;
            (isPre ? pre : stable).Add((parsed, published));
        }

        var pool = stable.Count > 0 ? stable : pre;
        if (pool.Count == 0) return null;

        var ordered = byVersion
            ? pool.OrderByDescending(x => x.Release.TagName, ReleaseVersion.Comparer)
                  .ThenByDescending(x => x.Published, StringComparer.Ordinal)
            : pool.OrderByDescending(x => x.Published, StringComparer.Ordinal);

        return ordered.First().Release;
    }

    public static Release? Latest(string repo, TimeSpan timeout, string? tagPrefix = null,
                                  bool byVersion = false) =>
        LatestAsync(repo, timeout, tagPrefix, byVersion).GetAwaiter().GetResult();

    private static async Task<JsonNode?> GetJsonAsync(string url, TimeSpan timeout, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            using var response = await Http.GetAsync(url, cts.Token);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadFromJsonAsync<JsonNode>(cts.Token);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Release? Parse(JsonObject rel)
    {
        var tag = rel["tag_name"]?.GetValue<string>();
        if (tag is null) return null;

        var assets = new List<ReleaseAsset>();
        if (rel["assets"] is JsonArray list)
        {
            foreach (var node in list)
            {
                if (node is not JsonObject a) continue;
                var name = a["name"]?.GetValue<string>();
                var url = a["browser_download_url"]?.GetValue<string>();
                if (name is null || url is null) continue;
                assets.Add(new ReleaseAsset(name, url, a["size"]?.GetValue<long>() ?? 0));
            }
        }
        return new Release(tag, assets, rel["body"]?.GetValue<string>()?.Trim() ?? "");
    }

    public static ReleaseAsset? PickAsset(PluginDef plugin, Release release, string osPref)
    {
        var excludes = plugin.AssetNameExclude.Select(x => x.ToLowerInvariant()).ToArray();
        var prefer = plugin.AssetNamePrefer?.ToLowerInvariant();

        bool Allowed(ReleaseAsset a) =>
            !excludes.Any(ex => a.Name.Contains(ex, StringComparison.OrdinalIgnoreCase));

        bool Preferred(ReleaseAsset a) =>
            prefer is null || a.Name.Contains(prefer, StringComparison.OrdinalIgnoreCase);

        bool ForOs(ReleaseAsset a) =>
            a.Name.Contains(osPref, StringComparison.OrdinalIgnoreCase);

        string[] exts = osPref == "linux"
            ? [".tar.gz", ".tgz", ".zip"]
            : [".zip", ".tar.gz", ".tgz"];

        foreach (var pass in new Func<ReleaseAsset, bool>[]
                 {
                     a => ForOs(a) && Preferred(a) && Allowed(a),
                     a => ForOs(a) && Allowed(a),
                     Allowed,
                 })
        {
            foreach (var ext in exts)
            {
                var hit = release.Assets.FirstOrDefault(
                    a => a.Name.EndsWith(ext, StringComparison.OrdinalIgnoreCase) && pass(a));
                if (hit is not null) return hit;
            }
        }
        return null;
    }

    public static void Download(string url, string dest)
    {
        var dir = Path.GetDirectoryName(dest);
        if (!string.IsNullOrEmpty(dir)) AppPaths.EnsureDir(dir);

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        using var response = Http
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token)
            .GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();

        using var stream = response.Content.ReadAsStream(cts.Token);
        using var file = File.Create(dest);
        stream.CopyTo(file);
    }
}
