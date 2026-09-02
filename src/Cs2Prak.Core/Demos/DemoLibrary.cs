using System.Text.Json.Nodes;

namespace Cs2Prak.Core.Demos;

public static class DemoLibrary
{
    private static readonly object Gate = new();

    private static string File_ => Path.Combine(AppPaths.Root, "demo_library.json");

    public static JsonArray List()
    {
        var kept = new JsonArray();
        foreach (var node in Load())
        {
            if (node is not JsonObject entry) continue;
            var key = entry["key"]?.GetValue<string>();
            if (key is null || !DemoCache.IsCached(key)) continue;
            kept.Add(entry.DeepClone());
        }
        return kept;
    }

    public static void Add(string key, string name, string map, int scoreA, int scoreB, string winner)
    {
        lock (Gate)
        {
            var entry = new JsonObject
            {
                ["key"] = key,
                ["name"] = name,
                ["map"] = map,
                ["sa"] = scoreA,
                ["sb"] = scoreB,
                ["winner"] = winner,
                ["added"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            };

            var kept = new JsonArray { entry };
            foreach (var node in Load())
            {
                if (node is not JsonObject old) continue;
                if (old["key"]?.GetValue<string>() == key) continue;
                kept.Add(old.DeepClone());
            }
            Save(kept);
        }
    }

    public static void Remove(string key)
    {
        lock (Gate)
        {
            var kept = new JsonArray();
            foreach (var node in Load())
            {
                if (node is not JsonObject entry) continue;
                if (entry["key"]?.GetValue<string>() == key) continue;
                kept.Add(entry.DeepClone());
            }
            Save(kept);
        }
        DemoCache.Forget(key);
    }

    private static JsonArray Load()
    {
        try { return JsonNode.Parse(File.ReadAllText(File_)) as JsonArray ?? []; }
        catch (Exception) { return []; }
    }

    private static void Save(JsonArray entries)
    {
        try { File.WriteAllText(File_, entries.ToJsonString()); }
        catch (Exception) {  }
    }
}
