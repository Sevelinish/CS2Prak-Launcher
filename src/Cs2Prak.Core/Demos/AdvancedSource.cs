using DemoFile;
using DemoFile.Game.Cs;

namespace Cs2Prak.Core.Demos;

internal sealed class AdvancedSource
{
    public const int Window = (int)(CsDemoAnalyzer.TickRate * 2.5);

    private static readonly HashSet<string> UtilityWeapons = new(StringComparer.Ordinal)
        { "hegrenade", "inferno", "molotov", "incgrenade" };

    private static readonly Lock Gate = new();
    private static string? _cachedKey;
    private static AdvancedSource? _cached;

    public string Map = "";
    public int MatchStart;

    public readonly Dictionary<ulong, string> Names = [];
    public readonly List<int> FreezeEnds = [];
    public readonly List<Nade> Nades = [];
    public readonly List<Death> Deaths = [];

    public readonly Dictionary<ulong, List<int>> Shots = [];

    public readonly Dictionary<(ulong Attacker, ulong Victim), HashSet<int>> Hits = [];

    public readonly Dictionary<(ulong Player, int Tick), State> States = [];

    public readonly record struct Nade(int Tick, double X, double Y, string Kind);

    public readonly record struct Death(int Tick, ulong Attacker, ulong Victim, string Weapon,
                                        bool Headshot, double? Distance);

    public readonly record struct State(double X, double Y, double Z, double Yaw, double Pitch,
                                        bool Spotted, double FlashDuration, int Health, int? Team);

    public static AdvancedSource For(string path)
    {
        var key = DemoCache.Key(path);
        lock (Gate)
        {
            if (_cachedKey == key && _cached is not null) return _cached;

            var source = Read(path);
            _cachedKey = key;
            _cached = source;
            return source;
        }
    }

    private static AdvancedSource Read(string path)
    {
        var source = new AdvancedSource();
        source.ReadEvents(path);
        source.ReadStates(path);
        return source;
    }

    private void ReadEvents(string path)
    {
        var demo = new CsDemoParser();
        int Tick() => demo.CurrentDemoTick.Value;

        demo.Source1GameEvents.BeginNewMatch += _ => MatchStart = Math.Max(MatchStart, Tick());
        demo.Source1GameEvents.RoundFreezeEnd += _ => FreezeEnds.Add(Tick());

        demo.Source1GameEvents.PlayerDeath += e => Deaths.Add(new Death(
            Tick(), e.Attacker?.SteamID ?? 0, e.Player?.SteamID ?? 0,
            (e.Weapon ?? "").Replace("weapon_", "", StringComparison.Ordinal),
            e.Headshot,
            float.IsFinite(e.Distance) ? Math.Round(e.Distance, 1) : null));

        demo.Source1GameEvents.WeaponFire += e =>
        {
            if (e.Player is not { } player) return;
            var weapon = e.Weapon ?? "";
            if (weapon.Contains("grenade", StringComparison.Ordinal)
                || weapon.Contains("flash", StringComparison.Ordinal)
                || weapon.Contains("molotov", StringComparison.Ordinal)
                || weapon.Contains("decoy", StringComparison.Ordinal)
                || weapon.Contains("knife", StringComparison.Ordinal)
                || weapon.Contains("bayonet", StringComparison.Ordinal)) return;

            if (!Shots.TryGetValue(player.SteamID, out var list)) Shots[player.SteamID] = list = [];
            list.Add(Tick());
        };

        demo.Source1GameEvents.PlayerHurt += e =>
        {
            if (e.Attacker is not { } attacker || e.Player is not { } victim) return;

            var weapon = (e.Weapon ?? "").Replace("weapon_", "", StringComparison.Ordinal);
            if (UtilityWeapons.Contains(weapon)) return;

            var pair = (attacker.SteamID, victim.SteamID);
            if (!Hits.TryGetValue(pair, out var ticks)) Hits[pair] = ticks = [];
            ticks.Add(Tick());
        };

        void Detonated(string kind, float x, float y) => Nades.Add(new Nade(Tick(), x, y, kind));

        demo.Source1GameEvents.SmokegrenadeDetonate += e => Detonated("smoke", e.X, e.Y);
        demo.Source1GameEvents.HegrenadeDetonate += e => Detonated("he", e.X, e.Y);
        demo.Source1GameEvents.FlashbangDetonate += e => Detonated("flash", e.X, e.Y);
        demo.Source1GameEvents.InfernoStartburn += e => Detonated("molotov", e.X, e.Y);
        demo.Source1GameEvents.DecoyDetonate += e => Detonated("decoy", e.X, e.Y);

        using var stream = File.OpenRead(path);
        var reader = DemoFileReader.Create(demo, stream);
        reader.StartReadingAsync(default).AsTask().GetAwaiter().GetResult();

        while (reader.MoveNextAsync(default).AsTask().GetAwaiter().GetResult())
        {
            if (demo.CurrentDemoTick.Value % CsDemoAnalyzer.TickRate != 0) continue;

            foreach (var player in demo.Players)
                if (player.SteamID != 0 && player.PlayerName is { Length: > 0 } name)
                    Names[player.SteamID] = name;
        }

        Map = demo.ServerInfo?.MapName ?? "";
        FreezeEnds.RemoveAll(t => t < MatchStart);
        FreezeEnds.Sort();
        foreach (var list in Shots.Values) list.Sort();
    }

    private void ReadStates(string path)
    {
        var wanted = new HashSet<int>();
        foreach (var death in Deaths)
        {
            if (death.Tick < MatchStart) continue;
            for (var t = Math.Max(0, death.Tick - Window - 2); t < death.Tick + 8; t++)
                wanted.Add(t);
        }
        if (wanted.Count == 0) return;

        var demo = new CsDemoParser();
        using var stream = File.OpenRead(path);
        var reader = DemoFileReader.Create(demo, stream);
        reader.StartReadingAsync(default).AsTask().GetAwaiter().GetResult();

        var lastWanted = wanted.Max();

        while (reader.MoveNextAsync(default).AsTask().GetAwaiter().GetResult())
        {
            var tick = demo.CurrentDemoTick.Value;
            if (tick > lastWanted) break;
            if (!wanted.Contains(tick)) continue;

            foreach (var player in demo.Players)
            {
                if (player.SteamID == 0 || player.PlayerPawn is not { } pawn) continue;

                var origin = pawn.Origin;
                var angles = pawn.EyeAngles;
                States[(player.SteamID, tick)] = new State(
                    origin.X, origin.Y, origin.Z, angles.Yaw, angles.Pitch,
                    pawn.EntitySpottedState.Spotted, pawn.FlashDuration, pawn.Health,
                    player.CSTeamNum is CSTeamNumber.Terrorist or CSTeamNumber.CounterTerrorist
                        ? (int)player.CSTeamNum
                        : null);
            }
        }
    }
}
