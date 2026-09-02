using System.Text.Json.Nodes;

namespace Cs2Prak.Core.Faceit;

public sealed record FaceitProfile(string url, string nick, int lvl, int elo)
{
    public static readonly FaceitProfile Empty = new("", "", 0, 0);
}

public static class FaceitAvatars
{
    private static readonly object Gate = new();

    private static string CacheFile => Path.Combine(AppPaths.Root, "faceit_avatars.json");

    public static FaceitProfile? Lookup(string steamId)
    {
        if (Read() is { } cached) return cached;

        var key = FaceitApi.Key();
        if (key is null) return null;

        var profile = FaceitProfile.Empty;
        try
        {
            var player = FaceitApi.Get("/players", key,
                new() { ["game"] = "cs2", ["game_player_id"] = steamId });

            var cs2 = player?["games"]?["cs2"];
            profile = new FaceitProfile(
                url: player?["avatar"]?.GetValue<string>() ?? "",
                nick: player?["nickname"]?.GetValue<string>() ?? "",
                lvl: (int)(cs2?["skill_level"]?.GetValue<long>() ?? 0),
                elo: (int)(cs2?["faceit_elo"]?.GetValue<long>() ?? 0));
        }
        catch (Exception)
        {
        }

        Write(steamId, profile);
        return profile;

        FaceitProfile? Read()
        {
            lock (Gate)
            {
                return Load()[steamId] is { } node ? Normalise(node) : null;
            }
        }
    }

    private static FaceitProfile Normalise(JsonNode node)
    {
        if (node is JsonValue value)
            return new FaceitProfile(value.TryGetValue(out string? s) ? s ?? "" : "", "", 0, 0);

        return new FaceitProfile(
            url: node["url"]?.GetValue<string>() ?? "",
            nick: node["nick"]?.GetValue<string>() ?? "",
            lvl: (int)(node["lvl"]?.GetValue<long>() ?? 0),
            elo: (int)(node["elo"]?.GetValue<long>() ?? 0));
    }

    private static JsonObject Load()
    {
        try { return JsonNode.Parse(File.ReadAllText(CacheFile)) as JsonObject ?? []; }
        catch (Exception) { return []; }
    }

    private static void Write(string steamId, FaceitProfile profile)
    {
        lock (Gate)
        {
            var cache = Load();
            cache[steamId] = new JsonObject
            {
                ["url"] = profile.url,
                ["nick"] = profile.nick,
                ["lvl"] = profile.lvl,
                ["elo"] = profile.elo,
            };
            try { File.WriteAllText(CacheFile, cache.ToJsonString()); }
            catch (Exception) {  }
        }
    }
}
