using System.Text.Json;
using System.Text.Json.Nodes;

namespace Cs2Prak.Core.Plugins;

public static class ServerConfigurator
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static Action? EnsureSkinsSchema;

    public static Action? ReloadCatalogues;

    public static void PatchWeaponPaintsConfig(JobLog? log = null)
    {
        PatchCssCore(log);
        PatchValvePolicy(log);
    }

    private static void PatchCssCore(JobLog? log)
    {
        try
        {
            var target = AppPaths.CssCoreConfig;
            var existed = File.Exists(target);
            var source = existed
                ? target
                : Path.Combine(Path.GetDirectoryName(target)!, "core.example.json");

            var core = File.Exists(source)
                ? JsonNode.Parse(File.ReadAllText(source)) as JsonObject ?? new JsonObject()
                : new JsonObject();

            if (IsFalse(core["FollowCS2ServerGuidelines"])) return;

            core["FollowCS2ServerGuidelines"] = false;
            AppPaths.EnsureDir(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, core.ToJsonString(Indented));

            log?.Add(existed
                ? "Set FollowCS2ServerGuidelines=false in CSS core.json."
                : "Created CSS core.json with FollowCS2ServerGuidelines=false.");
        }
        catch (Exception e)
        {
            log?.Add($"! Could not write CSS core.json: {e.Message}");
        }
    }

    private static void PatchValvePolicy(JobLog? log)
    {
        var target = AppPaths.WeaponPaintsConfig;
        if (!File.Exists(target)) return;

        try
        {
            if (JsonNode.Parse(File.ReadAllText(target)) is not JsonObject cfg) return;

            var cleared = new List<string>();
            ClearValvePolicy(cfg, "", cleared);
            if (cleared.Count == 0) return;

            File.WriteAllText(target, cfg.ToJsonString(Indented));
            log?.Add($"Set {string.Join(", ", cleared)} to false in WeaponPaints.json.");
        }
        catch (Exception e)
        {
            log?.Add($"! Could not write WeaponPaints.json: {e.Message}");
        }
    }

    private static void ClearValvePolicy(JsonNode? node, string path, List<string> cleared)
    {
        if (node is JsonObject obj)
        {
            foreach (var key in obj.Select(pair => pair.Key).ToList())
            {
                var here = path.Length == 0 ? key : $"{path}.{key}";

                if (key.Contains("valve", StringComparison.OrdinalIgnoreCase)
                    && key.Contains("policy", StringComparison.OrdinalIgnoreCase))
                {
                    if (IsFalse(obj[key])) continue;
                    obj[key] = false;
                    cleared.Add(here);
                    continue;
                }

                ClearValvePolicy(obj[key], here, cleared);
            }
        }
        else if (node is JsonArray array)
        {
            for (var i = 0; i < array.Count; i++)
                ClearValvePolicy(array[i], $"{path}[{i}]", cleared);
        }
    }

    private static bool IsFalse(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<bool>(out var flag) && !flag;

    public static void ConfigureWeaponPaintsDb(JobLog? log = null)
    {
        var dll = Path.Combine(AppPaths.CssPlugins, @"WeaponPaints\WeaponPaints.dll");
        if (!File.Exists(dll)) return;

        var target = AppPaths.WeaponPaintsConfig;
        AppPaths.EnsureDir(Path.GetDirectoryName(target)!);

        JsonObject cfg;
        try
        {
            cfg = JsonNode.Parse(File.ReadAllText(target)) as JsonObject ?? Defaults();
        }
        catch (Exception)
        {
            cfg = Defaults();
        }

        var host = cfg["DatabaseHost"]?.GetValue<string>()?.Trim() ?? "";
        if (host.Length > 0)
        {
            log?.Add($"[+] WeaponPaints database already set ({host}).");
            return;
        }

        cfg["DatabaseHost"] = "127.0.0.1";
        cfg["DatabasePort"] = 3306;
        cfg["DatabaseUser"] = "root";
        cfg["DatabasePassword"] = "";
        cfg["DatabaseName"] = "cs2prak";

        try
        {
            File.WriteAllText(target, cfg.ToJsonString(Indented));
            log?.Add("[+] WeaponPaints database configured (127.0.0.1:3306).");
        }
        catch (Exception e)
        {
            log?.Add($"! Could not write WeaponPaints.json: {e.Message}");
        }
    }

    private static JsonObject Defaults() => new()
    {
        ["ConfigVersion"] = 10,
        ["SkinsLanguage"] = "en",
        ["DatabaseHost"] = "",
        ["DatabasePort"] = 3306,
        ["DatabaseUser"] = "",
        ["DatabasePassword"] = "",
        ["DatabaseName"] = "",
        ["CmdRefreshCooldownSeconds"] = 3,
        ["Website"] = "example.com/skins",
        ["MenuType"] = "selectable",
    };

    public static int ConfigureServer(JobLog log)
    {
        if (!File.Exists(AppPaths.GameinfoGi))
        {
            log.Add("ERROR: CS2 server not found. Install the server first (Download tab).");
            return 0;
        }

        Overlay.PatchGameinfo();
        log.Add("[+] gameinfo.gi patched for Metamod.");

        var cssDll = Path.Combine(AppPaths.CssBase, @"bin\win64\counterstrikesharp.dll");
        if (File.Exists(cssDll))
        {
            var vdf = Path.Combine(AppPaths.CsgoAddons, "counterstrikesharp.vdf");
            File.WriteAllText(vdf,
                "\"Plugin\"\n{\n\t\"file\"\t\t"
                + "\"addons/counterstrikesharp/bin/win64/counterstrikesharp\"\n}\n");
            log.Add("[+] counterstrikesharp.vdf written.");

            DotnetRuntime.Ensure(log);
            Overlay.EnsureCssBasePathLink(log);
        }
        else
        {
            log.Add("— CounterStrikeSharp not found; place it then re-run Configure.");
        }

        MoveWeaponPaintsGamedata(log);

        PatchWeaponPaintsConfig(log);
        ConfigureWeaponPaintsDb(log);

        ReportAdmin(log);

        ReloadCatalogues?.Invoke();
        log.Add("Configuration complete.");
        return 0;
    }

    private static void MoveWeaponPaintsGamedata(JobLog log)
    {
        var wrong = Path.Combine(AppPaths.CssPlugins, "gamedata", "weaponpaints.json");
        var right = Path.Combine(AppPaths.CssBase, "gamedata", "weaponpaints.json");
        if (!File.Exists(wrong) || File.Exists(right)) return;

        try
        {
            AppPaths.EnsureDir(Path.GetDirectoryName(right)!);
            File.Move(wrong, right);
            log.Add("[+] WeaponPaints gamedata moved to correct location.");
        }
        catch (Exception e)
        {
            log.Add($"! Could not move WeaponPaints gamedata: {e.Message}");
        }
    }

    private static void ReportAdmin(JobLog log)
    {
        if (!File.Exists(AppPaths.AdminsJson))
        {
            log.Add("— No admin set. Enter a SteamID64 in the Plugins tab and click SAVE.");
            return;
        }

        try
        {
            if (JsonNode.Parse(File.ReadAllText(AppPaths.AdminsJson)) is not JsonObject admins) return;
            foreach (var (_, value) in admins)
            {
                if (value is not JsonObject entry) continue;
                var identity = entry["identity"]?.GetValue<string>();
                if (string.IsNullOrEmpty(identity)) continue;
                log.Add($"[+] Admin already configured ({identity}).");
                return;
            }
        }
        catch (Exception)
        {
        }
    }
}
