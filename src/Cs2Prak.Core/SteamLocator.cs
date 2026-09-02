using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Cs2Prak.Core;

public static partial class SteamLocator
{
    [GeneratedRegex(@"""path""\s*""([^""]+)""")]
    private static partial Regex VdfPath();

    public static string? SteamPath()
    {
        (RegistryKey hive, string sub, string name)[] probes =
        [
            (Registry.CurrentUser,  @"Software\Valve\Steam",            "SteamPath"),
            (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"),
            (Registry.LocalMachine, @"SOFTWARE\Valve\Steam",             "InstallPath"),
        ];

        foreach (var (hive, sub, name) in probes)
        {
            try
            {
                using var key = hive.OpenSubKey(sub);
                if (key?.GetValue(name) is not string raw || string.IsNullOrWhiteSpace(raw)) continue;
                var p = Path.GetFullPath(raw);
                if (Directory.Exists(p)) return p;
            }
            catch (Exception) {  }
        }

        var fallbacks = new[]
        {
            Path.Combine(Environment.GetEnvironmentVariable("ProgramFiles(x86)") ?? @"C:\Program Files (x86)", "Steam"),
            Path.Combine(Environment.GetEnvironmentVariable("ProgramFiles")      ?? @"C:\Program Files",      "Steam"),
            @"C:\Steam",
        };
        return fallbacks.FirstOrDefault(Directory.Exists);
    }

    public static List<string> Libraries()
    {
        var libs = new List<string>();
        var steam = SteamPath();
        if (steam is null) return libs;

        libs.Add(steam);
        try
        {
            var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            var text = File.ReadAllText(vdf);
            foreach (Match m in VdfPath().Matches(text))
            {
                var p = Path.GetFullPath(m.Groups[1].Value.Replace(@"\\", @"\"));
                if (!libs.Contains(p, StringComparer.OrdinalIgnoreCase)) libs.Add(p);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return libs;
    }

    public static string? FindExistingCs2Game()
    {
        foreach (var lib in Libraries())
        {
            var g = Path.Combine(lib, "steamapps", "common", "Counter-Strike Global Offensive", "game");
            if (File.Exists(Path.Combine(g, "bin", "win64", "cs2.exe"))) return g;
        }
        return null;
    }

    public static string? FindClientCfgDir()
    {
        foreach (var lib in Libraries())
        {
            var cfg = Path.Combine(lib, "steamapps", "common",
                                   "Counter-Strike Global Offensive", "game", "csgo", "cfg");
            if (Directory.Exists(cfg)) return cfg;
        }
        return null;
    }
}
