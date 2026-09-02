using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Cs2Prak.Core.Faceit;

public sealed class FaceitHttpException(HttpStatusCode status)
    : Exception($"FACEIT returned {(int)status}")
{
    public HttpStatusCode Status { get; } = status;
    public int Code => (int)Status;
}

public static partial class FaceitApi
{
    private const string DataApi = "https://open.faceit.com/data/v4";
    private const string DownloadApi = "https://open.faceit.com/download/v2/demos/download";

    private static string KeyFile => Path.Combine(AppPaths.Root, "faceit_key.txt");

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    [GeneratedRegex(@"/players/([^/?#]+)", RegexOptions.IgnoreCase)]
    private static partial Regex ProfilePath();

    public static string? Key()
    {
        try
        {
            var key = File.ReadAllText(KeyFile).Trim();
            return key.Length > 0 ? key : null;
        }
        catch (Exception) { return null; }
    }

    public sealed record KeyResult(bool Ok, string? Message);

    public static KeyResult SetKey(string? candidate)
    {
        var key = (candidate ?? "").Trim();
        if (key.Length == 0)
        {
            try { File.Delete(KeyFile); } catch (Exception) { }
            return new KeyResult(true, null);
        }

        try
        {
            Get("/players", key, new() { ["nickname"] = "donk", ["game"] = "cs2" });
        }
        catch (FaceitHttpException e)
        {
            return e.Status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                ? new KeyResult(false, "Invalid key — use a Server-side API key")
                : new KeyResult(false, $"FACEIT returned {e.Code}");
        }
        catch (Exception)
        {
            return new KeyResult(false, "Could not reach FACEIT — check your connection");
        }

        try
        {
            File.WriteAllText(KeyFile, key, new UTF8Encoding(false));
        }
        catch (Exception e)
        {
            return new KeyResult(false, $"Could not save key: {e.Message}");
        }
        return new KeyResult(true, null);
    }

    public static JsonObject? Get(string path, string key, Dictionary<string, string>? query = null,
                                  int timeoutSeconds = 20)
    {
        var url = DataApi + path;
        if (query is { Count: > 0 })
        {
            url += "?" + string.Join("&", query.Select(
                kv => Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(kv.Value)));
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + key);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var response = Http.Send(request, cts.Token);
        if (!response.IsSuccessStatusCode) throw new FaceitHttpException(response.StatusCode);

        using var stream = response.Content.ReadAsStream(cts.Token);
        return JsonNode.Parse(stream) as JsonObject;
    }

    public static string Nickname(string? profile)
    {
        var text = (profile ?? "").Trim();
        var match = ProfilePath().Match(text);
        if (match.Success) return Uri.UnescapeDataString(match.Groups[1].Value);
        return text.Split('?')[0].Trim('/');
    }

    public static string? DownloadUrl(string resourceUrl, string key)
    {
        var body = new JsonObject { ["resource_url"] = resourceUrl }.ToJsonString();

        using var request = new HttpRequestMessage(HttpMethod.Post, DownloadApi)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + key);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var response = Http.Send(request, cts.Token);
        if (!response.IsSuccessStatusCode) throw new FaceitHttpException(response.StatusCode);

        using var stream = response.Content.ReadAsStream(cts.Token);
        return (JsonNode.Parse(stream) as JsonObject)?["payload"]?["download_url"]?.GetValue<string>();
    }

    private static readonly Dictionary<string, string> B2Regions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["demos-europe-central"] = "eu-central-003",
        ["demos-eu-central"] = "eu-central-003",
        ["demos-europe-west"] = "eu-central-003",
        ["demos-us-east"] = "us-east-005",
        ["demos-us-west"] = "us-west-002",
    };

    private const string VanitySuffix = ".backblaze.faceit-cdn.net";

    public static List<string> DemoHosts(string url)
    {
        var candidates = new List<string>();

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return [url];
        var host = uri.Host;

        if (host.EndsWith(VanitySuffix, StringComparison.OrdinalIgnoreCase))
        {
            var name = host[..^VanitySuffix.Length];
            var bucket = name + "-faceit-cdn";
            var region = RegionFromCredential(uri.Query) ?? B2Regions.GetValueOrDefault(name);

            if (region is not null)
            {
                var afterHost = url[(url.IndexOf(host, StringComparison.Ordinal) + host.Length)..];
                candidates.Add($"{uri.Scheme}://{bucket}.s3.{region}.backblazeb2.com{afterHost}");
            }
        }

        candidates.Add(url);
        return candidates;
    }

    private static string? RegionFromCredential(string query)
    {
        foreach (var pair in query.TrimStart('?').Split('&'))
        {
            var split = pair.IndexOf('=');
            if (split < 0) continue;
            if (!pair.AsSpan(0, split).Equals("X-Amz-Credential", StringComparison.OrdinalIgnoreCase))
                continue;

            var parts = Uri.UnescapeDataString(pair[(split + 1)..]).Split('/');
            return parts.Length >= 3 ? parts[2] : null;
        }
        return null;
    }

    public static List<string> DownloadDemo(IEnumerable<string> candidates, string destination)
    {
        var errors = new List<string>();

        foreach (var url in candidates)
        {
            var host = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : "?";
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");

                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                using var response = Http.Send(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                response.EnsureSuccessStatusCode();

                using var source = response.Content.ReadAsStream(cts.Token);
                using var file = File.Create(destination);
                source.CopyTo(file, 1 << 20);

                return [];
            }
            catch (Exception e)
            {
                errors.Add($"{host}: {e.Message}");
            }
        }

        return errors;
    }
}
