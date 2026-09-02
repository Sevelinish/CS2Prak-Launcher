using System.Text.Json;
using System.Text.Json.Nodes;
using DemoFile;
using DemoFile.Game.Cs;

namespace Cs2Prak.Core.Demos;

public static partial class CsDemoAnalyzer
{
    public const int TickRate = 64;
    public const int RadarSize = 1024;

    private sealed class Frame
    {
        public readonly double[]?[] Slots = new double[]?[10];
    }

    public static ParsedDemo Parse(string path) => Parse(path, fps: 8);

    public static ParsedDemo Parse(string path, int fps)
    {
        AppPaths.EnsureDir(AppPaths.DemosCacheDir);

        var key = DemoCache.Key(path);
        var cached = DemoCache.DataPath(key);
        if (File.Exists(cached)) return new ParsedDemo(key, MetaOfFile(cached));

        var log = ReadEvents(path);

        var calibration = RadarCalibration.For(log.Map)
            ?? throw new InvalidOperationException(
                $"No radar calibration for map \"{log.Map}\". Supported: {RadarCalibration.SupportedMaps}");

        var grid = FrameGrid.Lay(log, fps);
        var players = ChoosePlayers(log);
        if (players.Count == 0)
            throw new InvalidOperationException("No player position data found in this demo.");

        var world = ReadFrames(path, grid, players, calibration, log);
        var meta = WriteBlob(cached, key, log, grid, calibration, players, world, fps);
        return new ParsedDemo(key, meta);
    }

    private static DemoEventLog ReadEvents(string path)
    {
        var log = new DemoEventLog();
        var demo = new CsDemoParser();

        int Tick() => demo.CurrentDemoTick.Value;

        demo.Source1GameEvents.BeginNewMatch += _ =>
        {
            log.Note(Tick());
            log.MatchStart = Math.Max(log.MatchStart, Tick());
        };
        demo.Source1GameEvents.RoundFreezeEnd += _ => { log.Note(Tick()); log.FreezeEnds.Add(Tick()); };
        demo.Source1GameEvents.RoundStart += _ => { log.Note(Tick()); log.RoundStarts.Add(Tick()); };
        demo.Source1GameEvents.RoundOfficiallyEnded += _ => log.Note(Tick());
        demo.Source1GameEvents.RoundMvp += e =>
        {
            log.Note(Tick());
            if (e.Player is { } mvp) log.Mvps.Add(mvp.SteamID);
        };

        demo.Source1GameEvents.PlayerHurt += e =>
        {
            log.Note(Tick());
            log.Hurts.Add(new DemoEventLog.Hurt(Tick(),
                e.Attacker?.SteamID ?? 0, e.Player?.SteamID ?? 0,
                e.DmgHealth, e.Weapon ?? "", e.Hitgroup));
        };

        demo.Source1GameEvents.RoundEnd += e =>
        {
            log.Note(Tick());
            log.RoundEnds.Add(new DemoEventLog.RoundOutcome(Tick(), e.Winner, e.Reason));
        };

        demo.Source1GameEvents.PlayerDeath += e =>
        {
            log.Note(Tick());
            log.Deaths.Add(new DemoEventLog.Death(Tick(),
                e.Attacker?.SteamID ?? 0, e.Player?.SteamID ?? 0, e.Assister?.SteamID ?? 0,
                e.Weapon ?? "", e.Headshot));
        };

        demo.Source1GameEvents.WeaponFire += e =>
        {
            log.Note(Tick());
            log.Shots.Add(new DemoEventLog.Shot(Tick(), e.Player?.SteamID ?? 0, e.Weapon ?? ""));
        };

        demo.Source1GameEvents.PlayerBlind += e =>
        {
            log.Note(Tick());
            log.Blinds.Add(new DemoEventLog.Blind(Tick(),
                e.Attacker?.SteamID ?? 0, e.Player?.SteamID ?? 0, e.BlindDuration));
        };

        void Detonated(string kind, float x, float y, int entityId)
        {
            log.Note(Tick());
            log.Detonations[kind].Add(new DemoEventLog.Detonation(Tick(), x, y, entityId));
        }

        demo.Source1GameEvents.SmokegrenadeDetonate += e => Detonated("smoke", e.X, e.Y, e.Entityid);
        demo.Source1GameEvents.HegrenadeDetonate += e => Detonated("he", e.X, e.Y, e.Entityid);
        demo.Source1GameEvents.FlashbangDetonate += e => Detonated("flash", e.X, e.Y, e.Entityid);
        demo.Source1GameEvents.InfernoStartburn += e => Detonated("molotov", e.X, e.Y, e.Entityid);
        demo.Source1GameEvents.DecoyDetonate += e => Detonated("decoy", e.X, e.Y, e.Entityid);

        demo.Source1GameEvents.SmokegrenadeExpired += e =>
        {
            log.Note(Tick());
            log.Expiries["smoke"].Add(new DemoEventLog.Expiry(Tick(), e.Entityid));
        };
        demo.Source1GameEvents.InfernoExpire += e =>
        {
            log.Note(Tick());
            log.Expiries["molotov"].Add(new DemoEventLog.Expiry(Tick(), e.Entityid));
        };

        void Bomb(string kind, int site, ulong player)
        {
            log.Note(Tick());
            log.Bombs.Add(new DemoEventLog.BombEvent(Tick(), kind, site, player));
        }

        demo.PacketEvents.SvcVoiceData += m =>
        {
            var data = m.Audio?.VoiceData;
            if (data is null || data.Length == 0 || m.Xuid == 0) return;

            if (!log.Voice.TryGetValue(m.Xuid, out var list)) log.Voice[m.Xuid] = list = [];
            list.Add(new VoiceTrack.Packet(Tick(), data.ToByteArray()));
        };

        demo.Source1GameEvents.BombPlanted += e => Bomb("plant", e.Site, e.Player?.SteamID ?? 0);
        demo.Source1GameEvents.BombDefused += e => Bomb("defuse", e.Site, 0);
        demo.Source1GameEvents.BombExploded += e => Bomb("explode", e.Site, 0);

        demo.Source1GameEvents.RoundStart += _ => Census(demo, log);
        demo.Source1GameEvents.RoundFreezeEnd += _ => Census(demo, log);
        demo.Source1GameEvents.PlayerDeath += _ => Census(demo, log);

        using var stream = File.OpenRead(path);
        var reader = DemoFileReader.Create(demo, stream);
        reader.ReadAllAsync().AsTask().GetAwaiter().GetResult();

        log.Map = demo.ServerInfo?.MapName ?? "";

        log.FreezeEnds.RemoveAll(t => t < log.MatchStart);
        log.RoundStarts.RemoveAll(t => t < log.MatchStart);
        log.FreezeEnds.Sort();
        log.RoundStarts.Sort();
        return log;
    }

    private static void Census(CsDemoParser demo, DemoEventLog log)
    {
        foreach (var player in demo.Players)
        {
            var id = player.SteamID;
            if (id == 0) continue;
            log.Seen[id] = log.Seen.GetValueOrDefault(id) + 1;

            var name = player.PlayerName;
            if (!string.IsNullOrEmpty(name)) log.Names[id] = name;
        }
    }

    private sealed record FrameGrid(int StartTick, int Step, int Count, int Fps)
    {
        public int Frame(int tick) =>
            Math.Min(Count - 1, Math.Max(0,
                (int)Math.Round((tick - StartTick) / (double)Step, MidpointRounding.ToEven)));

        public IEnumerable<int> Ticks()
        {
            for (var i = 0; i < Count; i++) yield return StartTick + i * Step;
        }

        public static FrameGrid Lay(DemoEventLog log, int fps)
        {
            var endTick = log.MaxTick > 0 ? log.MaxTick : log.MatchStart + TickRate;

            var startTick = log.FreezeEnds.Count > 0
                ? BuyBegin(log, log.FreezeEnds[0]) - TickRate
                : log.MatchStart;
            startTick = Math.Max(0, startTick);

            var step = Math.Max(1, (int)Math.Round(TickRate / (double)fps, MidpointRounding.ToEven));
            var count = endTick >= startTick ? (endTick - startTick) / step + 1 : 1;
            return new FrameGrid(startTick, step, count, fps);
        }

        public static int BuyBegin(DemoEventLog log, int freezeTick)
        {
            var last = -1;
            foreach (var t in log.RoundStarts)
            {
                if (t >= freezeTick) break;
                last = t;
            }
            return last >= 0 ? last : Math.Max(log.MatchStart, freezeTick - TickRate * 20);
        }
    }

    private sealed record PlayerSlot(ulong SteamId, string Name);

    private static List<PlayerSlot> ChoosePlayers(DemoEventLog log) =>
        log.Seen
            .Where(kv => kv.Key != 0)
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key)
            .Take(10)
            .Select(kv => new PlayerSlot(kv.Key, log.Names.GetValueOrDefault(kv.Key, "?")))
            .ToList();

    private sealed class World
    {
        public readonly List<Frame> Frames = [];
        public readonly List<string> Weapons = [];
        private readonly Dictionary<string, int> _weaponIndex = new(StringComparer.Ordinal);

        public readonly Dictionary<int, Dictionary<string, int>> Clans = [];

        public readonly List<GrenadeTrack> Grenades = [];

        public readonly Dictionary<int, List<int>> Spotted = [];

        private readonly Dictionary<int, bool> _wasSpotted = [];

        public void NoteSpotted(int slot, int tick, bool spotted)
        {
            var known = _wasSpotted.TryGetValue(slot, out var before);
            if (known && before == spotted) return;
            _wasSpotted[slot] = spotted;

            if (!known || !spotted) return;

            if (!Spotted.TryGetValue(slot, out var list)) Spotted[slot] = list = [];
            list.Add(tick);
        }

        public readonly Dictionary<int, List<(int Frame, int[] Weapons)>> Inventory = [];

        private readonly Dictionary<int, int[]> _carried = [];

        public void NoteInventory(int slot, int frame, int[] weapons)
        {
            if (weapons.Length == 0) return;

            if (_carried.TryGetValue(slot, out var previous)
                && previous.AsSpan().SequenceEqual(weapons)) return;

            _carried[slot] = weapons;
            if (!Inventory.TryGetValue(slot, out var list)) Inventory[slot] = list = [];
            list.Add((frame, weapons));
        }

        public readonly Dictionary<int, Dictionary<int, int[]>> Economy = [];

        public void NoteEconomy(int round, int slot, int[] values)
        {
            if (!Economy.TryGetValue(round, out var perPlayer))
                Economy[round] = perPlayer = [];
            perPlayer[slot] = values;
        }

        public int WeaponIndex(string? name)
        {
            if (string.IsNullOrEmpty(name)) return -1;
            if (_weaponIndex.TryGetValue(name, out var index)) return index;
            index = Weapons.Count;
            Weapons.Add(name);
            _weaponIndex[name] = index;
            return index;
        }

        public void NoteClan(int slot, string? clan)
        {
            if (string.IsNullOrWhiteSpace(clan)) return;
            var trimmed = clan.Trim();
            if (!Clans.TryGetValue(slot, out var counts))
                Clans[slot] = counts = new Dictionary<string, int>(StringComparer.Ordinal);
            counts[trimmed] = counts.GetValueOrDefault(trimmed) + 1;
        }
    }

    private static World ReadFrames(string path, FrameGrid grid, List<PlayerSlot> players,
                                    RadarCalibration cal, DemoEventLog log)
    {
        var slot = new Dictionary<ulong, int>();
        for (var i = 0; i < players.Count; i++) slot[players[i].SteamId] = i;

        var world = new World();
        var wanted = grid.Ticks().ToArray();

        var buys = log.FreezeEnds
            .Select((freeze, round) => (Tick: freeze + 192, Round: round))
            .OrderBy(s => s.Tick)
            .ToList();
        var nextBuy = 0;

        var demo = new CsDemoParser();

        var live = new Dictionary<CBaseCSGrenadeProjectile, GrenadeTrack>();
        demo.EntityEvents.CBaseCSGrenadeProjectile.Create += projectile =>
        {
            var track = new GrenadeTrack { Kind = GrenadeTrack.KindOf(projectile.ServerClass.Name) };
            track.CaptureThrow(projectile.Thrower);
            live[projectile] = track;
            world.Grenades.Add(track);
        };
        demo.EntityEvents.CBaseCSGrenadeProjectile.Delete += projectile => live.Remove(projectile);

        using var stream = File.OpenRead(path);
        var reader = DemoFileReader.Create(demo, stream);
        reader.StartReadingAsync(default).AsTask().GetAwaiter().GetResult();

        while (world.Frames.Count < wanted.Length
               && reader.MoveNextAsync(default).AsTask().GetAwaiter().GetResult())
        {
            var tick = demo.CurrentDemoTick.Value;

            foreach (var (projectile, track) in live)
            {
                var at = projectile.Origin;
                if (float.IsNaN(at.X)) continue;
                track.Rows.Add(new GrenadeTrack.Sample(tick, at.X, at.Y, at.Z, projectile.Bounces));
            }

            foreach (var player in demo.Players)
            {
                if (!slot.TryGetValue(player.SteamID, out var i)) continue;
                if (player.PlayerPawn is not { } pawn) continue;
                world.NoteSpotted(i, tick, pawn.EntitySpottedState.Spotted);
            }

            while (nextBuy < buys.Count && tick >= buys[nextBuy].Tick)
            {
                CaptureEconomy(demo, slot, world, buys[nextBuy].Round);
                nextBuy++;
            }

            if (tick < wanted[world.Frames.Count]) continue;

            var frame = Sample(demo, slot, cal, world, world.Frames.Count);
            do
            {
                world.Frames.Add(frame);
            }
            while (world.Frames.Count < wanted.Length && tick >= wanted[world.Frames.Count]);
        }

        while (world.Frames.Count < wanted.Length) world.Frames.Add(new Frame());

        return world;
    }

    private static Frame Sample(CsDemoParser demo, Dictionary<ulong, int> slot,
                                RadarCalibration cal, World world, int frameIndex)
    {
        var takeInventory = frameIndex % 2 == 0;

        var frame = new Frame();

        foreach (var player in demo.Players)
        {
            if (!slot.TryGetValue(player.SteamID, out var i)) continue;

            var pawn = player.PlayerPawn;
            if (pawn is null) continue;

            var team = (int)player.CSTeamNum;
            if (team is not (2 or 3)) continue;

            var origin = pawn.Origin;
            if (float.IsNaN(origin.X) || float.IsNaN(origin.Y)) continue;

            var (px, py) = cal.ToPixels(origin.X, origin.Y);
            var angles = pawn.EyeAngles;

            frame.Slots[i] =
            [
                px,
                py,
                Math.Round(angles.Yaw, MidpointRounding.ToEven),
                pawn.Health,
                team == 3 ? 1 : 0,
                pawn.IsAlive ? 1 : 0,
                cal.HasLower && origin.Z < cal.LowerLevelMax ? 1 : 0,
                world.WeaponIndex(WeaponOf(pawn)),
                Math.Round((double)origin.Z, MidpointRounding.ToEven),
                Math.Round(angles.Pitch, MidpointRounding.ToEven),
            ];

            world.NoteClan(i, player.Clan.Value);

            if (takeInventory) world.NoteInventory(i, frameIndex, CarriedBy(pawn, world));
        }

        return frame;
    }

    private static int[] CarriedBy(CCSPlayerPawn pawn, World world)
    {
        var carried = new List<int>();
        foreach (var weapon in pawn.Weapons)
        {
            if (weapon?.EconItem is not { } item) continue;
            var name = WeaponNames.For(item.ItemDefinitionIndex);
            if (name is null) continue;
            carried.Add(world.WeaponIndex(name));
        }
        carried.Sort();
        return carried.ToArray();
    }

    private static void CaptureEconomy(CsDemoParser demo, Dictionary<ulong, int> slot,
                                       World world, int round)
    {
        foreach (var player in demo.Players)
        {
            if (!slot.TryGetValue(player.SteamID, out var i)) continue;

            var pawn = player.PlayerPawn;
            if (pawn is null) continue;

            world.NoteEconomy(round, i,
            [
                player.InGameMoneyServices?.Account ?? 0,
                pawn.ArmorValue,
                player.PawnHasHelmet ? 1 : 0,
                player.PawnHasDefuser ? 1 : 0,
                pawn.CurrentEquipmentValue,
            ]);
        }
    }

    private static string? WeaponOf(CCSPlayerPawn pawn)
    {
        var item = pawn.ActiveWeapon?.EconItem;
        return item is null ? null : WeaponNames.For(item.ItemDefinitionIndex);
    }

    private static List<Flight> BuildFlights(World world, DemoEventLog log, FrameGrid grid,
                                             RadarCalibration cal)
    {
        var detonations = new Dictionary<string, List<(int Tick, double X, double Y)>>(StringComparer.Ordinal);
        foreach (var (kind, list) in log.Detonations)
        {
            detonations[kind] = list
                .Select(d => { var (x, y) = cal.ToPixels(d.X, d.Y); return (d.Tick, X: x, Y: y); })
                .OrderBy(d => d.Tick)
                .ToList();
        }

        var flights = new List<Flight>();
        foreach (var track in world.Grenades)
        {
            if (track.Kind == "decoy" || track.Rows.Count == 0) continue;

            var first = track.Rows[0].Tick;
            var last = track.Rows[^1].Tick;
            if (first < grid.StartTick) continue;

            var landed = last;
            if (detonations.TryGetValue(track.Kind, out var candidates))
            {
                foreach (var (tick, dx, dy) in candidates)
                {
                    if (tick < first || tick > last + 16) continue;

                    var nearest = track.Rows.MinBy(r => Math.Abs(r.Tick - tick));
                    var (px, py) = cal.ToPixels(nearest.X, nearest.Y);
                    if ((px - dx) * (px - dx) + (py - dy) * (py - dy) >= 900) continue;

                    landed = tick;
                    break;
                }
            }

            var kept = track.Rows.Where(r => r.Tick <= landed).ToList();
            if (kept.Count == 0) continue;

            var ground = kept.Min(r => r.Z);

            var points = new List<double[]>();
            for (var i = 0; i < kept.Count; i++)
            {
                if (i % 2 == 1 && i != kept.Count - 1) continue;
                var row = kept[i];
                var (px, py) = cal.ToPixels(row.X, row.Y);
                points.Add([Math.Round((row.Tick - grid.StartTick) / (double)grid.Step, 2),
                            px, py, Math.Round((double)(row.Z - ground), MidpointRounding.ToEven)]);
            }
            if (points.Count < 2) continue;

            var bounces = new List<double[]>();
            int? lastBounceTick = null;
            for (var i = 1; i < kept.Count; i++)
            {
                if (kept[i].Bounces <= kept[i - 1].Bounces) continue;

                if (lastBounceTick is { } previous && kept[i].Tick - previous < 6) continue;
                lastBounceTick = kept[i].Tick;

                var (px, py) = cal.ToPixels(kept[i].X, kept[i].Y);
                bounces.Add([Math.Round((kept[i].Tick - grid.StartTick) / (double)grid.Step, 2), px, py]);
            }

            flights.Add(new Flight(
                track.Kind, points, bounces, track.ThrowerName, track.ThrowerSteamId.ToString(),
                track.ThrowPosition, track.ThrowAngles,
                track.ThrowPosition is null ? null : track.ThrowSide));
        }
        return flights;
    }

    private static void WriteFlights(Utf8JsonWriter w, List<Flight> flights)
    {
        w.WriteStartArray("flights");
        foreach (var flight in flights)
        {
            w.WriteStartObject();
            w.WriteString("t", flight.Kind);

            w.WriteStartArray("p");
            foreach (var point in flight.Points)
            {
                w.WriteStartArray();
                foreach (var v in point) w.WriteNumberValue(v);
                w.WriteEndArray();
            }
            w.WriteEndArray();

            w.WriteString("by", flight.By);
            w.WriteString("sid", flight.SteamId);

            if (flight.Bounces.Count > 0)
            {
                w.WriteStartArray("b");
                foreach (var bounce in flight.Bounces)
                {
                    w.WriteStartArray();
                    foreach (var v in bounce) w.WriteNumberValue(v);
                    w.WriteEndArray();
                }
                w.WriteEndArray();
            }

            if (flight.ThrowPosition is { } position)
            {
                w.WriteStartArray("sp");
                foreach (var v in position) w.WriteNumberValue(v);
                w.WriteEndArray();

                w.WriteStartArray("sa");
                foreach (var v in flight.ThrowAngles!) w.WriteNumberValue(v);
                w.WriteEndArray();

                w.WriteNumber("tm", flight.ThrowSide!.Value);
            }

            w.WriteEndObject();
        }
        w.WriteEndArray();
    }

    private sealed class Round
    {
        public int Number, Start, End, Freeze, ScoreA, ScoreB;
        public string? Win, Side, Reason;
    }

    private static List<Round> BuildRounds(DemoEventLog log, FrameGrid grid, List<int> teamA,
                                           World world)
    {
        var rounds = new List<Round>();
        for (var n = 0; n < log.FreezeEnds.Count; n++)
        {
            var freeze = log.FreezeEnds[n];
            var buy = FrameGrid.BuyBegin(log, freeze);
            var next = n + 1 < log.FreezeEnds.Count ? log.FreezeEnds[n + 1] : (int?)null;
            var end = next is null ? log.MaxTick : FrameGrid.BuyBegin(log, next.Value) - 1;

            rounds.Add(new Round
            {
                Number = n + 1,
                Start = grid.Frame(buy),
                End = grid.Frame(end),
                Freeze = grid.Frame(freeze),
            });
        }

        if (rounds.Count == 0)
            throw new InvalidOperationException("No rounds found — is this a warm-up-only demo?");

        var ends = log.RoundEnds
            .Select(r => (Frame: grid.Frame(r.Tick), r.Winner, r.Reason))
            .OrderBy(r => r.Frame)
            .ToList();

        int scoreA = 0, scoreB = 0;
        foreach (var round in rounds)
        {
            var outcome = ends.FirstOrDefault(e => e.Frame >= round.Start && e.Frame <= round.End,
                                              (Frame: -1, Winner: 0, Reason: 0));
            if (outcome.Frame >= 0)
            {
                var winningSide = outcome.Winner == RoundEndReasons.TeamCounterTerrorist ? 1 : 0;
                var teamASide = teamA
                    .Select(i => SideAt(world, round.Freeze, i))
                    .FirstOrDefault(s => s is not null) ?? 1;

                round.Win = teamASide == winningSide ? "A" : "B";
                round.Side = RoundEndReasons.Side(outcome.Winner);
                round.Reason = RoundEndReasons.Name(outcome.Reason);

                if (round.Win == "A") scoreA++; else scoreB++;
            }
            round.ScoreA = scoreA;
            round.ScoreB = scoreB;
        }
        return rounds;
    }

    private static int? SideAt(World world, int frame, int slot)
    {
        if (frame < 0 || frame >= world.Frames.Count) return null;
        var entry = world.Frames[frame].Slots[slot];
        return entry is null ? null : (int)entry[4];
    }

    private static (List<int> A, List<int> B) SplitTeams(World world, int firstFreeze)
    {
        var a = Enumerable.Range(0, 10).Where(i => SideAt(world, firstFreeze, i) == 1).ToList();
        if (a.Count != 5) a = [0, 1, 2, 3, 4];
        var b = Enumerable.Range(0, 10).Where(i => !a.Contains(i)).ToList();
        return (a, b);
    }

    private static string? ClanOf(IEnumerable<int> team, World world)
    {
        foreach (var slot in team)
        {
            if (!world.Clans.TryGetValue(slot, out var counts) || counts.Count == 0) continue;
            var name = counts.MaxBy(kv => kv.Value).Key;
            if (name.StartsWith("TEAM_", StringComparison.OrdinalIgnoreCase)) name = name[5..];
            if (name.Length > 24) name = name[..24];
            if (name.Length > 0) return name;
        }
        return null;
    }

    private static JsonObject WriteBlob(string cachedPath, string key, DemoEventLog log,
                                        FrameGrid grid, RadarCalibration cal,
                                        List<PlayerSlot> players, World world, int fps)
    {
        var slot = new Dictionary<ulong, int>();
        for (var i = 0; i < players.Count; i++) slot[players[i].SteamId] = i;

        int? SlotOf(ulong id) => id != 0 && slot.TryGetValue(id, out var i) ? i : null;

        var (teamA, teamB) = SplitTeams(world, grid.Frame(log.FreezeEnds.Count > 0 ? log.FreezeEnds[0] : 0));
        var rounds = BuildRounds(log, grid, teamA, world);

        var temp = cachedPath + ".tmp";
        using (var stream = File.Create(temp))
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();

            w.WriteString("key", key);
            w.WriteString("map", log.Map);
            w.WriteNumber("fps", fps);
            w.WriteNumber("tickrate", TickRate);
            w.WriteNumber("radarSize", RadarSize);
            w.WriteBoolean("hasLower", cal.HasLower);
            w.WriteNumber("scale", cal.Scale);
            w.WriteNumber("posX", cal.PosX);
            w.WriteNumber("posY", cal.PosY);
            w.WriteNumber("startTick", grid.StartTick);
            w.WriteNumber("step", grid.Step);
            w.WriteNumber("nFrames", grid.Count);

            w.WriteStartArray("players");
            foreach (var p in players)
            {
                w.WriteStartObject();
                w.WriteString("name", p.Name);
                w.WriteString("steamid", p.SteamId.ToString());
                w.WriteEndObject();
            }
            w.WriteEndArray();

            WriteRounds(w, rounds);
            WriteFrames(w, world);

            WriteInts(w, "teamA", teamA);
            WriteInts(w, "teamB", teamB);
            WriteNullableString(w, "teamAName", ClanOf(teamA, world));
            WriteNullableString(w, "teamBName", ClanOf(teamB, world));

            w.WriteStartArray("weapons");
            foreach (var name in world.Weapons) w.WriteStringValue(name);
            w.WriteEndArray();

            WriteFlights(w, BuildFlights(world, log, grid, cal));
            WriteEconomy(w, world);
            WriteInventory(w, world);
            var voice = BuildVoice(log, grid, players, world, key);
            WriteVoice(w, voice);

            var kills = BuildKills(log, grid, SlotOf);
            WriteStats(w, PlayerStatistics.Build(players, kills, rounds, world, log, grid, voice, fps),
                      rounds.Count);
            WriteKills(w, kills);
            WriteGrenades(w, "smokes", Timed(log, grid, "smoke", 18, 20, fps));
            WriteGrenades(w, "molotovs", Timed(log, grid, "molotov", 7, 9, fps));
            WriteGrenades(w, "hes", Points(log, grid, "he", cal));
            WriteGrenades(w, "flashes", Points(log, grid, "flash", cal));
            WriteGrenades(w, "decoys", Points(log, grid, "decoy", cal));
            WriteBombs(w, log, grid, SlotOf);
            WriteShots(w, log, grid, SlotOf);
            WriteBlinds(w, log, grid, SlotOf, fps);

            w.WriteEndObject();
        }
        File.Move(temp, cachedPath, overwrite: true);

        var last = rounds[^1];
        return new JsonObject
        {
            ["map"] = log.Map,
            ["fps"] = fps,
            ["nFrames"] = grid.Count,
            ["rounds"] = rounds.Count,
            ["players"] = new JsonArray(players.Select(p => (JsonNode)new JsonObject
            {
                ["name"] = p.Name,
                ["steamid"] = p.SteamId.ToString(),
            }).ToArray()),
            ["hasLower"] = cal.HasLower,
            ["sa"] = last.ScoreA,
            ["sb"] = last.ScoreB,
            ["winner"] = last.ScoreA > last.ScoreB ? "A" : last.ScoreB > last.ScoreA ? "B" : "",
        };
    }

    private static void WriteRounds(Utf8JsonWriter w, List<Round> rounds)
    {
        w.WriteStartArray("rounds");
        foreach (var r in rounds)
        {
            w.WriteStartObject();
            w.WriteNumber("n", r.Number);
            w.WriteNumber("start", r.Start);
            w.WriteNumber("end", r.End);
            w.WriteNumber("freeze", r.Freeze);
            if (r.Win is not null)
            {
                w.WriteString("win", r.Win);
                w.WriteString("wside", r.Side);
                w.WriteString("reason", r.Reason);
            }
            w.WriteNumber("sa", r.ScoreA);
            w.WriteNumber("sb", r.ScoreB);
            w.WriteEndObject();
        }
        w.WriteEndArray();
    }

    private static void WriteFrames(Utf8JsonWriter w, World world)
    {
        w.WriteStartArray("frames");
        foreach (var frame in world.Frames)
        {
            w.WriteStartArray();
            foreach (var entry in frame.Slots)
            {
                if (entry is null) { w.WriteNullValue(); continue; }
                w.WriteStartArray();
                foreach (var v in entry) w.WriteNumberValue(v);
                w.WriteEndArray();
            }
            w.WriteEndArray();
        }
        w.WriteEndArray();
    }

    private static List<VoiceTrack.Clip> BuildVoice(DemoEventLog log, FrameGrid grid,
                                                    List<PlayerSlot> players, World world,
                                                    string key)
    {
        try
        {
            var bySlot = new Dictionary<int, List<VoiceTrack.Packet>>();
            for (var i = 0; i < players.Count; i++)
            {
                if (log.Voice.TryGetValue(players[i].SteamId, out var packets) && packets.Count > 0)
                    bySlot[i] = packets;
            }
            if (bySlot.Count == 0) return [];

            return VoiceTrack.Build(bySlot, DemoCache.VoiceDir(key), grid.StartTick, grid.Step,
                grid.Count,
                (frame, slot) => SideAt(world, (int)Math.Round(frame, MidpointRounding.ToEven), slot) ?? 0);
        }
        catch (Exception e)
        {
            LastVoiceError = $"{e.GetType().Name}: {e.Message}";
            return [];
        }
    }

    public static string LastVoiceError { get; private set; } = "";

    public static (long Decoded, long Inserted) VoiceMix => (VoiceTrack.Decoded, VoiceTrack.Inserted);

    private static void WriteVoice(Utf8JsonWriter w, List<VoiceTrack.Clip> clips)
    {
        w.WriteStartArray("voice");
        foreach (var clip in clips)
        {
            w.WriteStartObject();
            w.WriteNumber("idx", clip.Slot);
            w.WriteNumber("n", clip.Number);
            w.WriteNumber("f", clip.Frame);
            w.WriteNumber("dur", clip.Duration);
            w.WriteNumber("side", clip.Side);
            w.WriteEndObject();
        }
        w.WriteEndArray();
    }

    private static void WriteEconomy(Utf8JsonWriter w, World world)
    {
        w.WriteStartObject("econ");
        foreach (var (round, perPlayer) in world.Economy.OrderBy(kv => kv.Key))
        {
            w.WriteStartObject(round.ToString());
            foreach (var (player, values) in perPlayer.OrderBy(kv => kv.Key))
            {
                w.WriteStartArray(player.ToString());
                foreach (var v in values) w.WriteNumberValue(v);
                w.WriteEndArray();
            }
            w.WriteEndObject();
        }
        w.WriteEndObject();
    }

    private static void WriteInventory(Utf8JsonWriter w, World world)
    {
        w.WriteStartObject("inv");
        foreach (var (player, checkpoints) in world.Inventory.OrderBy(kv => kv.Key))
        {
            w.WriteStartArray(player.ToString());
            foreach (var (frame, weapons) in checkpoints)
            {
                w.WriteStartArray();
                w.WriteNumberValue(frame);
                w.WriteStartArray();
                foreach (var index in weapons) w.WriteNumberValue(index);
                w.WriteEndArray();
                w.WriteEndArray();
            }
            w.WriteEndArray();
        }
        w.WriteEndObject();
    }

    private static void WriteInts(Utf8JsonWriter w, string name, IEnumerable<int> values)
    {
        w.WriteStartArray(name);
        foreach (var v in values) w.WriteNumberValue(v);
        w.WriteEndArray();
    }

    private static void WriteNullableString(Utf8JsonWriter w, string name, string? value)
    {
        if (value is null) w.WriteNull(name);
        else w.WriteString(name, value);
    }

    internal sealed record Kill(int Frame, int? Attacker, int? Victim, int? Assister,
                                string Weapon, bool Headshot);

    private static List<Kill> BuildKills(DemoEventLog log, FrameGrid grid, Func<ulong, int?> slotOf) =>
        log.Deaths
            .Where(d => d.Tick >= grid.StartTick)
            .Select(d => new Kill(grid.Frame(d.Tick), slotOf(d.Attacker), slotOf(d.Victim),
                                  slotOf(d.Assister), d.Weapon, d.Headshot))
            .ToList();

    private static void WriteKills(Utf8JsonWriter w, List<Kill> kills)
    {
        w.WriteStartArray("kills");
        foreach (var kill in kills)
        {
            w.WriteStartObject();
            w.WriteNumber("f", kill.Frame);
            WriteNullableInt(w, "a", kill.Attacker);
            WriteNullableInt(w, "v", kill.Victim);
            WriteNullableInt(w, "as", kill.Assister);
            w.WriteString("w", kill.Weapon);
            w.WriteBoolean("hs", kill.Headshot);
            w.WriteEndObject();
        }
        w.WriteEndArray();
    }

    private static void WriteNullableInt(Utf8JsonWriter w, string name, int? value)
    {
        if (value is null) w.WriteNull(name);
        else w.WriteNumber(name, value.Value);
    }

    private static readonly HashSet<string> NotShots = new(StringComparer.Ordinal)
    {
        "hegrenade", "flashbang", "smokegrenade", "molotov", "incgrenade", "decoy",
    };

    private static void WriteShots(Utf8JsonWriter w, DemoEventLog log, FrameGrid grid,
                                   Func<ulong, int?> slotOf)
    {
        var seen = new HashSet<(int, int)>();
        w.WriteStartArray("shots");
        foreach (var shot in log.Shots)
        {
            if (shot.Tick < grid.StartTick) continue;
            if (slotOf(shot.Player) is not { } i) continue;

            var weapon = shot.Weapon.Replace("weapon_", "", StringComparison.Ordinal);
            if (NotShots.Contains(weapon) || weapon.Contains("knife", StringComparison.Ordinal)
                || weapon.Contains("bayonet", StringComparison.Ordinal)) continue;

            var frame = grid.Frame(shot.Tick);
            if (!seen.Add((frame, i))) continue;

            w.WriteStartArray();
            w.WriteNumberValue(frame);
            w.WriteNumberValue(i);
            w.WriteEndArray();
        }
        w.WriteEndArray();
    }

    private static void WriteBlinds(Utf8JsonWriter w, DemoEventLog log, FrameGrid grid,
                                    Func<ulong, int?> slotOf, int fps)
    {
        w.WriteStartArray("blinds");
        foreach (var blind in log.Blinds)
        {
            if (blind.Tick < grid.StartTick || blind.Duration < 0.4) continue;
            if (slotOf(blind.Player) is not { } i) continue;

            var frame = grid.Frame(blind.Tick);
            w.WriteStartObject();
            w.WriteNumber("i", i);
            w.WriteNumber("f", frame);
            w.WriteNumber("end", Math.Round(frame + blind.Duration * fps, 1));
            w.WriteEndObject();
        }
        w.WriteEndArray();
    }

    private static void WriteBombs(Utf8JsonWriter w, DemoEventLog log, FrameGrid grid,
                                   Func<ulong, int?> slotOf)
    {
        w.WriteStartArray("bomb");
        foreach (var bomb in log.Bombs)
        {
            if (bomb.Tick < grid.StartTick) continue;
            w.WriteStartObject();
            w.WriteString("k", bomb.Kind);
            w.WriteNumber("f", grid.Frame(bomb.Tick));
            w.WriteNumber("site", bomb.Site);
            if (bomb.Kind == "plant") WriteNullableInt(w, "by", slotOf(bomb.Player));
            w.WriteEndObject();
        }
        w.WriteEndArray();
    }

    private sealed record Puff(int Frame, double X, double Y, int EntityId, int? End = null);

    private static List<Puff> Points(DemoEventLog log, FrameGrid grid, string kind,
                                     RadarCalibration cal)
    {
        var points = new List<Puff>();
        foreach (var d in log.Detonations[kind])
        {
            if (d.Tick < grid.StartTick) continue;
            var (x, y) = cal.ToPixels(d.X, d.Y);
            points.Add(new Puff(grid.Frame(d.Tick), x, y, d.EntityId));
        }
        return points;
    }

    private static List<Puff> Timed(DemoEventLog log, FrameGrid grid, string kind,
                                    int defaultSeconds, int maxSeconds, int fps)
    {
        var cal = RadarCalibration.For(log.Map)!;
        var starts = Points(log, grid, kind, cal);

        var endsByEntity = new Dictionary<int, List<int>>();
        foreach (var e in log.Expiries[kind])
        {
            if (!endsByEntity.TryGetValue(e.EntityId, out var list))
                endsByEntity[e.EntityId] = list = [];
            list.Add(grid.Frame(e.Tick));
        }
        foreach (var list in endsByEntity.Values) list.Sort();

        var timed = new List<Puff>(starts.Count);
        foreach (var puff in starts)
        {
            var cap = puff.Frame + fps * maxSeconds;
            int? next = endsByEntity.TryGetValue(puff.EntityId, out var list)
                ? list.FirstOrDefault(f => f >= puff.Frame, -1) is var f && f >= 0 ? f : null
                : null;

            var end = next is not null
                ? Math.Min(next.Value, cap)
                : Math.Min(puff.Frame + fps * defaultSeconds, cap);
            timed.Add(puff with { End = end });
        }
        return timed;
    }

    private static void WriteGrenades(Utf8JsonWriter w, string name, List<Puff> puffs)
    {
        w.WriteStartArray(name);
        foreach (var p in puffs)
        {
            w.WriteStartObject();
            w.WriteNumber("f", p.Frame);
            w.WriteNumber("x", p.X);
            w.WriteNumber("y", p.Y);
            w.WriteNumber("eid", p.EntityId);
            if (p.End is not null) w.WriteNumber("end", p.End.Value);
            w.WriteEndObject();
        }
        w.WriteEndArray();
    }

    private static JsonObject MetaOfFile(string cachedPath)
    {
        using var stream = File.OpenRead(cachedPath);
        var blob = JsonNode.Parse(stream) as JsonObject ?? [];
        var rounds = blob["rounds"] as JsonArray ?? [];
        var last = rounds.Count > 0 ? rounds[^1] as JsonObject : null;
        var sa = (int)(last?["sa"]?.GetValue<long>() ?? 0);
        var sb = (int)(last?["sb"]?.GetValue<long>() ?? 0);

        return new JsonObject
        {
            ["map"] = blob["map"]?.DeepClone(),
            ["fps"] = blob["fps"]?.DeepClone(),
            ["nFrames"] = blob["nFrames"]?.DeepClone(),
            ["rounds"] = rounds.Count,
            ["players"] = (blob["players"] as JsonArray)?.DeepClone(),
            ["hasLower"] = blob["hasLower"]?.DeepClone(),
            ["sa"] = sa,
            ["sb"] = sb,
            ["winner"] = sa > sb ? "A" : sb > sa ? "B" : "",
        };
    }
}
