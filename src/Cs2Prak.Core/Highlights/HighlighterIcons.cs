using System.Text.Json.Nodes;

namespace Cs2Prak.Core.Highlights;

public static class HighlighterIcons
{
    private static readonly TimeSpan ProbeTtl = TimeSpan.FromSeconds(30);
    private static readonly Lock Gate = new();

    private static readonly Dictionary<string, byte[]> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    private static JsonNode? _probe;
    private static DateTime _probedAt;

    public static Func<string, byte[]?>? FromExecutable;

    private static readonly Dictionary<string, string> ExeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cs2"] = "cs2.exe",
        ["hlae"] = "HLAE.exe",
        ["ffmpeg"] = "ffmpeg.exe",
    };

    public static void Warm()
    {
        new Thread(() =>
        {
            foreach (var name in ExeNames.Keys)
            {
                try { For(name, askPlugin: false); }
                catch (Exception) { }
            }
        })
        {
            IsBackground = true,
            Name = "highlighter-icons",
        }.Start();
    }

    public static byte[]? For(string toolName, bool askPlugin = true)
    {
        if (FromExecutable is null) return null;

        var exe = LocalPath(toolName);
        if ((exe is null || !File.Exists(exe)) && askPlugin) exe = PathOf(toolName);
        if (exe is null || !File.Exists(exe)) return null;

        lock (Gate)
        {
            if (Cache.TryGetValue(exe, out var cached)) return cached;
        }

        var png = FromExecutable(exe);
        if (png is null) return null;

        lock (Gate) Cache[exe] = png;
        return png;
    }

    private static string? LocalPath(string toolName)
    {
        if (string.Equals(toolName, "cs2", StringComparison.OrdinalIgnoreCase))
        {
            var game = SteamLocator.FindExistingCs2Game();
            return game is null ? null : Path.Combine(game, @"bin\win64\cs2.exe");
        }
        return SearchTools(toolName);
    }

    private static string? SearchTools(string toolName)
    {
        if (!ExeNames.TryGetValue(toolName, out var exeName)) return null;
        if (HighlighterInstall.Home() is not { } home) return null;

        var tools = Path.Combine(home, "tools");
        if (!Directory.Exists(tools)) return null;

        try
        {
            return Directory.EnumerateFiles(tools, exeName, SearchOption.AllDirectories)
                            .FirstOrDefault();
        }
        catch (Exception) { return null; }
    }

    private static string? PathOf(string toolName)
    {
        JsonNode? probe;
        lock (Gate)
        {
            probe = DateTime.UtcNow - _probedAt < ProbeTtl ? _probe : null;
        }

        if (probe is null)
        {
            probe = HighlighterClient.TryCall("system.probe", null, TimeSpan.FromSeconds(20));
            if (probe is null) return null;

            lock (Gate)
            {
                _probe = probe;
                _probedAt = DateTime.UtcNow;
            }
        }

        foreach (var node in probe["tools"] as JsonArray ?? [])
        {
            if (node is not JsonObject tool) continue;
            if (!string.Equals(tool["name"]?.GetValue<string>(), toolName,
                               StringComparison.OrdinalIgnoreCase)) continue;

            return tool["path"]?.GetValue<string>();
        }
        return null;
    }
}
