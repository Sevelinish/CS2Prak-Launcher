using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Cs2Prak.Core.Demos;

public static class ScoutMetrics
{
    private const int TradeSeconds = 5;

    private const int SideField = 4;
    private const int AliveField = 5;

    public sealed class Result
    {
        [JsonPropertyName("tradeWindow")] public int TradeWindow { get; init; }
        [JsonPropertyName("players")] public required List<Player> Players { get; init; }

        [JsonPropertyName("matrix")] public required int[][] Matrix { get; init; }
    }

    public sealed class Player
    {
        [JsonPropertyName("openW")] public int OpeningsWon { get; set; }
        [JsonPropertyName("openL")] public int OpeningsLost { get; set; }

        [JsonPropertyName("openLTraded")] public int OpeningsLostTraded { get; set; }

        [JsonPropertyName("tradedFor")] public int TradedFor { get; set; }
        [JsonPropertyName("tradedBy")] public int TradedBy { get; set; }

        [JsonPropertyName("clutch")] public Dictionary<string, int[]> Clutch { get; } = [];

        [JsonPropertyName("rounds")] public List<RoundLine> Rounds { get; } = [];
        [JsonPropertyName("util")] public Utility Util { get; } = new();
        [JsonPropertyName("buys")] public Buys Buys { get; } = new();
    }

    public sealed class RoundLine
    {
        [JsonPropertyName("n")] public int Number { get; init; }
        [JsonPropertyName("k")] public int Kills { get; init; }
        [JsonPropertyName("d")] public int Died { get; init; }
        [JsonPropertyName("open")] public int Opened { get; init; }
        [JsonPropertyName("openDied")] public int OpenedAgainst { get; init; }
        [JsonPropertyName("won")] public int Won { get; init; }
    }

    public sealed class Utility
    {
        [JsonPropertyName("smoke")] public int Smoke { get; set; }
        [JsonPropertyName("flash")] public int Flash { get; set; }
        [JsonPropertyName("he")] public int He { get; set; }
        [JsonPropertyName("molotov")] public int Molotov { get; set; }

        [JsonPropertyName("flashHits")] public int FlashHits { get; set; }
        [JsonPropertyName("flashBlindTime")] public double FlashBlindTime { get; set; }

        [JsonPropertyName("blindedTime")] public double BlindedTime { get; set; }
    }

    public sealed class Buys
    {
        [JsonPropertyName("eco")] public int Eco { get; set; }
        [JsonPropertyName("force")] public int Force { get; set; }
        [JsonPropertyName("full")] public int Full { get; set; }
    }

    public static Result Build(JsonNode blob)
    {
        var players = blob["players"]?.AsArray() ?? [];
        var kills = blob["kills"]?.AsArray() ?? [];
        var rounds = blob["rounds"]?.AsArray() ?? [];
        var frames = blob["frames"]?.AsArray() ?? [];

        var n = players.Count;
        var fps = Int(blob["fps"]) is var f and > 0 ? f : 8;
        var frameCount = Int(blob["nFrames"]) is var nf and > 0 ? nf : frames.Count;
        var window = TradeSeconds * fps;

        var grid = FrameTable.From(frames, n);
        var out_ = Enumerable.Range(0, n).Select(_ => new Player()).ToList();
        var matrix = Enumerable.Range(0, n).Select(_ => new int[n]).ToArray();

        var duels = kills.Select(Duel.From).ToList();

        foreach (var duel in duels)
            if (duel.Killer is { } a && duel.Victim is { } v && a < n && v < n)
                matrix[a][v]++;

        Trades(duels, out_, grid, n, window);
        PerRound(duels, rounds, out_, grid, n, frameCount, window);
        Utilities(blob, players, out_, grid, n, fps);
        BuyDiscipline(blob, out_, n);

        foreach (var player in out_)
        {
            player.Util.FlashBlindTime = Math.Round(player.Util.FlashBlindTime, 1);
            player.Util.BlindedTime = Math.Round(player.Util.BlindedTime, 1);
        }

        return new Result { TradeWindow = TradeSeconds, Players = out_, Matrix = matrix };
    }

    private static void Trades(List<Duel> duels, List<Player> out_, FrameTable grid,
                               int n, int window)
    {
        foreach (var duel in duels)
        {
            if (duel.Killer is not { } a || duel.Victim is not { } v || a >= n || v >= n) continue;
            var side = grid.SideAt(duel.Frame, v);

            foreach (var answer in duels)
            {
                if (answer.Frame - duel.Frame is var gap && (gap <= 0 || gap > window)) continue;
                if (answer.Victim != a) continue;
                if (answer.Killer is not { } avenger || avenger >= n) continue;

                if (grid.SideAt(answer.Frame, avenger) != side) continue;
                out_[v].TradedFor++;
                out_[avenger].TradedBy++;
                break;
            }
        }
    }

    private static void PerRound(List<Duel> duels, JsonArray rounds, List<Player> out_,
                                 FrameTable grid, int n, int frameCount, int window)
    {
        foreach (var round in rounds)
        {
            if (round is null) continue;

            var freeze = Int(round["freeze"]);
            var end = Int(round["end"]);
            var last = Math.Min(end, frameCount - 1);
            var winner = Text(round["wside"]) == "CT" ? 1 : 0;

            var inRound = duels.Where(d => d.Frame >= freeze && d.Frame <= end).ToList();
            var opener = inRound.Count > 0 ? inRound.MinBy(d => d.Frame) : null;

            if (opener is not null)
            {
                if (opener.Killer is { } a && a < n) out_[a].OpeningsWon++;
                if (opener.Victim is { } v && v < n)
                {
                    out_[v].OpeningsLost++;
                    if (inRound.Any(d => d.Frame - opener.Frame is var g && g > 0
                                         && g <= window && d.Victim == opener.Killer))
                        out_[v].OpeningsLostTraded++;
                }
            }

            Clutches(winner, freeze, last, out_, grid, n);

            for (var i = 0; i < n; i++)
            {
                var kills = inRound.Count(d => d.Killer == i);
                out_[i].Rounds.Add(new RoundLine
                {
                    Number = Int(round["n"]),
                    Kills = kills,
                    Died = inRound.Any(d => d.Victim == i) ? 1 : 0,
                    Opened = opener?.Killer == i ? 1 : 0,
                    OpenedAgainst = opener?.Victim == i ? 1 : 0,
                    Won = grid.SideAt(freeze, i) == winner ? 1 : 0,
                });
            }
        }
    }

    private static void Clutches(int winner, int freeze, int last, List<Player> out_,
                                 FrameTable grid, int n)
    {
        var counted = new bool[2];

        for (var f = freeze; f < last; f++)
        {
            if (!grid.Has(f)) continue;

            Span<int> alive = [0, 0];
            Span<int> only = [-1, -1];
            for (var i = 0; i < n; i++)
            {
                if (!grid.IsAlive(f, i) || grid.SideAt(f, i) is not { } side) continue;
                alive[side]++;
                only[side] = i;
            }

            for (var side = 0; side < 2; side++)
            {
                var foes = alive[1 - side];
                if (alive[side] != 1 || foes < 1 || counted[side]) continue;

                counted[side] = true;
                var who = only[side];
                if (who >= n) continue;

                var key = $"1v{Math.Min(foes, 5)}";
                if (!out_[who].Clutch.TryGetValue(key, out var tally))
                    out_[who].Clutch[key] = tally = [0, 0];
                tally[0]++;
                if (side == winner) tally[1]++;
            }
            if (counted[0] && counted[1]) break;
        }
    }

    private static void Utilities(JsonNode blob, JsonArray players, List<Player> out_,
                                  FrameTable grid, int n, int fps)
    {
        var bySteamId = new Dictionary<string, int>(StringComparer.Ordinal);
        var byName = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < players.Count; i++)
        {
            if (Text(players[i]?["steamid"]) is { Length: > 0 } id) bySteamId.TryAdd(id, i);
            if (Text(players[i]?["name"]) is { Length: > 0 } name) byName.TryAdd(name, i);
        }

        var blinds = (blob["blinds"]?.AsArray() ?? [])
            .Where(b => b?["i"] is not null)
            .Select(b => (Player: Int(b!["i"]), Frame: Number(b["f"]),
                          Seconds: Math.Max(0.0, Number(b["end"]) - Number(b["f"])) / fps))
            .ToList();

        foreach (var flight in blob["flights"]?.AsArray() ?? [])
        {
            if (flight is null) continue;

            int? thrower = null;
            if (Text(flight["sid"]) is { Length: > 0 } sid && bySteamId.TryGetValue(sid, out var bySid))
                thrower = bySid;
            else if (Text(flight["by"]) is { } by && byName.TryGetValue(by, out var byNm))
                thrower = byNm;
            if (thrower is not { } i || i >= n) continue;

            var kind = Text(flight["t"]);
            var util = out_[i].Util;
            switch (kind)
            {
                case "smoke": util.Smoke++; break;
                case "flash": util.Flash++; break;
                case "he": util.He++; break;
                case "molotov": util.Molotov++; break;
                default: continue;
            }
            if (kind != "flash") continue;

            var path = flight["p"]?.AsArray();
            if (path is null || path.Count == 0) continue;

            var burst = Number(path[^1]?[0]);
            var throwerSide = grid.SideAt((int)burst, i);

            foreach (var (blinded, frame, seconds) in blinds)
            {
                if (frame < burst - 1 || frame > burst + 2) continue;
                if (blinded >= n) continue;
                if (grid.SideAt((int)frame, blinded) == throwerSide) continue;

                util.FlashHits++;
                util.FlashBlindTime += seconds;
            }
        }

        foreach (var (blinded, _, seconds) in blinds)
            if (blinded < n) out_[blinded].Util.BlindedTime += seconds;
    }

    private static void BuyDiscipline(JsonNode blob, List<Player> out_, int n)
    {
        foreach (var (_, perPlayer) in blob["econ"]?.AsObject() ?? [])
        {
            foreach (var (index, values) in perPlayer?.AsObject() ?? [])
            {
                if (!int.TryParse(index, out var i) || i >= n) continue;
                if (values?.AsArray() is not { Count: >= 5 } row) continue;

                var spend = Int(row[4]);
                var buys = out_[i].Buys;
                if (spend < 2000) buys.Eco++;
                else if (spend < 3900) buys.Force++;
                else buys.Full++;
            }
        }
    }

    private sealed record Duel(int Frame, int? Killer, int? Victim)
    {
        public static Duel From(JsonNode? kill) => new(
            Int(kill?["f"]), Slot(kill?["a"]), Slot(kill?["v"]));

        private static int? Slot(JsonNode? node) => node is null ? null : Int(node);
    }

    private sealed class FrameTable
    {
        private sbyte[] _side = [];
        private bool[] _alive = [];
        private int _players;
        private int _count;

        public static FrameTable From(JsonArray frames, int players)
        {
            var table = new FrameTable
            {
                _players = players,
                _count = frames.Count,
                _side = new sbyte[frames.Count * players],
                _alive = new bool[frames.Count * players],
            };
            Array.Fill(table._side, (sbyte)-1);

            for (var f = 0; f < frames.Count; f++)
            {
                if (frames[f]?.AsArray() is not { } row) continue;
                for (var i = 0; i < players && i < row.Count; i++)
                {
                    if (row[i]?.AsArray() is not { Count: > AliveField } slot) continue;
                    var side = Int(slot[SideField]);
                    table._side[f * players + i] = side is 0 or 1 ? (sbyte)side : (sbyte)-1;
                    table._alive[f * players + i] = Int(slot[AliveField]) != 0;
                }
            }
            return table;
        }

        public bool Has(int frame) => frame >= 0 && frame < _count;

        public int? SideAt(int frame, int player)
        {
            if (!Has(frame) || player < 0 || player >= _players) return null;
            var side = _side[frame * _players + player];
            return side < 0 ? null : side;
        }

        public bool IsAlive(int frame, int player) =>
            Has(frame) && player >= 0 && player < _players && _alive[frame * _players + player];
    }

    private static int Int(JsonNode? node) => (int)Math.Round(Number(node));

    private static double Number(JsonNode? node)
    {
        if (node is not JsonValue value) return 0;
        if (value.TryGetValue<double>(out var d)) return d;
        if (value.TryGetValue<bool>(out var b)) return b ? 1 : 0;
        return 0;
    }

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var s) ? s : null;
}
