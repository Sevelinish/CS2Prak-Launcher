namespace Cs2Prak.Core.Demos;

internal sealed class DemoEventLog
{
    public string Map = "";

    public int MaxTick;

    public int MatchStart;

    public readonly List<int> FreezeEnds = [];
    public readonly List<int> RoundStarts = [];

    public readonly List<Death> Deaths = [];
    public readonly List<Shot> Shots = [];
    public readonly List<Blind> Blinds = [];
    public readonly List<Hurt> Hurts = [];
    public readonly List<ulong> Mvps = [];
    public readonly List<RoundOutcome> RoundEnds = [];
    public readonly List<BombEvent> Bombs = [];

    public readonly Dictionary<string, List<Detonation>> Detonations = new(StringComparer.Ordinal)
    {
        ["smoke"] = [], ["molotov"] = [], ["he"] = [], ["flash"] = [], ["decoy"] = [],
    };

    public readonly Dictionary<string, List<Expiry>> Expiries = new(StringComparer.Ordinal)
    {
        ["smoke"] = [], ["molotov"] = [],
    };

    public readonly Dictionary<ulong, List<VoiceTrack.Packet>> Voice = [];

    public readonly Dictionary<ulong, int> Seen = [];
    public readonly Dictionary<ulong, string> Names = [];
    public readonly Dictionary<ulong, string> Clans = [];

    public void Note(int tick)
    {
        if (tick > MaxTick) MaxTick = tick;
    }

    public readonly record struct Death(int Tick, ulong Attacker, ulong Victim, ulong Assister,
                                        string Weapon, bool Headshot);

    public readonly record struct Shot(int Tick, ulong Player, string Weapon);

    public readonly record struct Blind(int Tick, ulong Attacker, ulong Player, double Duration);

    public readonly record struct Hurt(int Tick, ulong Attacker, ulong Victim, int DamageHealth,
                                       string Weapon, int HitGroup);

    public readonly record struct RoundOutcome(int Tick, int Winner, int Reason);

    public readonly record struct BombEvent(int Tick, string Kind, int Site, ulong Player);

    public readonly record struct Detonation(int Tick, double X, double Y, int EntityId);

    public readonly record struct Expiry(int Tick, int EntityId);
}
