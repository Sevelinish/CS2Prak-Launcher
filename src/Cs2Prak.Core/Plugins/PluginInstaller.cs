namespace Cs2Prak.Core.Plugins;

public static class PluginInstaller
{
    private static string TempDir => Path.Combine(Path.GetTempPath(), "cs2prak_plugins");
    private static string BackupDir => Path.Combine(Path.GetTempPath(), "cs2prak_pluginbak");

    public static readonly string[] OsChoices = ["windows", "linux"];

    public static string NormaliseOs(string? value)
    {
        var os = (value ?? "").ToLowerInvariant();
        return OsChoices.Contains(os) ? os : "windows";
    }

    public static int Install(string pluginId, JobLog log, string osPref = "windows") =>
        Install(pluginId, log, osPref, []);

    private static int Install(string pluginId, JobLog log, string osPref, HashSet<string> chain)
    {
        var plugin = PluginCatalog.Require(pluginId);

        if (!File.Exists(AppPaths.Cs2Exe))
            throw new InvalidOperationException("Create the CS2 server first (Create Server tab).");

        chain.Add(pluginId);
        foreach (var depId in plugin.DependsOn)
        {
            if (chain.Contains(depId)) continue;
            var dep = PluginCatalog.Find(depId);
            if (dep is null || dep.IsInstalled) continue;

            log.Add($"— {plugin.Name} needs {dep.Name}; installing it first…");
            Install(depId, log, osPref, chain);
        }

        log.Add($"Fetching latest release for {plugin.Name}…");
        var release = GitHubReleases.Latest(plugin.GitHub, TimeSpan.FromSeconds(10), plugin.GitHubTagPrefix)
                      ?? throw new InvalidOperationException("GitHub API unreachable or no release with assets.");

        var asset = GitHubReleases.PickAsset(plugin, release, osPref)
                    ?? throw new InvalidOperationException($"No suitable asset in release {release.TagName}.");

        log.Add($"Latest: {release.TagName} → {asset.Name} ({asset.Size / 1024} KB)");

        AppPaths.EnsureDir(TempDir);
        var archive = Path.Combine(TempDir, asset.Name);
        log.Add("Downloading…");
        GitHubReleases.Download(asset.DownloadUrl, archive);

        log.Add("Installing (extracting into the server)…");
        ExtractPreservingUserData(plugin, archive, log);
        try { File.Delete(archive); } catch (Exception) { }

        PostInstall(plugin, asset, log);

        if (plugin.VersionSrc == VersionSource.Tracker)
            PluginState.Record(plugin.Id, release.TagName.TrimStart('v'));

        if (plugin.IsInstalled)
            log.Add($"[+] {plugin.Name} {release.TagName} installed and ready.");
        else
            log.Add($"! {plugin.Name} extracted, but its file was not where "
                    + $"expected — it may still load. ({plugin.Marker})");

        return 0;
    }

    private static void PostInstall(PluginDef plugin, ReleaseAsset asset, JobLog log)
    {
        switch (plugin.Id)
        {
            case "metamod":
                Overlay.PatchGameinfo();
                log.Add("[+] gameinfo.gi patched for Metamod.");
                break;

            case "counterstrikesharp":
                if (!asset.Name.Contains("with-runtime", StringComparison.OrdinalIgnoreCase))
                    DotnetRuntime.Ensure(log);

                if (Directory.Exists(AppPaths.CssBase)) Overlay.EnsureCssBasePathLink(log);

                ServerConfigurator.PatchWeaponPaintsConfig(log);
                break;

            case "weaponpaints":
                ServerConfigurator.PatchWeaponPaintsConfig(log);
                ServerConfigurator.ConfigureWeaponPaintsDb(log);
                ServerConfigurator.EnsureSkinsSchema?.Invoke();
                break;
        }
    }

    private static void ExtractPreservingUserData(PluginDef plugin, string archive, JobLog log)
    {
        var root = plugin.ExtractTo;
        AppPaths.EnsureDir(root);

        var backup = Path.Combine(BackupDir, plugin.Id);
        DeleteTree(backup);

        var saved = new List<string>();
        foreach (var relative in plugin.Preserve)
        {
            var source = Path.Combine(root, relative);
            if (!Path.Exists(source)) continue;

            var target = Path.Combine(backup, relative);
            AppPaths.EnsureDir(Path.GetDirectoryName(target)!);
            Move(source, target);
            saved.Add(relative);
            log.Add($"  · kept your {relative}");
        }

        Archives.ExtractSafely(archive, root);
        log.Add($"[+] Extracted into {root}");

        foreach (var relative in saved)
        {
            var source = Path.Combine(backup, relative);
            var target = Path.Combine(root, relative);

            if (Path.Exists(target)) DeleteAny(target);

            AppPaths.EnsureDir(Path.GetDirectoryName(target)!);
            Move(source, target);
        }

        DeleteTree(backup);
    }

    private static void Move(string source, string target)
    {
        if (Directory.Exists(source)) Directory.Move(source, target);
        else File.Move(source, target, overwrite: true);
    }

    private static void DeleteAny(string path)
    {
        if (Directory.Exists(path)) DeleteTree(path);
        else { try { File.Delete(path); } catch (Exception) { } }
    }

    private static void DeleteTree(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (Exception) { }
    }

    public static int InstallAll(JobLog log, string osPref)
    {
        if (!File.Exists(AppPaths.Cs2Exe))
            throw new InvalidOperationException("Create the CS2 server first (Create Server tab).");

        var all = PluginCatalog.All;
        var done = 0;

        for (var i = 0; i < all.Count; i++)
        {
            var plugin = all[i];
            var position = $"({i + 1}/{all.Count})";

            if (plugin.IsInstalled)
            {
                log.Add($"[=] {position} {plugin.Name} — already installed, skipping.");
                done++;
                continue;
            }

            log.Add($"=== {position} {plugin.Name} ===");
            try
            {
                Install(plugin.Id, log, osPref);
                done++;
            }
            catch (Exception e)
            {
                log.Add($"! {plugin.Name} failed: {e.Message}");
            }
        }

        log.Add($"[+] Auto-install finished — {done}/{all.Count} plugins ready.");
        return 0;
    }

    public static void ReinstallAfterRebuild(JobLog log)
    {
        foreach (var id in new[] { "metamod", "counterstrikesharp" })
        {
            var plugin = PluginCatalog.Find(id);
            if (plugin is null || !plugin.IsInstalled) continue;

            log.Add($"Updating {plugin.Name} to match the new CS2…");
            try
            {
                Install(id, log);
            }
            catch (Exception e)
            {
                log.Add($"! Could not auto-update {plugin.Name} ({e.Message}). "
                        + "Re-install it from the Plugins tab.");
            }
        }
    }
}
