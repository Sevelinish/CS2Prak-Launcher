using DemoFile;
using DemoFile.Game.Cs;

namespace Cs2Prak.Core.Demos;

public static class AdvancedStaging
{
    private static readonly TimeSpan KeepFor = TimeSpan.FromHours(2);

    private static string Directory_ => Path.Combine(Path.GetTempPath(), "cs2prak_adv");

    public readonly record struct Entry(string SteamId, string Name, int Team, string Clan);

    public static string Stage(string rawDemo)
    {
        System.IO.Directory.CreateDirectory(Directory_);
        Sweep();

        var id = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        var staged = PathFor(id);

        try { File.Move(rawDemo, staged, overwrite: true); }
        catch (IOException) { File.Copy(rawDemo, staged, overwrite: true); }

        return id;
    }

    public static string? Find(string? id)
    {
        if (id is null || id.Length is 0 or > 32 || !id.All(char.IsAsciiDigit)) return null;

        var path = PathFor(id);
        return File.Exists(path) ? path : null;
    }

    private static string PathFor(string id) => Path.Combine(Directory_, $"{id}.dem");

    private static void Sweep()
    {
        try
        {
            foreach (var file in System.IO.Directory.EnumerateFiles(Directory_))
            {
                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) <= KeepFor) continue;
                try { File.Delete(file); } catch (IOException) { }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    public static (string Map, List<Entry> Players) Roster(string path)
    {
        var demo = new CsDemoParser();
        var names = new Dictionary<ulong, string>();
        var order = new List<ulong>();

        var perFreeze = new List<Dictionary<ulong, (int Team, string Clan)>>();

        demo.Source1GameEvents.RoundFreezeEnd += _ =>
        {
            var sides = new Dictionary<ulong, (int Team, string Clan)>();
            foreach (var player in demo.Players)
            {
                if (player.SteamID == 0) continue;

                var clan = player.Clan.Value ?? "";
                if (clan.StartsWith("TEAM_", StringComparison.OrdinalIgnoreCase)) clan = clan[5..];
                sides[player.SteamID] = ((int)player.CSTeamNum, clan[..Math.Min(24, clan.Length)]);
            }
            perFreeze.Add(sides);
        };

        using var stream = File.OpenRead(path);
        var reader = DemoFileReader.Create(demo, stream);
        reader.StartReadingAsync(default).AsTask().GetAwaiter().GetResult();

        while (reader.MoveNextAsync(default).AsTask().GetAwaiter().GetResult())
        {
            if (demo.CurrentDemoTick.Value % CsDemoAnalyzer.TickRate != 0) continue;

            foreach (var player in demo.Players)
            {
                if (player.SteamID == 0 || player.PlayerName is not { Length: > 0 } name) continue;
                if (names.TryAdd(player.SteamID, name)) order.Add(player.SteamID);
            }
        }

        var middle = perFreeze.Count > 0
            ? perFreeze[perFreeze.Count / 2]
            : [];

        var players = order
            .Select(id =>
            {
                var (team, clan) = middle.GetValueOrDefault(id, (0, ""));
                return new Entry(id.ToString(), names[id], team, clan);
            })
            .Take(12)
            .ToList();

        return (demo.ServerInfo?.MapName ?? "", players);
    }
}
