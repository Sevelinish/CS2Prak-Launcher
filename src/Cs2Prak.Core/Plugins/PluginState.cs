using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Cs2Prak.Core.Plugins;

public static class PluginState
{
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public const string Unknown = "unknown";

    public static JsonObject Load()
    {
        lock (Gate)
        {
            try
            {
                return JsonNode.Parse(File.ReadAllText(AppPaths.PluginStatePath)) as JsonObject
                       ?? new JsonObject();
            }
            catch (Exception) { return new JsonObject(); }
        }
    }

    public static void Record(string pluginId, string version)
    {
        lock (Gate)
        {
            JsonObject state;
            try
            {
                state = JsonNode.Parse(File.ReadAllText(AppPaths.PluginStatePath)) as JsonObject
                        ?? new JsonObject();
            }
            catch (Exception) { state = new JsonObject(); }

            state[pluginId] = version;
            try { File.WriteAllText(AppPaths.PluginStatePath, state.ToJsonString(Indented)); }
            catch (Exception) {  }
        }
    }

    public static string? LocalVersion(PluginDef plugin, JsonObject? state = null)
    {
        if (!plugin.IsInstalled) return null;

        return plugin.VersionSrc switch
        {
            VersionSource.CssDeps => FromCssDeps(),
            VersionSource.Dll => FromDll(plugin.Marker),
            VersionSource.Tracker => FromTracker(plugin.Id, state),
            _ => Unknown,
        };
    }

    private static string FromCssDeps()
    {
        try
        {
            var deps = Path.Combine(AppPaths.CsgoAddons,
                @"counterstrikesharp\api\CounterStrikeSharp.API.deps.json");
            if (JsonNode.Parse(File.ReadAllText(deps)) is not JsonObject root) return Unknown;
            if (root["libraries"] is not JsonObject libraries) return Unknown;

            foreach (var (key, _) in libraries)
            {
                if (key.Contains("CounterStrikeSharp.API", StringComparison.Ordinal))
                    return key.Split('/')[^1];
            }
        }
        catch (Exception) {  }
        return Unknown;
    }

    private static string FromDll(string path)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            return $"{info.FileMajorPart}.{info.FileMinorPart}.{info.FileBuildPart}.{info.FilePrivatePart}";
        }
        catch (Exception) { return Unknown; }
    }

    private static string FromTracker(string pluginId, JsonObject? state)
    {
        state ??= Load();
        return state[pluginId] is JsonValue v && v.TryGetValue(out string? s) ? s : Unknown;
    }
}
