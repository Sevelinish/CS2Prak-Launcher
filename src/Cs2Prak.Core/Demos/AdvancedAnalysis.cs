using System.Text.Json.Serialization;

namespace Cs2Prak.Core.Demos;

public static class AdvancedAnalysis
{
    private const double UnitsToMetres = 0.0254;

    private const int RecentShot = (int)(CsDemoAnalyzer.TickRate * 0.25);

    private const int BurstGap = CsDemoAnalyzer.TickRate / 2;

    private const int BurstReach = (int)(CsDemoAnalyzer.TickRate * 1.5);

    private const double StillSpeed = 55;

    private const double NadeRange = 900;

    private const int NadeWindow = CsDemoAnalyzer.TickRate * 4;

    public sealed class Report
    {
        [JsonPropertyName("ok")] public bool Ok => true;
        [JsonPropertyName("map")] public required string Map { get; init; }
        [JsonPropertyName("steamid")] public required string SteamId { get; init; }
        [JsonPropertyName("scale")] public double? Scale { get; init; }
        [JsonPropertyName("name")] public required string Name { get; init; }
        [JsonPropertyName("duels")] public required List<DuelReport> Duels { get; init; }
        [JsonPropertyName("agg")] public required object Aggregate { get; init; }
    }

    public sealed class DuelReport
    {
        [JsonPropertyName("round")] public int Round { get; init; }
        [JsonPropertyName("time")] public double Time { get; init; }
        [JsonPropertyName("won")] public bool Won { get; init; }
        [JsonPropertyName("opp")] public required string Opponent { get; init; }
        [JsonPropertyName("weapon")] public required string Weapon { get; init; }
        [JsonPropertyName("hs")] public bool Headshot { get; init; }
        [JsonPropertyName("dist")] public double? Distance { get; init; }
        [JsonPropertyName("hp")] public int? Health { get; init; }

        [JsonPropertyName("react")] public int? Reaction { get; init; }

        [JsonPropertyName("cross")] public double? Crosshair { get; init; }

        [JsonPropertyName("firstBullet")] public bool? FirstBullet { get; init; }

        [JsonPropertyName("cs")] public bool? CounterStrafed { get; init; }

        [JsonPropertyName("flashed")] public bool Flashed { get; init; }

        [JsonPropertyName("rp")] public double[]? Position { get; init; }
        [JsonPropertyName("ro")] public double[]? OpponentPosition { get; init; }
        [JsonPropertyName("rn")] public required List<NadeMark> Nades { get; init; }
    }

    public sealed class NadeMark
    {
        [JsonPropertyName("x")] public double X { get; init; }
        [JsonPropertyName("y")] public double Y { get; init; }
        [JsonPropertyName("t")] public required string Kind { get; init; }
    }

    public static Report Analyze(string path, string steamId)
    {
        var source = AdvancedSource.For(path);
        var me = ulong.TryParse(steamId, out var parsed) ? parsed : 0;
        var name = source.Names.GetValueOrDefault(me, "?");
        var calibration = RadarCalibration.For(source.Map);

        var duels = Duels(source, me, steamId, calibration);

        return new Report
        {
            Map = source.Map,
            SteamId = steamId,
            Scale = calibration?.Scale,
            Name = name,
            Duels = duels,
            Aggregate = duels.Count == 0 ? new Dictionary<string, object?>() : Aggregate(duels),
        };
    }

    private static List<DuelReport> Duels(AdvancedSource source, ulong me, string steamId,
                                          RadarCalibration? calibration)
    {
        var mine = source.Shots.GetValueOrDefault(me) ?? [];
        var reports = new List<DuelReport>();

        foreach (var death in source.Deaths)
        {
            if (death.Tick < source.MatchStart) continue;
            if (death.Attacker == death.Victim) continue;
            if (death.Attacker != me && death.Victim != me) continue;

            var won = death.Attacker == me;
            var opponent = won ? death.Victim : death.Attacker;

            if (opponent == 0) continue;

            var tick = death.Tick;
            source.States.TryGetValue((me, tick), out var mineNow);
            source.States.TryGetValue((opponent, tick), out var theirsNow);

            if (mineNow.Team is { } a && theirsNow.Team is { } b && a == b) continue;

            reports.Add(Measure(source, me, opponent, death, won, mine, calibration));
        }
        return reports;
    }

    private static DuelReport Measure(AdvancedSource source, ulong me, ulong opponent,
                                      AdvancedSource.Death death, bool won, List<int> mine,
                                      RadarCalibration? calibration)
    {
        var tick = death.Tick;
        var hits = source.Hits.GetValueOrDefault((me, opponent)) ?? [];

        var appeared = Appeared(source, opponent, tick, mine);
        var firstShot = BurstStart(mine, tick);

        double? distance = death.Distance ?? Separation(source, me, opponent, tick);

        var anchor = firstShot ?? tick;
        source.States.TryGetValue((me, anchor), out var mineThen);
        source.States.TryGetValue((opponent, anchor), out var theirsThen);
        var hasMine = source.States.ContainsKey((me, anchor));
        var hasTheirs = source.States.ContainsKey((opponent, anchor));

        AdvancedSource.State? atContact = appeared is { } seen
            && source.States.TryGetValue((me, seen), out var contact) ? contact : null;

        return new DuelReport
        {
            Round = RoundNumber(source, tick),
            Time = RoundTime(source, tick),
            Won = won,
            Opponent = source.Names.GetValueOrDefault(opponent, "?"),
            Weapon = death.Weapon,
            Headshot = death.Headshot,
            Distance = distance,
            Health = atContact?.Health,
            Reaction = Reaction(appeared, hits, tick),
            Crosshair = Crosshair(source, me, opponent, appeared),
            FirstBullet = firstShot is { } shot ? hits.Contains(shot) || hits.Contains(shot + 1) : null,
            CounterStrafed = CounterStrafed(source, me, firstShot),
            Flashed = atContact is { FlashDuration: > 1.0 },
            Position = hasMine ? ToPixels(calibration, mineThen.X, mineThen.Y) : null,
            OpponentPosition = hasTheirs ? ToPixels(calibration, theirsThen.X, theirsThen.Y) : null,
            Nades = NearbyNades(source, calibration, tick,
                                hasMine ? mineThen : null, hasTheirs ? theirsThen : null),
        };
    }

    private static int? Appeared(AdvancedSource source, ulong opponent, int tick, List<int> mine)
    {
        bool? before = null;

        for (var t = Math.Max(0, tick - AdvancedSource.Window); t <= tick; t++)
        {
            if (!source.States.TryGetValue((opponent, t), out var state)) continue;

            if (before is false && state.Spotted)
            {
                var last = int.MinValue;
                foreach (var shot in mine)
                {
                    if (shot >= t) break;
                    last = shot;
                }
                if (last == int.MinValue || t - last >= RecentShot) return t;
            }
            before = state.Spotted;
        }
        return null;
    }

    private static int? Reaction(int? appeared, HashSet<int> hits, int tick)
    {
        if (appeared is not { } seen) return null;

        var landed = int.MaxValue;
        foreach (var hit in hits)
            if (hit >= seen && hit <= tick + 6 && hit < landed) landed = hit;
        if (landed == int.MaxValue) return null;

        var seconds = (landed - seen) / (double)CsDemoAnalyzer.TickRate;
        return seconds is >= 0.05 and <= 3.0
            ? (int)Math.Round(seconds * 1000, MidpointRounding.AwayFromZero)
            : null;
    }

    private static double? Crosshair(AdvancedSource source, ulong me, ulong opponent, int? appeared)
    {
        if (appeared is not { } seen) return null;
        if (!source.States.TryGetValue((me, seen), out var mine)) return null;
        if (!source.States.TryGetValue((opponent, seen), out var theirs)) return null;

        const double ToRadians = Math.PI / 180.0;
        var pitchCosine = Math.Cos(mine.Pitch * ToRadians);
        var aimX = pitchCosine * Math.Cos(mine.Yaw * ToRadians);
        var aimY = pitchCosine * Math.Sin(mine.Yaw * ToRadians);
        var aimZ = -Math.Sin(mine.Pitch * ToRadians);

        var dx = theirs.X - mine.X;
        var dy = theirs.Y - mine.Y;
        var dz = theirs.Z - mine.Z;
        var length = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        if (length <= 1) return null;

        var dot = (aimX * dx + aimY * dy + aimZ * dz) / length;
        return Math.Round(Math.Acos(Math.Clamp(dot, -1.0, 1.0)) * 180.0 / Math.PI, 1);
    }

    private static int? BurstStart(List<int> mine, int tick)
    {
        var last = -1;
        var index = -1;
        for (var i = 0; i < mine.Count; i++)
        {
            if (mine[i] > tick + 6) break;
            last = mine[i];
            index = i;
        }
        if (index < 0 || tick - last > BurstReach) return null;

        while (index > 0 && mine[index] - mine[index - 1] <= BurstGap) index--;
        return mine[index];
    }

    private static bool? CounterStrafed(AdvancedSource source, ulong me, int? firstShot)
    {
        if (firstShot is not { } shot) return null;
        if (!source.States.TryGetValue((me, shot), out var now)) return null;
        if (!source.States.TryGetValue((me, shot - 1), out var before)) return null;

        var speed = Math.Sqrt(Math.Pow(now.X - before.X, 2) + Math.Pow(now.Y - before.Y, 2))
                    * CsDemoAnalyzer.TickRate;
        return speed < StillSpeed;
    }

    private static double? Separation(AdvancedSource source, ulong me, ulong opponent, int tick)
    {
        if (!source.States.TryGetValue((me, tick), out var mine)) return null;
        if (!source.States.TryGetValue((opponent, tick), out var theirs)) return null;

        var dx = theirs.X - mine.X;
        var dy = theirs.Y - mine.Y;
        var dz = theirs.Z - mine.Z;
        return Math.Round(Math.Sqrt(dx * dx + dy * dy + dz * dz) * UnitsToMetres, 1);
    }

    private static List<NadeMark> NearbyNades(AdvancedSource source, RadarCalibration? calibration,
                                              int tick, AdvancedSource.State? mine,
                                              AdvancedSource.State? theirs)
    {
        var marks = new List<NadeMark>();
        if (calibration is null || (mine is null && theirs is null)) return marks;

        foreach (var nade in source.Nades)
        {
            if (Math.Abs(nade.Tick - tick) > NadeWindow) continue;

            if (!Near(mine, nade) && !Near(theirs, nade)) continue;

            var (x, y) = calibration.ToPixels(nade.X, nade.Y);
            marks.Add(new NadeMark { X = x, Y = y, Kind = nade.Kind });
        }
        return marks;

        static bool Near(AdvancedSource.State? state, AdvancedSource.Nade nade) =>
            state is { } s
            && Math.Sqrt(Math.Pow(nade.X - s.X, 2) + Math.Pow(nade.Y - s.Y, 2)) < NadeRange;
    }

    private static double[]? ToPixels(RadarCalibration? calibration, double x, double y)
    {
        if (calibration is null || double.IsNaN(x) || double.IsNaN(y)) return null;
        var (px, py) = calibration.ToPixels(x, y);
        return [px, py];
    }

    private static int RoundNumber(AdvancedSource source, int tick) =>
        source.FreezeEnds.Count(freeze => tick >= freeze);

    private static double RoundTime(AdvancedSource source, int tick)
    {
        var start = source.MatchStart;
        foreach (var freeze in source.FreezeEnds)
            if (freeze <= tick) start = freeze;

        return Math.Round((tick - start) / (double)CsDemoAnalyzer.TickRate, 1);
    }

    private static object Aggregate(List<DuelReport> duels)
    {
        var won = duels.Where(d => d.Won).ToList();
        var lost = duels.Where(d => !d.Won).ToList();
        var distances = duels.Where(d => d.Distance is not null).Select(d => d.Distance!.Value).ToList();

        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["duels"] = duels.Count,
            ["won"] = won.Count,
            ["lost"] = lost.Count,
            ["winPct"] = duels.Count > 0
                ? (int)Math.Round(won.Count * 100.0 / duels.Count, MidpointRounding.AwayFromZero)
                : 0,

            ["reactMed"] = Median(duels, d => d.Reaction),
            ["reactWon"] = Median(won, d => d.Reaction),
            ["reactLost"] = Median(lost, d => d.Reaction),

            ["crossMed"] = Median(duels, d => d.Crosshair),
            ["crossWon"] = Median(won, d => d.Crosshair),
            ["crossLost"] = Median(lost, d => d.Crosshair),

            ["firstBulletPct"] = Percent(duels, d => d.FirstBullet) ?? 0,
            ["fbWonPct"] = Percent(won, d => d.FirstBullet),
            ["fbLostPct"] = Percent(lost, d => d.FirstBullet),

            ["csPct"] = Percent(duels, d => d.CounterStrafed) ?? 0,
            ["csWon"] = Percent(won, d => d.CounterStrafed),
            ["csLost"] = Percent(lost, d => d.CounterStrafed),

            ["hsPct"] = won.Count > 0
                ? (int)Math.Round(won.Count(d => d.Headshot) * 100.0 / won.Count,
                                  MidpointRounding.AwayFromZero)
                : 0,
            ["avgDist"] = Median(distances),
            ["flashedLost"] = lost.Count(d => d.Flashed),

            ["reactN"] = duels.Count(d => d.Reaction is not null),
            ["reactWonN"] = won.Count(d => d.Reaction is not null),
            ["reactLostN"] = lost.Count(d => d.Reaction is not null),

            ["crossN"] = duels.Count(d => d.Crosshair is not null),
            ["crossWonN"] = won.Count(d => d.Crosshair is not null),
            ["crossLostN"] = lost.Count(d => d.Crosshair is not null),

            ["fbN"] = duels.Count(d => d.FirstBullet is not null),
            ["fbWonN"] = won.Count(d => d.FirstBullet is not null),
            ["fbLostN"] = lost.Count(d => d.FirstBullet is not null),

            ["csN"] = duels.Count(d => d.CounterStrafed is not null),
            ["csWonN"] = won.Count(d => d.CounterStrafed is not null),
            ["csLostN"] = lost.Count(d => d.CounterStrafed is not null),

            ["distN"] = distances.Count,
        };
    }

    private static double? Median(List<DuelReport> duels, Func<DuelReport, double?> pick) =>
        Median(duels.Select(pick).Where(v => v is not null).Select(v => v!.Value).ToList());

    private static double? Median(List<DuelReport> duels, Func<DuelReport, int?> pick) =>
        Median(duels.Select(pick).Where(v => v is not null).Select(v => (double)v!.Value).ToList());

    private static double? Median(List<double> values)
    {
        if (values.Count == 0) return null;
        values.Sort();

        var middle = values.Count / 2;
        return values.Count % 2 == 1
            ? values[middle]
            : Math.Round((values[middle - 1] + values[middle]) / 2, 1);
    }

    private static int? Percent(List<DuelReport> duels, Func<DuelReport, bool?> pick)
    {
        var values = duels.Select(pick).Where(v => v is not null).Select(v => v!.Value).ToList();
        return values.Count == 0
            ? null
            : (int)Math.Round(values.Count(v => v) * 100.0 / values.Count, MidpointRounding.AwayFromZero);
    }
}
