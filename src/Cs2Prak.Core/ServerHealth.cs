using Cs2Prak.Core.Plugins;

namespace Cs2Prak.Core;

public sealed record HealthIssue(string key, string message, bool fatal, bool repaired);

public static class ServerHealth
{
    private static string MetamodShim =>
        Path.Combine(AppPaths.CsgoAddons, @"metamod\bin\win64\server.dll");

    public static List<HealthIssue> Check(bool repair)
    {
        var issues = new List<HealthIssue>();

        if (!File.Exists(AppPaths.Cs2Exe))
        {
            issues.Add(new HealthIssue("server", "CS2 server is not installed.", true, false));
            return issues;
        }

        CheckEngineRoot(issues, repair);
        CheckGameinfo(issues, repair);
        CheckMetamod(issues);

        return issues;
    }

    private static void CheckEngineRoot(List<HealthIssue> issues, bool repair)
    {
        if (!Overlay.EngineBinIsLink()) return;

        var repaired = repair && Overlay.RepairEngineBin();
        issues.Add(new HealthIssue(
            "engineRoot",
            repaired
                ? "The engine folder was a link into your Steam copy, so the server started "
                  + "vanilla. Rebuilt it in place — plugins load again."
                : "The engine folder is a link into your Steam copy. The server will start "
                  + "vanilla because CS2 reads your Steam gameinfo.gi instead of ours. "
                  + "Rebuild the server to fix it.",
            !repaired,
            repaired));
    }

    private static void CheckGameinfo(List<HealthIssue> issues, bool repair)
    {
        if (!File.Exists(AppPaths.GameinfoGi))
        {
            issues.Add(new HealthIssue("gameinfo", "gameinfo.gi is missing from the server.",
                                       true, false));
            return;
        }

        if (HasMetamodPath()) return;

        var repaired = false;
        if (repair)
        {
            Overlay.PatchGameinfo();
            repaired = HasMetamodPath();
        }

        issues.Add(new HealthIssue(
            "gameinfo",
            repaired
                ? "gameinfo.gi had lost its Metamod search path. Patched it back."
                : "gameinfo.gi has no Metamod search path and could not be patched. "
                  + "Rebuild the server.",
            !repaired,
            repaired));
    }

    private static void CheckMetamod(List<HealthIssue> issues)
    {
        if (File.Exists(MetamodShim)) return;

        issues.Add(new HealthIssue(
            "metamod",
            "Metamod is not installed, so no plugin will load. Install it in the Plugins tab.",
            true,
            false));
    }

    private static bool HasMetamodPath()
    {
        try
        {
            return File.ReadAllText(AppPaths.GameinfoGi)
                       .Contains("csgo/addons/metamod", StringComparison.Ordinal);
        }
        catch (Exception) { return false; }
    }

    public static bool CssInstalled => PluginCatalog.Find("counterstrikesharp")?.IsInstalled ?? false;
}
