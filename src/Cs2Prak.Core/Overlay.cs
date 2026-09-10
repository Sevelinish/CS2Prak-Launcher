using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Cs2Prak.Core;

public static partial class Overlay
{
    [GeneratedRegex(@"""buildid""\s*""(\d+)""")]
    private static partial Regex BuildIdInAcf();

    [GeneratedRegex(@"ClientVersion=(\d+)")]
    private static partial Regex ClientVersionInInf();

    private static string StatePath => Path.Combine(AppPaths.ServerRoot, "overlay_state.json");

    public static Action<JobLog>? OnRebuilt;

    private static string InsertMetamodSearchPath(string text)
    {
        if (text.Contains("csgo/addons/metamod", StringComparison.Ordinal)) return text;

        foreach (var eol in new[] { "\n", "\r\n" })
        {
            var needle = "\t\t\tGame\tcsgo" + eol;
            var index = text.IndexOf(needle, StringComparison.Ordinal);
            if (index < 0) continue;
            return text[..index]
                   + "\t\t\tGame\tcsgo/addons/metamod" + eol + needle
                   + text[(index + needle.Length)..];
        }
        return text;
    }

    public static void PatchGameinfo()
    {
        try
        {
            var raw = File.ReadAllText(AppPaths.GameinfoGi);
            var patched = InsertMetamodSearchPath(raw);
            if (ReferenceEquals(patched, raw) || patched == raw) return;
            File.WriteAllText(AppPaths.GameinfoGi, patched);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public static string? RetailBuildId()
    {
        var game = SteamLocator.FindExistingCs2Game();
        if (game is null) return null;

        var acf = Path.GetFullPath(Path.Combine(game, "..", "..", "..", "appmanifest_730.acf"));
        var fromAcf = FirstGroup(acf, BuildIdInAcf());
        if (fromAcf is not null) return fromAcf;

        return FirstGroup(Path.Combine(game, "csgo", "steam.inf"), ClientVersionInInf());
    }

    private static string? FirstGroup(string path, Regex pattern)
    {
        try
        {
            var m = pattern.Match(File.ReadAllText(path));
            return m.Success ? m.Groups[1].Value : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static void SaveBuildId()
    {
        try
        {
            AppPaths.EnsureDir(AppPaths.ServerRoot);
            var state = new JsonObject { ["cs2_buildid"] = RetailBuildId() };
            File.WriteAllText(StatePath, state.ToJsonString());
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public static string? BuiltBuildId()
    {
        try
        {
            return (JsonNode.Parse(File.ReadAllText(StatePath)) as JsonObject)
                ?["cs2_buildid"]?.GetValue<string>();
        }
        catch (Exception) { return null; }
    }

    public static bool IsStale()
    {
        var game = SteamLocator.FindExistingCs2Game();
        if (game is null) return false;

        if (Directory.Exists(EngineBinDir) && EngineBinIsLink()) return true;

        foreach (var name in new[]
                 {
                     @"csgo\steam.inf",
                     @"csgo\pak01_dir.vpk",
                     Path.Combine(EngineBin, @"win64\cs2.exe"),
                 })
        {
            var rt = Path.Combine(game, name);
            var ov = Path.Combine(AppPaths.Cs2Game, name);
            if (!File.Exists(rt)) continue;
            if (!File.Exists(ov)) return true;
            if (FileLinks.SameFile(ov, rt) is not true) return true;
        }
        return false;
    }

    public const string EngineBin = "bin";

    public static string EngineBinDir => Path.Combine(AppPaths.Cs2Game, EngineBin);

    public static bool EngineBinIsLink() => FileLinks.IsLink(EngineBinDir);

    public static bool RepairEngineBin(JobLog? log = null)
    {
        var src = SteamLocator.FindExistingCs2Game();
        if (src is null) return false;

        var dst = EngineBinDir;
        if (FileLinks.IsLink(dst) && !FileLinks.RemoveLink(dst, out var error))
        {
            log?.Add($"! Could not replace {dst} ({error}).");
            return false;
        }

        var mirrored = HardLinkTree(Path.Combine(src, EngineBin), dst);
        log?.Add($"[+] Rebuilt {EngineBin} as {mirrored} hardlinks so the engine "
                 + "resolves its root inside the server folder.");
        return File.Exists(AppPaths.Cs2Exe);
    }

    private static int HardLinkTree(string src, string dst)
    {
        AppPaths.EnsureDir(dst);
        foreach (var dir in Directory.EnumerateDirectories(src, "*", SearchOption.AllDirectories))
            AppPaths.EnsureDir(Path.Combine(dst, Path.GetRelativePath(src, dir)));

        var mirrored = 0;
        foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(dst, Path.GetRelativePath(src, file));
            if (Path.Exists(target)) continue;

            if (FileLinks.TryHardLink(file, target)) mirrored++;
            else
            {
                try { File.Copy(file, target, overwrite: false); mirrored++; }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        return mirrored;
    }

    private static bool IsContent(string name)
    {
        var n = name.ToLowerInvariant();
        if (n == "gameinfo.gi" || n.EndsWith(".dem") || n.EndsWith(".dat")) return false;
        if (n.StartsWith("backup_round") || n.StartsWith("~vss") || n == "serverconfig.vdf") return false;
        return true;
    }

    public static int BuildFromExisting(JobLog log)
    {
        var src = SteamLocator.FindExistingCs2Game()
                  ?? throw new InvalidOperationException(
                      "Your installed CS2 was not found in any Steam library.");

        log.Add($"[+] Found your CS2: {src}");

        var dst = AppPaths.Cs2Game;
        var srcDrive = Path.GetPathRoot(Path.GetFullPath(src))?.ToLowerInvariant() ?? "";
        var dstDrive = Path.GetPathRoot(Path.GetFullPath(dst))?.ToLowerInvariant() ?? "";
        if (srcDrive != dstDrive)
        {
            var s = srcDrive.TrimEnd('\\', '/');
            var d = dstDrive.TrimEnd('\\', '/');
            throw new InvalidOperationException(
                $"cs2prak is on {d} but CS2 is on {s}. Hardlinks need the same drive — "
                + $"move cs2prak onto {s} and build the server again.");
        }

        AppPaths.EnsureDir(dst);

        foreach (var entry in new DirectoryInfo(src).EnumerateFileSystemInfos())
        {
            if (entry.Name.Equals("csgo", StringComparison.OrdinalIgnoreCase)) continue;
            var link = Path.Combine(dst, entry.Name);
            if (Path.Exists(link)) continue;

            if (!FileLinks.IsDirectoryEntry(entry.FullName))
            {
                if (IsContent(entry.Name)) FileLinks.TryHardLink(entry.FullName, link);
            }
            else if (entry.Name.Equals(EngineBin, StringComparison.OrdinalIgnoreCase))
            {
                var mirrored = HardLinkTree(entry.FullName, link);
                log.Add($"[+] Mirrored {EngineBin} with {mirrored} hardlinks (0 extra disk).");
            }
            else
            {
                FileLinks.CreateJunction(link, entry.FullName);
            }
        }
        log.Add("[+] Linked engine + content folders (junctions, 0 extra disk).");

        var ourCsgo = Path.Combine(dst, "csgo");
        var srcCsgo = Path.Combine(src, "csgo");
        AppPaths.EnsureDir(ourCsgo);

        var linked = 0;
        foreach (var entry in new DirectoryInfo(srcCsgo).EnumerateFileSystemInfos())
        {
            var name = entry.Name.ToLowerInvariant();
            var link = Path.Combine(ourCsgo, entry.Name);

            if (FileLinks.IsDirectoryEntry(entry.FullName))
            {
                if (name == "addons")
                {
                    AppPaths.EnsureDir(link);
                }
                else if (name == "cfg")
                {
                    if (!Path.Exists(link)) CopyTree(entry.FullName, link);
                }
                else if (!Path.Exists(link))
                {
                    FileLinks.CreateJunction(link, entry.FullName);
                }
            }
            else if (name != "gameinfo.gi" && IsContent(entry.Name) && !Path.Exists(link))
            {
                if (FileLinks.TryHardLink(entry.FullName, link)) linked++;
            }
        }
        log.Add($"[+] Hardlinked {linked} content files (VPKs) — 0 extra disk.");

        WriteOverlayGameinfo(srcCsgo, ourCsgo);
        AppPaths.EnsureDir(Path.Combine(ourCsgo, "addons"));
        log.Add("[+] Wrote our Metamod-patched gameinfo.gi into the overlay (your game stays vanilla).");

        SaveBuildId();
        log.Add($"[+] Overlay ready at {dst}");
        log.Add("Done — now install Metamod / CounterStrikeSharp / WeaponPaints in the Plugins tab, then launch.");
        return 0;
    }

    private static void WriteOverlayGameinfo(string srcCsgo, string ourCsgo)
    {
        var raw = File.ReadAllBytes(Path.Combine(srcCsgo, "gameinfo.gi"));
        if (raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF) raw = raw[3..];

        var text = InsertMetamodSearchPath(Encoding.UTF8.GetString(raw));
        File.WriteAllBytes(Path.Combine(ourCsgo, "gameinfo.gi"), Encoding.UTF8.GetBytes(text));
    }

    private static void CopyTree(string src, string dst)
    {
        AppPaths.EnsureDir(dst);
        foreach (var dir in Directory.EnumerateDirectories(src, "*", SearchOption.AllDirectories))
            AppPaths.EnsureDir(Path.Combine(dst, Path.GetRelativePath(src, dir)));
        foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(dst, Path.GetRelativePath(src, file)), overwrite: false);
    }

    public static void Unbind(JobLog log)
    {
        var dst = AppPaths.Cs2Game;
        var ourCsgo = Path.Combine(dst, "csgo");
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "addons", "cfg", "gameinfo.gi" };
        var removed = 0;

        void Drop(string path)
        {
            if (FileLinks.RemoveLink(path, out var error)) { removed++; return; }
            if (error is null && DropMirror(path)) { removed++; return; }
            if (error is not null) log.Add($"  ! could not unbind {Path.GetFileName(path)} ({error})");
        }

        if (Directory.Exists(ourCsgo))
            foreach (var entry in new DirectoryInfo(ourCsgo).EnumerateFileSystemInfos())
                if (!keep.Contains(entry.Name)) Drop(entry.FullName);

        if (Directory.Exists(dst))
            foreach (var entry in new DirectoryInfo(dst).EnumerateFileSystemInfos())
                if (!entry.Name.Equals("csgo", StringComparison.OrdinalIgnoreCase)) Drop(entry.FullName);

        log.Add($"[+] Unbound {removed} old links to the game "
                + "(kept your plugins, configs and gameinfo).");
    }

    private static bool DropMirror(string path)
    {
        if (!FileLinks.IsPlainDirectory(path)) return false;
        if (!FileLinks.IsUnder(path, AppPaths.ServerRoot)) return false;

        try { Directory.Delete(path, recursive: true); return true; }
        catch (Exception) { return false; }
    }

    public static int Rebuild(JobLog log)
    {
        if (!File.Exists(AppPaths.Cs2Exe))
            throw new InvalidOperationException("No server to rebuild yet — create it first.");

        var built = BuiltBuildId();
        var current = RetailBuildId();
        if (built is not null && current is not null && built == current)
        {
            log.Add($"! Heads up: CS2 build is still {current} — update the game in "
                    + "Steam first for this to help. Rebuilding anyway.");
        }

        log.Add("Unbinding old links to the game…");
        Unbind(log);
        log.Add("Re-binding against your current CS2…");
        BuildFromExisting(log);

        OnRebuilt?.Invoke(log);
        return 0;
    }

    public static void EnsureCssBasePathLink(JobLog? log = null)
    {
        void Say(string m) => log?.Add(m);

        if (!Directory.Exists(AppPaths.CsgoAddons)) return;

        var link = DriveRootAddons();
        if (link is null) return;

        var want = (FileLinks.Target(AppPaths.CsgoAddons) ?? Path.GetFullPath(AppPaths.CsgoAddons))
                   .TrimEnd('\\', '/');

        try
        {
            if (Path.Exists(link) || FileLinks.IsLink(link))
            {
                var have = FileLinks.Target(link)?.TrimEnd('\\', '/');
                if (have is not null && have.Equals(want, StringComparison.OrdinalIgnoreCase)) return;

                if (!FileLinks.RemoveLink(link, out var error))
                {
                    Say($"! {link} exists and could not be replaced ({error ?? "not a link"}). "
                        + $"Remove it manually (rmdir \"{link}\") and re-run.");
                    return;
                }
            }

            if (FileLinks.CreateJunction(link, AppPaths.CsgoAddons))
                Say($@"[+] CSS base-path link created: {link} -> csgo\addons");
            else
                Say($"! Could not create CSS base-path link {link}.");
        }
        catch (Exception e)
        {
            Say($"! CSS base-path link error: {e.Message}");
        }
    }

    public static void RemoveCssBasePathLink(JobLog? log = null)
    {
        var link = OurCssBasePathLink();
        if (link is null) return;

        if (FileLinks.RemoveLink(link, out var error))
            log?.Add($"[+] Removed CSS base-path link {link}");
        else
            log?.Add($"! Could not remove {link} ({error}); remove it with: rmdir \"{link}\"");
    }

    private static string? DriveRootAddons()
    {
        var root = Path.GetPathRoot(Path.GetFullPath(AppPaths.CsgoAddons));
        return string.IsNullOrEmpty(root) ? null : Path.Combine(root, "addons");
    }

    private static string? OurCssBasePathLink()
    {
        var link = DriveRootAddons();
        if (link is null || !FileLinks.IsLink(link)) return null;

        var target = FileLinks.Target(link);
        return target is not null && FileLinks.IsUnder(target, AppPaths.ServerRoot) ? link : null;
    }
}
