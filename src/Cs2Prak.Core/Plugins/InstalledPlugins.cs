using System.Text.RegularExpressions;

namespace Cs2Prak.Core.Plugins;

public sealed record InstalledPlugin(string folder, bool enabled, bool external, string name);

public static partial class InstalledPlugins
{
    [GeneratedRegex("^[A-Za-z0-9._-]{1,64}$")]
    private static partial Regex FolderName();

    private static Dictionary<string, string> KnownFolders()
    {
        var known = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var plugin in PluginCatalog.All)
        {
            if (!FileLinks.IsUnder(plugin.Marker, AppPaths.CssPlugins)) continue;

            var relative = Path.GetRelativePath(AppPaths.CssPlugins, plugin.Marker);
            var folder = relative.Split(Path.DirectorySeparatorChar)[0];
            known[folder] = plugin.Name;
        }
        return known;
    }

    public static List<InstalledPlugin> List()
    {
        var known = KnownFolders();
        var items = Scan(AppPaths.CssPlugins, enabled: true, known);
        items.AddRange(Scan(AppPaths.CssPluginsDisabled, enabled: false, known));

        items.Sort((a, b) => a.external != b.external
            ? a.external.CompareTo(b.external)
            : string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
        return items;
    }

    private static List<InstalledPlugin> Scan(string baseDir, bool enabled,
                                              Dictionary<string, string> known)
    {
        var found = new List<InstalledPlugin>();
        if (!Directory.Exists(baseDir)) return found;

        foreach (var dir in Directory.EnumerateDirectories(baseDir))
        {
            var name = Path.GetFileName(dir);

            bool hasDll;
            try
            {
                hasDll = Directory.EnumerateFiles(dir, "*.dll").Any();
            }
            catch (Exception) { hasDll = false; }
            if (!hasDll) continue;

            found.Add(new InstalledPlugin(
                folder: name,
                enabled: enabled,
                external: !known.ContainsKey(name),
                name: known.TryGetValue(name, out var friendly) ? friendly : name));
        }
        return found;
    }

    public sealed record ToggleResult(bool Ok, string? Message, int Status);

    public static ToggleResult Toggle(string? folder, bool enable)
    {
        if (folder is null || !FolderName().IsMatch(folder))
            return new ToggleResult(false, "Invalid plugin folder.", 400);

        var sourceBase = enable ? AppPaths.CssPluginsDisabled : AppPaths.CssPlugins;
        var targetBase = enable ? AppPaths.CssPlugins : AppPaths.CssPluginsDisabled;

        var source = ResolveExisting(sourceBase, folder);
        if (source is null)
        {
            return ResolveExisting(targetBase, folder) is not null
                ? new ToggleResult(true, null, 200)
                : new ToggleResult(false, "Plugin folder not found.", 404);
        }

        var target = Path.Combine(targetBase, Path.GetFileName(source));

        if (!FileLinks.IsUnder(target, AppPaths.CssBase) ||
            !FileLinks.IsUnder(source, AppPaths.CssBase))
            return new ToggleResult(false, "Invalid plugin folder.", 400);

        try
        {
            AppPaths.EnsureDir(targetBase);
            if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
            Directory.Move(source, target);
            return new ToggleResult(true, null, 200);
        }
        catch (Exception e)
        {
            return new ToggleResult(false,
                $"Could not move plugin (is the server running?): {e.Message}", 500);
        }
    }

    private static string? ResolveExisting(string baseDir, string folder)
    {
        if (!Directory.Exists(baseDir)) return null;
        try
        {
            return Directory.EnumerateDirectories(baseDir).FirstOrDefault(
                d => string.Equals(Path.GetFileName(d), folder, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception) { return null; }
    }
}
