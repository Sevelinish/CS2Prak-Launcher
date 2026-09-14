using System.Text.Json;
using System.Text.Json.Nodes;
using Cs2Prak.Core.Plugins;

namespace Cs2Prak.Core.Highlights;

public static class HighlighterInstall
{
    public const string GitHub = "Sevelinish/CS2Prak-HighlightMaker";
    public const string ExeName = "HighlighterCS2.exe";

    private const string AssetName = "HighlighterCS2.zip";
    private const string KeepSuffix = ".cs2prak-keep";

    private static readonly string[] Preserve = ["config.json"];
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private static string TempDir => Path.Combine(Path.GetTempPath(), "cs2prak_highlighter");

    public static string Root => AppPaths.HighlighterDir;

    public static string? Exe()
    {
        if (!Directory.Exists(Root)) return null;

        var direct = Path.Combine(Root, ExeName);
        if (File.Exists(direct)) return direct;

        try
        {
            foreach (var dir in Directory.EnumerateDirectories(Root))
            {
                var nested = Path.Combine(dir, ExeName);
                if (File.Exists(nested)) return nested;
            }
        }
        catch (Exception) { }

        return null;
    }

    public static string? Home()
    {
        var exe = Exe();
        return exe is null ? null : Path.GetDirectoryName(exe);
    }

    public static bool IsInstalled => Exe() is not null;

    public static string? InstalledVersion()
    {
        try
        {
            return (JsonNode.Parse(File.ReadAllText(AppPaths.HighlighterState)) as JsonObject)
                ?["version"]?.GetValue<string>();
        }
        catch (Exception) { return null; }
    }

    private static void RecordVersion(string tag)
    {
        try
        {
            var state = new JsonObject { ["version"] = tag, ["repo"] = GitHub };
            File.WriteAllText(AppPaths.HighlighterState, state.ToJsonString(Indented));
        }
        catch (Exception) { }
    }

    public static int Install(JobLog log)
    {
        log.Add("Fetching the latest HighlighterCS2 release…");
        var release = GitHubReleases.Latest(GitHub, TimeSpan.FromSeconds(15))
                      ?? throw new InvalidOperationException(
                          "GitHub is unreachable or the repository has no release with assets.");

        var asset = release.Assets.FirstOrDefault(
                        a => a.Name.Equals(AssetName, StringComparison.OrdinalIgnoreCase))
                    ?? release.Assets.FirstOrDefault(
                        a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException(
                        $"Release {release.TagName} carries no zip to install.");

        log.Add($"Latest: {release.TagName} → {asset.Name} ({asset.Size / 1048576} MB)");

        AppPaths.EnsureDir(TempDir);
        var archive = Path.Combine(TempDir, asset.Name);

        log.Add("Downloading (this one is large, give it a minute)…");
        GitHubReleases.Download(asset.DownloadUrl, archive);

        log.Add("Installing…");
        AppPaths.EnsureDir(Root);

        var home = Home();
        var kept = home is null ? [] : SetAside(home, log);

        try
        {
            Archives.ExtractSafely(archive, Root);
        }
        finally
        {
            if (home is not null) PutBack(home, kept);
            try { File.Delete(archive); } catch (Exception) { }
        }

        if (Exe() is not { } exe)
            throw new InvalidOperationException(
                $"{ExeName} was not found under {Root} after extracting.");

        RecordVersion(release.TagName);
        log.Add($"[+] HighlighterCS2 {release.TagName} installed at {Path.GetDirectoryName(exe)}");
        log.Add("HLAE and ffmpeg are downloaded by the plugin itself the first time you record.");
        return 0;
    }

    private static List<string> SetAside(string home, JobLog log)
    {
        var kept = new List<string>();
        foreach (var relative in Preserve)
        {
            var source = Path.Combine(home, relative);
            if (!File.Exists(source)) continue;

            var aside = source + KeepSuffix;
            try
            {
                File.Move(source, aside, overwrite: true);
                kept.Add(relative);
                log.Add($"  · kept your {relative}");
            }
            catch (Exception) { }
        }
        return kept;
    }

    private static void PutBack(string home, List<string> kept)
    {
        foreach (var relative in kept)
        {
            var target = Path.Combine(home, relative);
            var aside = target + KeepSuffix;
            if (!File.Exists(aside)) continue;

            try { File.Move(aside, target, overwrite: true); }
            catch (Exception) { }
        }
    }

    public static string? LatestVersion()
    {
        try
        {
            return GitHubReleases.Latest(GitHub, TimeSpan.FromSeconds(10))?.TagName;
        }
        catch (Exception) { return null; }
    }
}
