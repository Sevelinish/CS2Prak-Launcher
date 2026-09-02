using System.Diagnostics;
using System.Text;

namespace Cs2Prak.Core.Uninstall;

public sealed record UninstallTarget(string key, string path, string kind, string? Root, bool nested);

public static class Uninstaller
{
    public const string ConfirmToken = "UNINSTALL";

    private static string Script => Path.Combine(Path.GetTempPath(), "cs2prak_cs_uninstall.cmd");

    private static string DemoLibrary => Path.Combine(AppPaths.Root, "demo_library.json");

    public static string? Blocked() =>
        InstallMarker.IsInstalled
            ? null
            : "Uninstall runs only from an installed cs2prak. Started from a build tree, "
              + "this would delete the project folder.";

    private static string[] Roots => [AppPaths.Root, Path.GetTempPath()];

    public static List<UninstallTarget> Targets()
    {
        var temp = Path.GetTempPath();

        (string Key, string Path, string Kind)[] raw =
        [
            ("server",        AppPaths.ServerRoot, "dir"),
            ("overlayState",  Path.Combine(AppPaths.ServerRoot, "overlay_state.json"), "file"),
            ("skinsDb",       AppPaths.DbPath, "file"),
            ("pluginState",   AppPaths.PluginStatePath, "file"),
            ("demoLibrary",   DemoLibrary, "file"),
            ("faceitAvatars", Path.Combine(AppPaths.Root, "faceit_avatars.json"), "file"),
            ("faceitKey",     Path.Combine(AppPaths.Root, "faceit_key.txt"), "file"),
            ("errorLog",      AppPaths.ShellErrorLog, "file"),
            ("demosCache",    AppPaths.DemosCacheDir, "dir"),
            ("tmpUpdate",     AppPaths.UpdateDir, "dir"),
            ("tmpStage",      Path.Combine(temp, "cs2prak_stage"), "dir"),
            ("tmpAdvanced",   Path.Combine(temp, "cs2prak_adv"), "dir"),
            ("tmpPlugins",    Path.Combine(temp, "cs2prak_plugins"), "dir"),
            ("tmpPluginBak",  Path.Combine(temp, "cs2prak_pluginbak"), "dir"),
            ("assets",        AppPaths.StaticDir, "dir"),
            ("templates",     AppPaths.TemplatesDir, "dir"),
            ("maps",          AppPaths.MapsDir, "dir"),
            ("runtimes",      Path.Combine(AppPaths.Root, "runtimes"), "dir"),
        ];

        var items = new List<UninstallTarget>();
        foreach (var (key, path, kind) in raw)
        {
            var root = Roots.FirstOrDefault(r => FileLinks.IsUnder(path, r));
            if (root is null) continue;
            items.Add(new UninstallTarget(key, Path.GetFullPath(path), kind,
                                          Path.GetFullPath(root), false));
        }

        var named = items.Select(i => i.path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var file in ProgramFiles(named))
            items.Add(new UninstallTarget("program", file, "file", AppPaths.Root, false));

        if (CssBasePathLink() is { } link)
            items.Insert(0, new UninstallTarget("addonsLink", link, "link", null, false));

        return items.Select(item => item with
        {
            nested = items.Any(other => !ReferenceEquals(other, item)
                                        && other.kind == "dir"
                                        && FileLinks.IsUnder(item.path, other.path)),
        }).ToList();
    }

    private static IEnumerable<string> ProgramFiles(HashSet<string> alreadyNamed)
    {
        string[] extensions = [".exe", ".dll", ".pdb", ".json", ".xml", ".ico", ".config"];
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(AppPaths.Root); }
        catch (Exception) { yield break; }

        foreach (var file in files)
        {
            if (alreadyNamed.Contains(Path.GetFullPath(file))) continue;
            if (extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                yield return file;
        }
    }

    private static string? CssBasePathLink()
    {
        var root = Path.GetPathRoot(Path.GetFullPath(AppPaths.CsgoAddons));
        if (string.IsNullOrEmpty(root)) return null;

        var link = Path.Combine(root, "addons");
        if (!FileLinks.IsLink(link)) return null;

        var target = FileLinks.Target(link);
        return target is not null && FileLinks.IsUnder(target, AppPaths.ServerRoot) ? link : null;
    }

    public static long SizeOnDisk(string path)
    {
        if (FileLinks.IsLink(path)) return 0;

        if (!FileLinks.IsDirectoryEntry(path))
        {
            try
            {
                return FileLinks.HardLinkCount(path) <= 1 ? new FileInfo(path).Length : 0;
            }
            catch (Exception) { return 0; }
        }

        long total = 0;
        var stack = new Stack<string>();
        stack.Push(path);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            IEnumerable<string> entries;
            try { entries = Directory.EnumerateFileSystemEntries(current); }
            catch (Exception) { continue; }

            foreach (var entry in entries)
            {
                if (FileLinks.IsLink(entry)) continue;
                if (FileLinks.IsDirectoryEntry(entry)) { stack.Push(entry); continue; }
                try
                {
                    if (FileLinks.HardLinkCount(entry) <= 1) total += new FileInfo(entry).Length;
                }
                catch (Exception) { }
            }
        }
        return total;
    }

    public static int Run(JobLog log)
    {
        if (Blocked() is { } blocked) throw new InvalidOperationException(blocked);

        if (Cs2ServerProcess.Kill())
        {
            log.Add("Stopped the running CS2 server.");
            Thread.Sleep(1500);
        }

        if (Directory.Exists(AppPaths.Cs2Game))
        {
            log.Add("Unlinking the overlay from your installed CS2…");
            Overlay.Unbind(log);
        }

        var targets = Targets();
        foreach (var target in targets)
        {
            if (target.kind == "link")
            {
                if (CssBasePathLink() is { } link && FileLinks.RemoveLink(link, out _))
                    log.Add($"Removed the CounterStrikeSharp base-path link {link}");
                continue;
            }

            if (!Path.Exists(target.path) && !FileLinks.IsLink(target.path)) continue;
            log.Add($"Removing {target.path}…");
            Delete(target.path, target.Root, log);
        }

        var leftovers = targets
            .Where(t => t.kind != "link" && (Path.Exists(t.path) || FileLinks.IsLink(t.path)))
            .Select(t => t.path)
            .ToList();

        WriteScript(leftovers, log);
        log.Add("Closing cs2prak — the last files go with it.");
        return 0;
    }

    private static void Delete(string path, string? root, JobLog log)
    {
        if (root is not null && !FileLinks.IsUnder(path, root))
            throw new InvalidOperationException($"refusing to delete outside {root}: {path}");

        if (!Path.Exists(path) && !FileLinks.IsLink(path)) return;

        if (FileLinks.IsLink(path)) { Unlink(path, log); return; }

        if (FileLinks.IsPlainDirectory(path))
        {
            string[] children;
            try { children = Directory.GetFileSystemEntries(path); }
            catch (Exception e) { log.Add($"  ! could not list {path} ({e.Message})"); return; }

            foreach (var child in children) Delete(child, root, log);

            try { Directory.Delete(path); }
            catch (Exception e) { log.Add($"  ! {path} left behind ({e.Message})"); }
            return;
        }

        if (File.Exists(path)) Unlink(path, log);
        else log.Add($"  ! skipped {path} — not provably a plain file or folder");
    }

    private static bool Unlink(string path, JobLog log)
    {
        for (var retry = 0; retry < 2; retry++)
        {
            try
            {
                if (FileLinks.IsDirectoryEntry(path)) Directory.Delete(path);
                else File.Delete(path);
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                if (retry == 1) break;
                try { File.SetAttributes(path, FileAttributes.Normal); }
                catch (Exception) { break; }
            }
            catch (Exception e)
            {
                log.Add($"  ! could not remove {path} ({e.Message})");
                return false;
            }
        }
        log.Add($"  ! could not remove {path} (in use or read-only)");
        return false;
    }

    private static void WriteScript(List<string> leftovers, JobLog log)
    {
        var pid = Environment.ProcessId;
        var lines = new List<string>
        {
            "@echo off",
            "chcp 65001 >nul",
            "cd /d \"%TEMP%\"",
            ":waitloop",
            $"tasklist /fi \"PID eq {pid}\" 2>nul | find \"{pid}\" >nul",
            "if not errorlevel 1 ( timeout /t 1 /nobreak >nul & goto waitloop )",
        };

        foreach (var path in leftovers)
        {
            lines.Add(FileLinks.IsDirectoryEntry(path)
                ? $"rd /s /q \"{path}\" 2>nul"
                : $"del /f /q \"{path}\" 2>nul");
        }

        lines.Add($"rd /q \"{AppPaths.Root}\" 2>nul");
        lines.Add("(goto) 2>nul & del /f /q \"%~f0\"");

        File.WriteAllText(Script, string.Join("\r\n", lines) + "\r\n", new UTF8Encoding(false));
        log.Add($"Wrote {Script} — it finishes once cs2prak has exited.");
    }

    public static void LaunchScript()
    {
        if (!File.Exists(Script)) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd",
                Arguments = $"/c \"{Script}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }
        catch (Exception) { }
    }
}
