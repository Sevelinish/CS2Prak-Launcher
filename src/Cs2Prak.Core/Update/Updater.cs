using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Cs2Prak.Core.Plugins;

namespace Cs2Prak.Core.Update;

public static partial class Updater
{
    [GeneratedRegex(@"\d+")]
    private static partial Regex Numbers();

    private static string UpdateDir => AppPaths.UpdateDir;

    internal static string StagingDir => Path.Combine(UpdateDir, "staged");
    internal static string BackupDir => Path.Combine(UpdateDir, "backup");
    internal static string ApplyScript => Path.Combine(UpdateDir, "apply_update.cmd");
    private static string ErrorLog => Path.Combine(AppPaths.Root, "update_error.log");

    private static Pending? _pending;

    private sealed record Pending(string Tag, List<string> Changed, string? BundleUrl,
                                  string? BundleSha, Dictionary<string, FileEntry> Files,
                                  Dictionary<string, ReleaseAsset> Assets);

    private sealed record FileEntry(string? Sha256, string? Asset);

    public static string? ReleaseRepo()
    {
        if (InstallMarker.ReleaseRepo() is { Length: > 0 } configured) return configured;
        return AppInfo.UpdateRepo is { Length: > 0 } fallback ? fallback : null;
    }

    public static bool IsStaged =>
        UpdateState.Current.staged && File.Exists(ApplyScript);

    public static void StartCheck() =>
        new Thread(Check) { IsBackground = true, Name = "update-check" }.Start();

    public static void StartDownload() =>
        new Thread(Download) { IsBackground = true, Name = "update-download" }.Start();

    public static void Check()
    {
        var state = UpdateState.Current;

        var repo = ReleaseRepo();
        if (repo is null)
        {
            state.status = "dev";
            return;
        }

        try
        {
            var release = GitHubReleases.Latest(repo, TimeSpan.FromSeconds(10), byVersion: true);
            if (release is null) { state.status = "no-release"; return; }

            var tag = release.TagName.Trim();
            state.latest = tag;
            if (ReleaseVersion.Compare(tag, AppInfo.Version) < 0) { state.status = "up-to-date"; return; }

            var assets = release.Assets.ToDictionary(a => a.Name, StringComparer.Ordinal);
            if (!assets.TryGetValue("manifest.json", out var manifestAsset))
            {
                state.status = "no-manifest";
                return;
            }

            state.status = "checking";
            AppPaths.EnsureDir(UpdateDir);
            var manifestPath = Path.Combine(UpdateDir, "manifest.json");
            DownloadVerified(manifestAsset.DownloadUrl, manifestPath, expectedSha: null);

            if (JsonNode.Parse(File.ReadAllText(manifestPath)) is not JsonObject manifest)
            {
                state.status = "no-manifest";
                return;
            }

            var bundleMeta = manifest["bundle"] as JsonObject;
            var bundleName = bundleMeta?["asset"]?.GetValue<string>();
            ReleaseAsset? bundleAsset = bundleName is not null && assets.TryGetValue(bundleName, out var b)
                ? b : null;

            var files = new Dictionary<string, FileEntry>(StringComparer.Ordinal);
            var changed = new List<string>();
            long size = 0;

            foreach (var (relative, node) in manifest["files"] as JsonObject ?? [])
            {
                var entry = new FileEntry(
                    node?["sha256"]?.GetValue<string>(),
                    node?["asset"]?.GetValue<string>());
                files[relative] = entry;

                var local = Path.Combine(AppPaths.Root, relative.Replace('/', Path.DirectorySeparatorChar));
                if (Sha256OfFile(local) == entry.Sha256) continue;

                changed.Add(relative);

                if (bundleAsset is not null) continue;
                if (entry.Asset is null || !assets.TryGetValue(entry.Asset, out var asset))
                {
                    state.status = "asset-missing";
                    state.message = $"Release is missing the asset for {relative}";
                    return;
                }
                size += asset.Size;
            }

            if (changed.Count == 0)
            {
                state.status = "up-to-date";
                state.available = false;
                return;
            }

            if (bundleAsset is not null) size = bundleAsset.Size;

            _pending = new Pending(tag, changed,
                bundleAsset?.DownloadUrl,
                bundleMeta?["sha256"]?.GetValue<string>(),
                files, assets);

            var sameVersion = ReleaseVersion.Compare(tag, AppInfo.Version) == 0;

            state.available = true;
            state.staged = false;
            state.status = "available";
            state.files = changed;
            state.size = size;
            state.notes = "";
            state.message = sameVersion ? "Update available" : $"Update {tag} available";
        }
        catch (Exception e)
        {
            state.status = "error";
            state.message = e.Message;
        }
    }

    public static void Download()
    {
        var pending = _pending;
        if (pending is null) return;

        var state = UpdateState.Current;
        try
        {
            state.status = "downloading";
            DeleteTree(StagingDir);

            if (pending.BundleUrl is not null)
            {
                var zip = Path.Combine(UpdateDir, "update.zip");
                DownloadVerified(pending.BundleUrl, zip, pending.BundleSha);
                ExtractFromBundle(zip, pending.Changed, StagingDir);
                try { File.Delete(zip); } catch (Exception) { }
            }
            else
            {
                foreach (var relative in pending.Changed)
                {
                    var asset = pending.Assets[pending.Files[relative].Asset!];
                    var target = Path.Combine(StagingDir, relative.Replace('/', Path.DirectorySeparatorChar));
                    DownloadVerified(asset.DownloadUrl, target, pending.Files[relative].Sha256);
                }
            }

            BackupFilesAboutToChange(pending.Changed);
            WriteApplyScript(pending.Changed);

            state.staged = true;
            state.status = "ready";
            state.message = $"Update {pending.Tag} downloaded — restart to install.";
        }
        catch (Exception e)
        {
            state.status = "error";
            state.message = e.Message;
        }
    }

    internal static void DownloadVerified(string url, string destination, string? expectedSha)
    {
        var directory = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(directory)) AppPaths.EnsureDir(directory);

        var part = destination + ".part";
        string actual;

        using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) })
        {
            http.DefaultRequestHeaders.UserAgent.ParseAdd("cs2prak/1.0");
            using var response = http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead)
                                     .GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();

            using var source = response.Content.ReadAsStream();
            using var file = File.Create(part);
            using var sha = SHA256.Create();
            using var hashing = new CryptoStream(file, sha, CryptoStreamMode.Write);
            source.CopyTo(hashing);
            hashing.FlushFinalBlock();
            actual = Convert.ToHexString(sha.Hash!).ToLowerInvariant();
        }

        if (expectedSha is not null && !actual.Equals(expectedSha, StringComparison.OrdinalIgnoreCase))
        {
            try { File.Delete(part); } catch (Exception) { }
            throw new InvalidOperationException(
                $"{Path.GetFileName(destination)} did not match its manifest hash — download rejected.");
        }

        File.Move(part, destination, overwrite: true);
    }

    private static string? Sha256OfFile(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
        }
        catch (Exception) { return null; }
    }

    internal static void ExtractFromBundle(string zipPath, List<string> relatives, string destination)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination));
        AppPaths.EnsureDir(root);

        using var zip = ZipFile.OpenRead(zipPath);
        var byName = zip.Entries.ToDictionary(e => e.FullName, StringComparer.Ordinal);

        var missing = relatives.Where(r => !byName.ContainsKey(r)).Take(3).ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException("update.zip is missing " + string.Join(", ", missing));

        foreach (var relative in relatives)
        {
            var target = Path.GetFullPath(
                Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));

            if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"refusing unsafe path in update.zip: {relative}");

            AppPaths.EnsureDir(Path.GetDirectoryName(target)!);
            byName[relative].ExtractToFile(target, overwrite: true);
        }
    }

    internal static void BackupFilesAboutToChange(List<string> relatives)
    {
        DeleteTree(BackupDir);
        foreach (var relative in relatives)
        {
            var native = relative.Replace('/', Path.DirectorySeparatorChar);
            var source = Path.Combine(AppPaths.Root, native);
            if (!File.Exists(source)) continue;

            var target = Path.Combine(BackupDir, native);
            AppPaths.EnsureDir(Path.GetDirectoryName(target)!);
            File.Copy(source, target, overwrite: true);
        }
    }

    internal static void WriteApplyScript(List<string> relatives)
    {
        var install = AppPaths.Root;
        var exe = Environment.ProcessPath ?? Path.Combine(install, "cs2prak.exe");
        var pid = Environment.ProcessId;
        var log = Path.Combine(UpdateDir, "robocopy.log");

        var lines = new[]
        {
            "@echo off",
            "chcp 65001 >nul",
            ":waitloop",
            $"tasklist /fi \"PID eq {pid}\" 2>nul | find \"{pid}\" >nul",
            "if not errorlevel 1 ( timeout /t 1 /nobreak >nul & goto waitloop )",
            $"robocopy \"{StagingDir}\" \"{install}\" /E /NFL /NDL /NJH /NJS /R:2 /W:2 >\"{log}\"",
            "if errorlevel 8 goto fail",
            $"start \"\" \"{exe}\"",
            "exit /b 0",
            ":fail",
            $"robocopy \"{BackupDir}\" \"{install}\" /E /NFL /NDL /NJH /NJS /R:1 /W:1 >>\"{log}\"",
            $"echo Update failed and was rolled back. robocopy log: {log} >\"{ErrorLog}\"",
            $"start \"\" \"{exe}\"",
            "exit /b 1",
        };

        AppPaths.EnsureDir(UpdateDir);
        File.WriteAllText(ApplyScript, string.Join("\r\n", lines) + "\r\n", new UTF8Encoding(false));
    }

    public static void ApplyStaged()
    {
        if (!File.Exists(ApplyScript)) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd",
                Arguments = $"/c \"{ApplyScript}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }
        catch (Exception) {  }
    }

    private static void DeleteTree(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (Exception) { }
    }
}
