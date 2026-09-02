using Microsoft.Data.Sqlite;

namespace Cs2Prak.Core.Skins;

public static class SkinsDatabase
{
    public static string ConnectionString => new SqliteConnectionStringBuilder
    {
        DataSource = AppPaths.DbPath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Cache = SqliteCacheMode.Default,
    }.ToString();

    public static SqliteConnection Open()
    {
        var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        Execute(conn, "PRAGMA journal_mode=WAL");
        Execute(conn, "PRAGMA busy_timeout=5000");
        return conn;
    }

    private static void Execute(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private const string SkinsDdl = """
        CREATE TABLE IF NOT EXISTS wp_player_skins (
            steamid               TEXT    NOT NULL,
            weapon_team           INTEGER NOT NULL DEFAULT 0,
            weapon_defindex       INTEGER NOT NULL DEFAULT 0,
            weapon_paint_id       INTEGER NOT NULL DEFAULT 0,
            weapon_wear           REAL    NOT NULL DEFAULT 0.001,
            weapon_seed           INTEGER NOT NULL DEFAULT 0,
            weapon_nametag        TEXT             DEFAULT NULL,
            weapon_stattrak       INTEGER NOT NULL DEFAULT 0,
            weapon_stattrak_count INTEGER NOT NULL DEFAULT 0,
            weapon_sticker_0      TEXT    NOT NULL DEFAULT '0;0;0;0;0;0;0',
            weapon_sticker_1      TEXT    NOT NULL DEFAULT '0;0;0;0;0;0;0',
            weapon_sticker_2      TEXT    NOT NULL DEFAULT '0;0;0;0;0;0;0',
            weapon_sticker_3      TEXT    NOT NULL DEFAULT '0;0;0;0;0;0;0',
            weapon_sticker_4      TEXT    NOT NULL DEFAULT '0;0;0;0;0;0;0',
            weapon_keychain       TEXT    NOT NULL DEFAULT '0;0;0;0;0',
            PRIMARY KEY (steamid, weapon_team, weapon_defindex)
        );
        """;

    private const string RestOfSchema = """
        CREATE TABLE IF NOT EXISTS wp_player_knife (
            steamid     TEXT    NOT NULL,
            weapon_team INTEGER NOT NULL DEFAULT 0,
            knife       TEXT    NOT NULL DEFAULT '',
            PRIMARY KEY (steamid, weapon_team)
        );
        CREATE TABLE IF NOT EXISTS wp_player_gloves (
            steamid         TEXT    NOT NULL,
            weapon_team     INTEGER NOT NULL DEFAULT 0,
            weapon_defindex INTEGER NOT NULL DEFAULT 0,
            PRIMARY KEY (steamid, weapon_team)
        );
        CREATE TABLE IF NOT EXISTS wp_player_agents (
            steamid  TEXT NOT NULL PRIMARY KEY,
            agent_ct TEXT DEFAULT NULL,
            agent_t  TEXT DEFAULT NULL
        );
        CREATE TABLE IF NOT EXISTS wp_player_music (
            steamid     TEXT    NOT NULL,
            weapon_team INTEGER NOT NULL DEFAULT 0,
            music_id    INTEGER NOT NULL DEFAULT 0,
            PRIMARY KEY (steamid, weapon_team)
        );
        CREATE TABLE IF NOT EXISTS wp_player_pins (
            steamid     TEXT    NOT NULL,
            weapon_team INTEGER NOT NULL DEFAULT 0,
            id          INTEGER NOT NULL DEFAULT 0,
            PRIMARY KEY (steamid, weapon_team)
        );
        """;

    public static void EnsureSchema()
    {
        using var conn = Open();
        MigrateSkinsForTeams(conn);
        Execute(conn, SkinsDdl + RestOfSchema);
    }

    private static void MigrateSkinsForTeams(SqliteConnection conn)
    {
        string? existing;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText =
                "SELECT sql FROM sqlite_master WHERE type='table' AND name='wp_player_skins'";
            existing = cmd.ExecuteScalar() as string;
        }

        if (existing is null || existing.Contains("weapon_team", StringComparison.Ordinal)) return;

        try
        {
            Execute(conn, "ALTER TABLE wp_player_skins RENAME TO _wp_player_skins_bak");
            Execute(conn, SkinsDdl);

            foreach (var team in new[] { 2, 3 })
            {
                try
                {
                    Execute(conn, $"""
                        INSERT OR IGNORE INTO wp_player_skins
                            (steamid, weapon_team, weapon_defindex, weapon_paint_id, weapon_wear,
                             weapon_seed, weapon_nametag, weapon_stattrak, weapon_stattrak_count)
                        SELECT steamid, {team}, weapon_defindex, weapon_paint_id, weapon_wear,
                               weapon_seed, weapon_nametag, weapon_stattrak, weapon_stattrak_count
                        FROM _wp_player_skins_bak
                        """);
                }
                catch (SqliteException)
                {
                }
            }

            Execute(conn, "DROP TABLE IF EXISTS _wp_player_skins_bak");
        }
        catch (SqliteException)
        {
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText =
                    "SELECT 1 FROM sqlite_master WHERE type='table' AND name='wp_player_skins'";
                if (cmd.ExecuteScalar() is null)
                    Execute(conn, "ALTER TABLE _wp_player_skins_bak RENAME TO wp_player_skins");
            }
            catch (SqliteException) { }
        }
    }
}
