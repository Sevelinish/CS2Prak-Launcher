using System.Text.Json;
using System.Text.Json.Nodes;

namespace Cs2Prak.Core.Skins;

public static class Catalogues
{
    private static readonly string[] Files =
        ["skins_en.json", "gloves_en.json", "agents_en.json", "stickers_en.json"];

    private static readonly object Gate = new();
    private static readonly Dictionary<string, DateTime> Stamps = new();

    public static JsonObject SkinsByWeapon { get; private set; } = new();

    public static JsonArray Gloves { get; private set; } = [];
    public static JsonArray Agents { get; private set; } = [];
    public static JsonArray Stickers { get; private set; } = [];

    public static bool Any => SkinsByWeapon.Count > 0;

    public static void RefreshIfChanged()
    {
        lock (Gate)
        {
            foreach (var name in Files)
            {
                if (Stamps.TryGetValue(name, out var known) && known == StampOf(name)) continue;
                Reload();
                return;
            }
        }
    }

    public static void Reload()
    {
        lock (Gate)
        {
            var skins = new JsonObject();
            if (Load("skins_en.json") is JsonArray raw)
            {
                foreach (var node in raw)
                {
                    if (node is not JsonObject skin) continue;
                    var weapon = skin["weapon_name"]?.GetValue<string>();
                    if (weapon is null) continue;

                    if (skins[weapon] is not JsonArray bucket)
                    {
                        bucket = [];
                        skins[weapon] = bucket;
                    }
                    bucket.Add(skin.DeepClone());
                }
            }

            SkinsByWeapon = skins;
            Gloves = Load("gloves_en.json") as JsonArray ?? [];
            Agents = Load("agents_en.json") as JsonArray ?? [];
            Stickers = Load("stickers_en.json") as JsonArray ?? [];

            foreach (var name in Files) Stamps[name] = StampOf(name);
        }
    }

    private static DateTime StampOf(string name)
    {
        try { return File.GetLastWriteTimeUtc(Path.Combine(AppPaths.PluginData, name)); }
        catch (Exception) { return default; }
    }

    private static JsonNode? Load(string name)
    {
        try
        {
            return JsonNode.Parse(File.ReadAllText(Path.Combine(AppPaths.PluginData, name)));
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (JsonException) { return null; }
    }
}
