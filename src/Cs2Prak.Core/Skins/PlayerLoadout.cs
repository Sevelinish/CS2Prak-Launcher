using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace Cs2Prak.Core.Skins;

public static class PlayerLoadout
{
    private const string EmptySticker = "0;0;0;0;0;0;0";

    private static readonly string[] SkinColumns =
    [
        "weapon_team", "weapon_defindex", "weapon_paint_id",
        "weapon_wear", "weapon_seed", "weapon_nametag",
        "weapon_stattrak", "weapon_stattrak_count",
        "weapon_sticker_0", "weapon_sticker_1", "weapon_sticker_2",
        "weapon_sticker_3", "weapon_sticker_4",
    ];

    public static JsonObject Read(string steamId)
    {
        using var conn = SkinsDatabase.Open();

        var skins = Query(conn,
            $"SELECT {string.Join(", ", SkinColumns)} FROM wp_player_skins WHERE steamid=$id",
            steamId);

        var knives = Query(conn,
            "SELECT weapon_team, knife FROM wp_player_knife WHERE steamid=$id", steamId);

        var gloves = Query(conn,
            "SELECT weapon_team, weapon_defindex FROM wp_player_gloves WHERE steamid=$id", steamId);

        var agents = Query(conn,
            "SELECT agent_ct, agent_t FROM wp_player_agents WHERE steamid=$id", steamId);

        return new JsonObject
        {
            ["skins"] = skins,
            ["knives"] = knives,
            ["gloves"] = gloves,
            ["agents"] = agents.Count > 0 ? agents[0]!.DeepClone() : new JsonObject(),
        };
    }

    private static JsonArray Query(SqliteConnection conn, string sql, string steamId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$id", steamId);

        var rows = new JsonArray();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var row = new JsonObject();
            for (var i = 0; i < reader.FieldCount; i++)
            {
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i) switch
                {
                    long l => JsonValue.Create(l),
                    double d => JsonValue.Create(d),
                    string s => JsonValue.Create(s),
                    var other => JsonValue.Create(other.ToString()),
                };
            }
            rows.Add(row);
        }
        return rows;
    }

    public static void Save(string steamId, JsonObject payload)
    {
        using var conn = SkinsDatabase.Open();
        using var tx = conn.BeginTransaction();

        foreach (var node in Array(payload, "skins"))
        {
            if (node is not JsonObject skin) continue;

            var stickers = Array(skin, "stickers")
                .Select(s => s?.GetValue<string>() ?? EmptySticker)
                .ToList();
            while (stickers.Count < 5) stickers.Add(EmptySticker);

            Execute(conn, tx,
                """
                INSERT OR REPLACE INTO wp_player_skins
                    (steamid, weapon_team, weapon_defindex, weapon_paint_id,
                     weapon_wear, weapon_seed, weapon_nametag,
                     weapon_stattrak, weapon_stattrak_count,
                     weapon_sticker_0, weapon_sticker_1, weapon_sticker_2,
                     weapon_sticker_3, weapon_sticker_4)
                VALUES ($id, $team, $defindex, $paint, $wear, $seed, $nametag,
                        $stattrak, $stattrakCount, $s0, $s1, $s2, $s3, $s4)
                """,
                ("$id", steamId),
                ("$team", Int(skin, "team")),
                ("$defindex", Int(skin, "defindex")),
                ("$paint", Int(skin, "paint_id")),
                ("$wear", Double(skin, "wear")),
                ("$seed", Int(skin, "seed")),
                ("$nametag", Text(skin, "nametag") is { Length: > 0 } tag ? tag : null),
                ("$stattrak", Bool(skin, "stattrak") ? 1L : 0L),
                ("$stattrakCount", Int(skin, "stattrak_count")),
                ("$s0", stickers[0]), ("$s1", stickers[1]), ("$s2", stickers[2]),
                ("$s3", stickers[3]), ("$s4", stickers[4]));
        }

        foreach (var node in Array(payload, "knives"))
        {
            if (node is not JsonObject knife) continue;
            Execute(conn, tx,
                "INSERT OR REPLACE INTO wp_player_knife (steamid, weapon_team, knife) "
                + "VALUES ($id, $team, $knife)",
                ("$id", steamId),
                ("$team", Int(knife, "team")),
                ("$knife", Text(knife, "knife") ?? ""));
        }

        foreach (var node in Array(payload, "gloves"))
        {
            if (node is not JsonObject glove) continue;
            Execute(conn, tx,
                "INSERT OR REPLACE INTO wp_player_gloves (steamid, weapon_team, weapon_defindex) "
                + "VALUES ($id, $team, $defindex)",
                ("$id", steamId),
                ("$team", Int(glove, "team")),
                ("$defindex", Int(glove, "defindex")));
        }

        if (payload["agents"] is JsonObject agents)
        {
            Execute(conn, tx,
                "INSERT OR REPLACE INTO wp_player_agents (steamid, agent_ct, agent_t) "
                + "VALUES ($id, $ct, $t)",
                ("$id", steamId),
                ("$ct", Text(agents, "ct") is { Length: > 0 } ct ? ct : null),
                ("$t", Text(agents, "t") is { Length: > 0 } t ? t : null));
        }

        tx.Commit();
    }

    private static void Execute(SqliteConnection conn, SqliteTransaction tx, string sql,
                                params (string Name, object? Value)[] parameters)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private static JsonArray Array(JsonObject obj, string key) => obj[key] as JsonArray ?? [];

    private static long Int(JsonObject obj, string key) => obj[key] switch
    {
        JsonValue v when v.TryGetValue(out long l) => l,
        JsonValue v when v.TryGetValue(out double d) => (long)d,
        JsonValue v when v.TryGetValue(out string? s) && long.TryParse(s, out var p) => p,
        _ => 0,
    };

    private static double Double(JsonObject obj, string key) => obj[key] switch
    {
        JsonValue v when v.TryGetValue(out double d) => d,
        JsonValue v when v.TryGetValue(out long l) => l,
        JsonValue v when v.TryGetValue(out string? s)
            && double.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, out var p) => p,
        _ => 0,
    };

    private static bool Bool(JsonObject obj, string key) => obj[key] switch
    {
        JsonValue v when v.TryGetValue(out bool b) => b,
        JsonValue v when v.TryGetValue(out long l) => l != 0,
        JsonValue v when v.TryGetValue(out string? s) => s is "1" or "true" or "True",
        _ => false,
    };

    private static string? Text(JsonObject obj, string key) =>
        obj[key] is JsonValue v && v.TryGetValue(out string? s) ? s : null;
}
