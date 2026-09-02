using System.Text.Json;

namespace Cs2Prak.Core.Demos;

public static partial class CsDemoAnalyzer
{
    private static class PlayerStatistics
    {
        private static readonly HashSet<string> UtilityWeapons = new(StringComparer.Ordinal)
            { "hegrenade", "inferno", "molotov", "incgrenade" };

        private static readonly HashSet<string> Thrown = new(StringComparer.Ordinal)
            { "hegrenade", "flashbang", "smokegrenade", "molotov", "incgrenade", "decoy" };

        private static string? HitBucket(int group) => group switch
        {
            1 or 8 => "head",
            2 => "chest",
            3 => "stomach",
            4 or 5 => "arm",
            6 or 7 => "leg",
            _ => null,
        };

        internal sealed class Stat
        {
            public required string Name;
            public required string SteamId;

            public int Kills, Deaths, Assists, Headshots;
            public int Damage, DamageTaken;
            public int Shots, Hits;
            public int OpeningKills, OpeningDeaths;
            public int TradeKills, TradedFor;
            public int UtilityDamage, FlashesThrown, EnemiesFlashed, FlashAssists;
            public int Mvps;
            public double TalkSeconds;
            public int KastRounds;
            public int FirstShots, FirstHits;

            public int? Reaction;

            public readonly int[] Multi = new int[5];
            public readonly int[] ClutchWon = new int[5];
            public readonly int[] ClutchLost = new int[5];
            public readonly Dictionary<string, int> HitGroups = new(StringComparer.Ordinal)
                { ["head"] = 0, ["chest"] = 0, ["stomach"] = 0, ["arm"] = 0, ["leg"] = 0 };
            public readonly Dictionary<string, int> WeaponKills = new(StringComparer.Ordinal);
        }

        public static List<Stat> Build(List<PlayerSlot> players, List<Kill> kills, List<Round> rounds,
                                       World world, DemoEventLog log, FrameGrid grid,
                                       List<VoiceTrack.Clip> voice, int fps)
        {
            var count = players.Count;
            var roundCount = Math.Max(1, rounds.Count);

            var stats = players
                .Select(p => new Stat { Name = p.Name, SteamId = p.SteamId.ToString() })
                .ToList();

            var slot = new Dictionary<ulong, int>();
            for (var i = 0; i < count; i++) slot[players[i].SteamId] = i;
            int? SlotOf(ulong id) => id != 0 && slot.TryGetValue(id, out var i) ? i : null;

            int? SideAt(int frame, int player) => CsDemoAnalyzer.SideAt(world, frame, player);

            PerRound(stats, kills, rounds, count, fps, SideAt);
            Damage(stats, log, grid, SlotOf, SideAt);
            var shotTicks = ShotsFired(stats, log, SlotOf);
            FirstShotAccuracy(stats, shotTicks, HitTicks(log, SlotOf, SideAt, grid));
            Flashes(stats, log, grid, kills, fps, SlotOf, SideAt);

            Reaction(stats, world, log, SlotOf);

            foreach (var mvp in log.Mvps)
                if (SlotOf(mvp) is { } i) stats[i].Mvps++;

            foreach (var clip in voice)
                if (clip.Slot >= 0 && clip.Slot < count) stats[clip.Slot].TalkSeconds += clip.Duration;

            return stats;
        }

        private const int PreFire = 16;

        private static void Reaction(List<Stat> stats, World world, DemoEventLog log,
                                     Func<ulong, int?> slotOf)
        {
            var shots = new Dictionary<int, List<int>>();
            foreach (var shot in log.Shots)
            {
                if (slotOf(shot.Player) is not { } i) continue;
                var w = shot.Weapon;
                if (w.Contains("grenade", StringComparison.Ordinal)
                    || w.Contains("flashbang", StringComparison.Ordinal)
                    || w.Contains("molotov", StringComparison.Ordinal)
                    || w.Contains("decoy", StringComparison.Ordinal)
                    || w.Contains("knife", StringComparison.Ordinal)
                    || w.Contains("bayonet", StringComparison.Ordinal)) continue;

                if (!shots.TryGetValue(i, out var list)) shots[i] = list = [];
                list.Add(shot.Tick);
            }
            foreach (var list in shots.Values) list.Sort();

            var duels = Duels(world, log, slotOf, shots);

            foreach (var (player, list) in duels)
            {
                if (list.Count < 3) continue;
                list.Sort();
                stats[player].Reaction = (int)Math.Round(list[list.Count / 2] * 1000,
                                                        MidpointRounding.AwayFromZero);
            }
        }

        private static Dictionary<int, List<double>> Duels(
            World world, DemoEventLog log, Func<ulong, int?> slotOf,
            Dictionary<int, List<int>> shots)
        {
            const int Window = TickRate * 3;
            var duels = new Dictionary<int, List<double>>();

            foreach (var death in log.Deaths)
            {
                if (death.Tick < log.MatchStart) continue;
                if (slotOf(death.Attacker) is not { } killer) continue;
                if (slotOf(death.Victim) is not { } victim) continue;
                if (!world.Spotted.TryGetValue(victim, out var sightings)) continue;

                var seen = -1;
                foreach (var tick in sightings)
                {
                    if (tick > death.Tick) break;
                    seen = tick;
                }
                if (seen < 0 || seen < death.Tick - Window) continue;

                if (!shots.TryGetValue(killer, out var fired)) continue;
                var first = -1;
                var alreadyFiring = false;
                foreach (var tick in fired)
                {
                    if (tick < seen)
                    {
                        alreadyFiring = tick >= seen - PreFire;
                        continue;
                    }
                    if (tick > death.Tick + 8) break;
                    first = tick;
                    break;
                }
                if (first < 0 || alreadyFiring) continue;

                var seconds = (first - seen) / (double)TickRate;
                if (seconds is < 0 or > 3) continue;

                if (!duels.TryGetValue(killer, out var list)) duels[killer] = list = [];
                list.Add(seconds);
            }
            return duels;
        }

        private static void PerRound(List<Stat> stats, List<Kill> kills, List<Round> rounds,
                                     int count, int fps, Func<int, int, int?> sideAt)
        {
            var tradeWindow = fps * 5;

            for (var ri = 0; ri < rounds.Count; ri++)
            {
                var round = rounds[ri];
                var inRound = kills
                    .Where(k => k.Frame >= round.Start && k.Frame <= round.End)
                    .OrderBy(k => k.Frame)
                    .ToList();

                var killers = new HashSet<int>();
                var victims = new HashSet<int>();
                var assisters = new HashSet<int>();
                var traded = new HashSet<int>();
                var perKiller = new Dictionary<int, int>();

                foreach (var kill in inRound)
                {
                    if (kill.Attacker is { } a)
                    {
                        stats[a].Kills++;
                        killers.Add(a);
                        perKiller[a] = perKiller.GetValueOrDefault(a) + 1;
                        if (kill.Headshot) stats[a].Headshots++;

                        var weapon = kill.Weapon.Replace("weapon_", "", StringComparison.Ordinal);
                        if (weapon.Length > 0)
                            stats[a].WeaponKills[weapon] = stats[a].WeaponKills.GetValueOrDefault(weapon) + 1;
                    }
                    if (kill.Victim is { } v) { stats[v].Deaths++; victims.Add(v); }
                    if (kill.Assister is { } s) { stats[s].Assists++; assisters.Add(s); }
                }

                if (inRound.Count > 0)
                {
                    if (inRound[0].Attacker is { } opener) stats[opener].OpeningKills++;
                    if (inRound[0].Victim is { } opened) stats[opened].OpeningDeaths++;
                }

                foreach (var (player, n) in perKiller)
                    if (n is >= 1 and <= 5) stats[player].Multi[n - 1]++;

                Trades(stats, inRound, tradeWindow, traded, sideAt);

                for (var i = 0; i < count; i++)
                {
                    if (killers.Contains(i) || assisters.Contains(i)
                        || !victims.Contains(i) || traded.Contains(i))
                        stats[i].KastRounds++;
                }

                Clutches(stats, round, inRound, count, sideAt);
            }
        }

        private static void Trades(List<Stat> stats, List<Kill> inRound, int window,
                                   HashSet<int> traded, Func<int, int, int?> sideAt)
        {
            foreach (var kill in inRound)
            {
                if (kill.Attacker is not { } avenger || kill.Victim is not { } target) continue;
                var side = sideAt(kill.Frame, avenger);

                foreach (var earlier in inRound)
                {
                    if (earlier.Frame < kill.Frame - window || earlier.Frame >= kill.Frame) continue;
                    if (earlier.Attacker != target) continue;
                    if (earlier.Victim is not { } fallen || fallen == avenger) continue;
                    if (sideAt(earlier.Frame, fallen) != side) continue;

                    stats[avenger].TradeKills++;
                    stats[fallen].TradedFor++;
                    traded.Add(fallen);
                    break;
                }
            }
        }

        private static void Clutches(List<Stat> stats, Round round, List<Kill> inRound,
                                     int count, Func<int, int, int?> sideAt)
        {
            var alive = new Dictionary<int, HashSet<int>>
            {
                [1] = [.. Enumerable.Range(0, count).Where(i => sideAt(round.Freeze, i) == 1)],
                [0] = [.. Enumerable.Range(0, count).Where(i => sideAt(round.Freeze, i) == 0)],
            };

            var decided = false;
            foreach (var kill in inRound)
            {
                if (kill.Victim is not { } victim) continue;
                var side = sideAt(kill.Frame, victim);
                if (side is 0 or 1) alive[side.Value].Remove(victim);

                for (var s = 0; s <= 1 && !decided; s++)
                {
                    var opponents = alive[1 - s];
                    if (alive[s].Count != 1 || opponents.Count < 1) continue;

                    var who = alive[s].First();
                    var size = Math.Min(5, opponents.Count);
                    var won = (s == 1 && round.Side == "CT") || (s == 0 && round.Side == "T");
                    (won ? stats[who].ClutchWon : stats[who].ClutchLost)[size - 1]++;
                    decided = true;
                }
            }
        }

        private static void Damage(List<Stat> stats, DemoEventLog log, FrameGrid grid,
                                   Func<ulong, int?> slotOf, Func<int, int, int?> sideAt)
        {
            foreach (var hurt in log.Hurts)
            {
                var attacker = slotOf(hurt.Attacker);
                var victim = slotOf(hurt.Victim);
                var frame = grid.Frame(hurt.Tick);

                if (attacker is { } a0 && victim is { } v0
                    && sideAt(frame, a0) is { } sa && sideAt(frame, v0) is { } sv && sa == sv)
                    continue;

                if (victim is { } v) stats[v].DamageTaken += hurt.DamageHealth;
                if (attacker is not { } attackerSlot || attacker == victim) continue;

                stats[attackerSlot].Damage += hurt.DamageHealth;

                var weapon = hurt.Weapon.Replace("weapon_", "", StringComparison.Ordinal);
                if (UtilityWeapons.Contains(weapon))
                {
                    stats[attackerSlot].UtilityDamage += hurt.DamageHealth;
                    continue;
                }

                stats[attackerSlot].Hits++;
                if (HitBucket(hurt.HitGroup) is { } bucket) stats[attackerSlot].HitGroups[bucket]++;
            }
        }

        private static Dictionary<int, List<int>> ShotsFired(List<Stat> stats, DemoEventLog log,
                                                             Func<ulong, int?> slotOf)
        {
            var byPlayer = new Dictionary<int, List<int>>();

            foreach (var shot in log.Shots)
            {
                if (slotOf(shot.Player) is not { } i) continue;
                var weapon = shot.Weapon.Replace("weapon_", "", StringComparison.Ordinal);

                if (weapon.Contains("flashbang", StringComparison.Ordinal))
                {
                    stats[i].FlashesThrown++;
                    continue;
                }
                if (Thrown.Contains(weapon)
                    || weapon.Contains("knife", StringComparison.Ordinal)
                    || weapon.Contains("bayonet", StringComparison.Ordinal)) continue;

                stats[i].Shots++;
                if (!byPlayer.TryGetValue(i, out var list)) byPlayer[i] = list = [];
                list.Add(shot.Tick);
            }
            return byPlayer;
        }

        private static Dictionary<int, HashSet<int>> HitTicks(DemoEventLog log, Func<ulong, int?> slotOf,
                                                              Func<int, int, int?> sideAt, FrameGrid grid)
        {
            var ticks = new Dictionary<int, HashSet<int>>();
            foreach (var hurt in log.Hurts)
            {
                if (slotOf(hurt.Attacker) is not { } a) continue;
                if (slotOf(hurt.Victim) == a) continue;

                var weapon = hurt.Weapon.Replace("weapon_", "", StringComparison.Ordinal);
                if (UtilityWeapons.Contains(weapon)) continue;

                var frame = grid.Frame(hurt.Tick);
                if (slotOf(hurt.Victim) is { } v
                    && sideAt(frame, a) is { } sa && sideAt(frame, v) is { } sv && sa == sv) continue;

                if (!ticks.TryGetValue(a, out var set)) ticks[a] = set = [];
                set.Add(hurt.Tick);
            }
            return ticks;
        }

        private static void FirstShotAccuracy(List<Stat> stats, Dictionary<int, List<int>> shotTicks,
                                              Dictionary<int, HashSet<int>> hitTicks)
        {
            const int gap = TickRate / 2;

            for (var i = 0; i < stats.Count; i++)
            {
                if (!shotTicks.TryGetValue(i, out var ticks) || ticks.Count == 0) continue;
                ticks.Sort();
                var hits = hitTicks.GetValueOrDefault(i) ?? [];

                int? previous = null;
                foreach (var tick in ticks)
                {
                    if (previous is null || tick - previous.Value > gap)
                    {
                        stats[i].FirstShots++;
                        if (hits.Contains(tick) || hits.Contains(tick + 1) || hits.Contains(tick - 1))
                            stats[i].FirstHits++;
                    }
                    previous = tick;
                }
            }
        }

        private static void Flashes(List<Stat> stats, DemoEventLog log, FrameGrid grid,
                                    List<Kill> kills, int fps, Func<ulong, int?> slotOf,
                                    Func<int, int, int?> sideAt)
        {
            var blinded = new List<(int Attacker, int Victim, int Frame, double Duration)>();

            foreach (var blind in log.Blinds)
            {
                if (slotOf(blind.Attacker) is not { } attacker) continue;
                if (slotOf(blind.Player) is not { } victim) continue;

                var frame = grid.Frame(blind.Tick);
                var side = sideAt(frame, attacker);
                if (side is null || side == sideAt(frame, victim)) continue;

                stats[attacker].EnemiesFlashed++;
                blinded.Add((attacker, victim, frame, blind.Duration));
            }

            foreach (var (attacker, victim, frame, duration) in blinded)
            {
                if (duration < 1.1) continue;
                var side = sideAt(frame, attacker);

                foreach (var kill in kills)
                {
                    if (kill.Victim != victim) continue;
                    if (kill.Frame < frame || kill.Frame > frame + fps * 2) continue;
                    if (kill.Attacker is not { } killer || killer == attacker) continue;
                    if (sideAt(kill.Frame, killer) != side) continue;

                    stats[attacker].FlashAssists++;
                    break;
                }
            }
        }
    }

    private static void WriteStats(Utf8JsonWriter w, List<PlayerStatistics.Stat> stats, int rounds)
    {
        rounds = Math.Max(1, rounds);
        w.WriteStartArray("stats");
        foreach (var s in stats)
        {
            var killsPerRound = s.Kills / (double)rounds;
            var deathsPerRound = s.Deaths / (double)rounds;
            var assistsPerRound = s.Assists / (double)rounds;
            var damagePerRound = s.Damage / (double)rounds;
            var kast = s.KastRounds / (double)rounds * 100.0;

            var impact = 2.13 * killsPerRound + 0.42 * assistsPerRound - 0.41;
            var rating = 0.0073 * kast + 0.3591 * killsPerRound - 0.5329 * deathsPerRound
                         + 0.2372 * impact + 0.0032 * damagePerRound + 0.1587;

            w.WriteStartObject();
            w.WriteString("name", s.Name);
            w.WriteString("steamid", s.SteamId);
            w.WriteNumber("k", s.Kills);
            w.WriteNumber("d", s.Deaths);
            w.WriteNumber("a", s.Assists);
            w.WriteNumber("hs", s.Headshots);
            w.WriteNumber("dmg", s.Damage);
            w.WriteNumber("dmgTaken", s.DamageTaken);
            w.WriteNumber("shots", s.Shots);
            w.WriteNumber("hits", s.Hits);
            w.WriteNumber("openK", s.OpeningKills);
            w.WriteNumber("openD", s.OpeningDeaths);
            w.WriteNumber("tradeK", s.TradeKills);
            w.WriteNumber("traded", s.TradedFor);
            w.WriteNumber("utilDmg", s.UtilityDamage);
            w.WriteNumber("flThrown", s.FlashesThrown);
            w.WriteNumber("flEnemy", s.EnemiesFlashed);
            w.WriteNumber("flAssist", s.FlashAssists);
            w.WriteNumber("mvp", s.Mvps);
            w.WriteNumber("talk", Math.Round(s.TalkSeconds, 1, MidpointRounding.ToEven));

            WriteInts(w, "multi", s.Multi);

            w.WriteStartObject("hg");
            foreach (var (group, n) in s.HitGroups) w.WriteNumber(group, n);
            w.WriteEndObject();

            w.WriteStartObject("wk");
            foreach (var (weapon, n) in s.WeaponKills) w.WriteNumber(weapon, n);
            w.WriteEndObject();

            WriteInts(w, "clutchW", s.ClutchWon);
            WriteInts(w, "clutchL", s.ClutchLost);

            w.WriteNumber("kastR", s.KastRounds);
            w.WriteNumber("firstShots", s.FirstShots);
            w.WriteNumber("firstHits", s.FirstHits);
            w.WriteNumber("firstAcc",
                s.FirstShots > 0
                    ? (int)Math.Round(s.FirstHits / (double)s.FirstShots * 100, MidpointRounding.ToEven)
                    : 0);

            if (s.Reaction is { } react) w.WriteNumber("react", react);
            else w.WriteNull("react");

            w.WriteNumber("rounds", rounds);
            w.WriteNumber("kd", s.Deaths > 0
                ? Math.Round(s.Kills / (double)s.Deaths, 2, MidpointRounding.ToEven)
                : s.Kills);
            w.WriteNumber("pm", s.Kills - s.Deaths);
            w.WriteNumber("hsPct", s.Kills > 0
                ? (int)Math.Round(s.Headshots / (double)s.Kills * 100, MidpointRounding.ToEven)
                : 0);
            w.WriteNumber("adr", Math.Round(damagePerRound, 1, MidpointRounding.ToEven));
            w.WriteNumber("kast", (int)Math.Round(kast, MidpointRounding.ToEven));
            w.WriteNumber("acc", s.Shots > 0
                ? (int)Math.Round(s.Hits / (double)s.Shots * 100, MidpointRounding.ToEven)
                : 0);
            w.WriteNumber("rating", Math.Round(rating, 2, MidpointRounding.ToEven));
            w.WriteNumber("kpr", Math.Round(killsPerRound, 2, MidpointRounding.ToEven));
            w.WriteEndObject();
        }
        w.WriteEndArray();
    }
}
