using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Wavebreak.Core;

namespace Wavebreak.Game.Puzzle;

/// <summary>
/// Builds a puzzle map from the run's seed: a winding chain of rooms (start,
/// three to five puzzle rooms, the reward vault), each with a piece type and a
/// layout drawn for this run. Every machine in a co-op session calls it with
/// the same inputs (seed, puzzle number, difficulty, player count as the host
/// saw it) and gets the same map, so only the puzzle's state ever travels.
///
/// <b>Never unsolvable.</b> A room is kept only when <see cref="PuzzleSolver"/>
/// solves it from its starting state, the way between its doors stays open,
/// and its timing (wind, latches) leaves a margin over a plain walk. A draw
/// that fails is drawn again; after enough failures a simple fixed layout is
/// used, which the same checks pass.
///
/// <b>Difficulty</b> is the puzzle's number in the run plus a step for Hard
/// and up (<see cref="DifficultyFor"/>). It adds rooms, crates, pushes,
/// mirrors and decoys, bells in the order, levers, shorter calms and latches.
/// </summary>
public static class PuzzleGen
{
    private const int MaxDifficulty = 5;

    /// <summary>--puzzle-rooms=twin,crates: force the puzzle rooms (tests and captures).</summary>
    public static string? ForcedRooms
    {
        get
        {
            foreach (string a in OS.GetCmdlineUserArgs())
                if (a.StartsWith("--puzzle-rooms=")) return a["--puzzle-rooms=".Length..];
            return null;
        }
    }

    /// <summary>The puzzle's difficulty: its place in the run, plus one on
    /// Hard and Nightmare and two from Tempest up.</summary>
    public static int DifficultyFor(int puzzleIndex, int tierIndex)
        => Math.Clamp(puzzleIndex + (tierIndex >= 1 ? 1 : 0) + (tierIndex >= 3 ? 1 : 0), 0, MaxDifficulty);

    /// <summary>Puzzle rooms in a map of this difficulty: three, then four, then five.</summary>
    public static int RoomsFor(int difficulty) => difficulty <= 1 ? 3 : difficulty <= 3 ? 4 : 5;

    private static ulong SeedFor(ulong runSeed, int puzzleIndex) => runSeed ^ 0x9A22_1E00UL ^ ((ulong)(puzzleIndex + 1) * 0x9E3779B97F4A7C15UL);

    /// <summary>The name puzzle <paramref name="puzzleIndex"/> of this run will
    /// have (the rest room before it says where you go next): a seeded start in
    /// the list, then the next name per puzzle, so a run never repeats one.</summary>
    public static string NameFor(ulong runSeed, int puzzleIndex)
    {
        int n = PuzzleText.MapNames.Count;
        return PuzzleText.MapNames[(int)(((runSeed >> 3) % (ulong)n + (ulong)Math.Max(0, puzzleIndex)) % (ulong)n)];
    }

    public static PuzzlePlan Build(ulong runSeed, int puzzleIndex, int difficulty, int players, string? forced = null)
    {
        ulong seed = SeedFor(runSeed, puzzleIndex);
        var rng = new Rng(seed);
        var plan = new PuzzlePlan
        {
            Seed = seed,
            PuzzleIndex = puzzleIndex,
            Difficulty = Math.Clamp(difficulty, 0, MaxDifficulty),
            Players = Math.Max(1, players),
        };
        int d = plan.Difficulty;
        plan.Name = NameFor(runSeed, puzzleIndex);

        List<RoomKind> kinds = PickKinds(rng, RoomsFor(d), plan.Players, forced);
        List<Vector2I> cells = Layout(rng, kinds.Count + 2);

        for (int i = 0; i < cells.Count; i++)
        {
            RoomKind kind = i == 0 ? RoomKind.Start : i == cells.Count - 1 ? RoomKind.Reward : kinds[i - 1];
            var room = new RoomPlan
            {
                Kind = kind,
                Index = i,
                MacroX = cells[i].X,
                MacroZ = cells[i].Y,
                EntrySide = i > 0 ? SideBetween(cells[i], cells[i - 1]) : -1,
                ExitSide = i < cells.Count - 1 ? SideBetween(cells[i], cells[i + 1]) : -1,
                Slot = PuzzleState.Globals + i * PuzzleState.PerRoom,
            };
            var roomRng = new Rng(seed ^ ((ulong)(i + 1) * 0xD1B54A32D192ED03UL));
            switch (kind)
            {
                case RoomKind.Crates: GenCrates(room, roomRng, d); break;
                case RoomKind.Mirrors: GenMirrors(room, roomRng, d); break;
                case RoomKind.Wind: GenWind(room, roomRng, d); break;
                case RoomKind.Runes: GenRunes(room, roomRng, d); break;
                case RoomKind.Levers: GenLevers(room, roomRng, d); break;
                case RoomKind.Twin: GenTwin(room, roomRng, d); break;
            }
            if (kind is not (RoomKind.Start or RoomKind.Reward)) room.Enemies = Math.Min(4, 2 + d / 2);
            plan.Rooms.Add(room);
        }
        plan.StateLength = PuzzleState.Globals + plan.Rooms.Count * PuzzleState.PerRoom;
        int n = plan.PuzzleRooms;
        plan.GoldSeconds = 22f * n + 12f;
        plan.SilverSeconds = 40f * n + 20f;
        return plan;
    }

    // ------------------------------------------------------------- layout

    private static List<RoomKind> PickKinds(Rng rng, int count, int players, string? forced)
    {
        if (forced is { Length: > 0 })
        {
            var list = new List<RoomKind>();
            foreach (string s in forced.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (Enum.TryParse(s, true, out RoomKind k) && k is not (RoomKind.Start or RoomKind.Reward)) list.Add(k);
            if (list.Count > 0) return list;
        }
        var pool = new List<RoomKind> { RoomKind.Crates, RoomKind.Mirrors, RoomKind.Wind, RoomKind.Runes, RoomKind.Levers, RoomKind.Twin };
        for (int i = pool.Count - 1; i > 0; i--)
        {
            int j = rng.RangeInt(0, i + 1);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }
        List<RoomKind> pick = pool.Take(count).ToList();
        // A team always meets one room that needs two of them (with a latch
        // to fall back on if someone leaves).
        if (players >= 2 && !pick.Contains(RoomKind.Twin)) pick[^1] = RoomKind.Twin;
        return pick;
    }

    /// <summary>A self-avoiding walk of rooms on the macro grid, starting at
    /// the origin (the start room is always where the map's centre is).</summary>
    private static List<Vector2I> Layout(Rng rng, int count)
    {
        for (int attempt = 0; attempt < 200; attempt++)
        {
            var cells = new List<Vector2I> { Vector2I.Zero };
            int last = -1;
            while (cells.Count < count)
            {
                var options = new List<int>();
                for (int s = 0; s < 4; s++)
                {
                    var next = cells[^1] + new Vector2I(RoomPlan.Dx[s], RoomPlan.Dz[s]);
                    if (cells.Contains(next) || Math.Abs(next.X) > 2 || Math.Abs(next.Y) > 2) continue;
                    options.Add(s);
                    // A turn is likelier than a straight: the map winds.
                    if (s != last) options.Add(s);
                }
                if (options.Count == 0) break;
                int pick = options[rng.RangeInt(0, options.Count)];
                cells.Add(cells[^1] + new Vector2I(RoomPlan.Dx[pick], RoomPlan.Dz[pick]));
                last = pick;
            }
            if (cells.Count == count) return cells;
        }
        var line = new List<Vector2I>();
        for (int i = 0; i < count; i++) line.Add(new Vector2I(0, -i));
        return line;
    }

    private static int SideBetween(Vector2I from, Vector2I to)
    {
        Vector2I d = to - from;
        for (int s = 0; s < 4; s++) if (RoomPlan.Dx[s] == d.X && RoomPlan.Dz[s] == d.Y) return s;
        return -1;
    }

    private static bool Inner(int cell)
    {
        int x = RoomPlan.X(cell), z = RoomPlan.Z(cell);
        return x >= 1 && z >= 1 && x <= RoomPlan.N - 2 && z <= RoomPlan.N - 2;
    }

    private static bool Edge(int cell)
    {
        int x = RoomPlan.X(cell), z = RoomPlan.Z(cell);
        return x == 0 || z == 0 || x == RoomPlan.N - 1 || z == RoomPlan.N - 1;
    }

    private static List<int> FreeInner(RoomPlan room)
    {
        var list = new List<int>();
        for (int c = 0; c < RoomPlan.N * RoomPlan.N; c++)
            if (Inner(c) && !room.NearDoor(c) && !PuzzleSolver.Solid(room, c)) list.Add(c);
        return list;
    }

    private static int Take(Rng rng, List<int> from)
    {
        int i = rng.RangeInt(0, from.Count);
        int c = from[i];
        from.RemoveAt(i);
        return c;
    }

    private static void ClearPillars(RoomPlan room) => Array.Clear(room.Pillar);

    private static void Scatter(RoomPlan room, Rng rng, int count)
    {
        List<int> free = FreeInner(room);
        for (int i = 0; i < count && free.Count > 0; i++) room.Pillar[Take(rng, free)] = true;
    }

    /// <summary>A two-cell spike strip on free floor, away from the doors.</summary>
    private static void AddSpikes(RoomPlan room, Rng rng, Func<int, bool> keepClear)
    {
        List<int> free = FreeInner(room).Where(c => !keepClear(c)).ToList();
        for (int attempt = 0; attempt < 40 && free.Count > 0; attempt++)
        {
            int a = free[rng.RangeInt(0, free.Count)];
            int side = rng.RangeInt(0, 2) == 0 ? 0 : 1;
            int b = PuzzleSolver.Step(a, side);
            if (b < 0 || !free.Contains(b)) continue;
            room.Spikes = new[] { a, b };
            room.SpikePhase = rng.Range(0f, 3f);
            return;
        }
    }

    // ------------------------------------------------------------- crates

    private static void GenCrates(RoomPlan room, Rng rng, int d)
    {
        int crates = d == 0 ? 1 : d <= 2 ? (rng.Chance(0.5f) ? 2 : 1) : 2;
        int lo = 2 + d, hi = 8 + 3 * d;
        int[] corners = { RoomPlan.CellOf(0, 0), RoomPlan.CellOf(RoomPlan.N - 1, 0), RoomPlan.CellOf(0, RoomPlan.N - 1), RoomPlan.CellOf(RoomPlan.N - 1, RoomPlan.N - 1) };
        for (int attempt = 0; attempt < 160; attempt++)
        {
            if (attempt == 100) lo = 1;   // late attempts take any honest room
            ClearPillars(room);
            room.ResetCell = corners[rng.RangeInt(0, corners.Length)];
            Scatter(room, rng, rng.RangeInt(3, 6 + d));
            List<int> free = FreeInner(room);
            if (free.Count < crates * 2 + 2) continue;
            var plates = new int[crates + 1];
            for (int i = 0; i < plates.Length; i++) plates[i] = Take(rng, free);
            var start = new int[crates];
            bool dead = false;
            for (int i = 0; i < crates; i++)
            {
                start[i] = Take(rng, free);
                dead |= DeadCorner(room, start[i]);
            }
            if (dead) continue;
            room.Plates = plates;
            room.CrateStart = start;
            List<int>? moves = PuzzleSolver.SolveCrates(room, room.EntryCell, start, out int pushes, 60000);
            if (moves is null || pushes < lo || pushes > hi || !PuzzleSolver.DoorsConnected(room)) continue;
            room.CratePushes = pushes;
            return;
        }
        // Fallback: one crate two cells short of a plate, in a clear row.
        ClearPillars(room);
        room.ResetCell = corners[0];
        int z = room.EntrySide == 1 || room.ExitSide == 1 ? 3 : 5;
        room.CrateStart = new[] { RoomPlan.CellOf(3, z) };
        room.Plates = new[] { RoomPlan.CellOf(5, z), RoomPlan.CellOf(6, z == 3 ? 2 : 6) };
        PuzzleSolver.SolveCrates(room, room.EntryCell, room.CrateStart, out room.CratePushes);
    }

    /// <summary>A crate wedged between two walls at right angles can never move again.</summary>
    private static bool DeadCorner(RoomPlan room, int cell)
    {
        bool Blocked(int side)
        {
            int n = PuzzleSolver.Step(cell, side);
            return n < 0 || PuzzleSolver.Solid(room, n) || room.CrateForbidden(n);
        }
        return (Blocked(0) || Blocked(2)) && (Blocked(1) || Blocked(3));
    }

    // ------------------------------------------------------------ mirrors

    private static void GenMirrors(RoomPlan room, Rng rng, int d)
    {
        int onPath = 2 + Math.Min(d, 2);
        int decoys = 1 + Math.Min(d, 2);
        for (int attempt = 0; attempt < 400; attempt++)
        {
            if (attempt == 250) onPath = 2;
            ClearPillars(room);
            room.MirrorCells = Array.Empty<int>();
            room.EmitterCell = room.ReceiverCell = -1;
            if (TryMirrorPath(room, rng, onPath, decoys, d)) return;
        }
        // Fallback: emitter on the left wall, one mirror in the middle turning
        // the beam up to a crystal on the top wall.
        ClearPillars(room);
        int row = room.EntrySide == 2 || room.ExitSide == 2 ? 2 : RoomPlan.Mid;
        room.EmitterCell = RoomPlan.CellOf(0, row);
        room.EmitterDir = 0;
        int mirror = RoomPlan.CellOf(row == 2 ? 6 : 3, row);
        room.MirrorCells = new[] { mirror };
        room.ReceiverCell = RoomPlan.CellOf(RoomPlan.X(mirror), 0);
        room.MirrorStart = new[] { 1 };
    }

    private static bool TryMirrorPath(RoomPlan room, Rng rng, int onPath, int decoys, int d)
    {
        int wall = rng.RangeInt(0, 4);
        int along = rng.RangeInt(1, RoomPlan.N - 1);
        int emitter = wall switch
        {
            0 => RoomPlan.CellOf(RoomPlan.N - 1, along),
            1 => RoomPlan.CellOf(along, RoomPlan.N - 1),
            2 => RoomPlan.CellOf(0, along),
            _ => RoomPlan.CellOf(along, 0),
        };
        if (room.NearDoor(emitter)) return false;
        int dir = (wall + 2) % 4;
        var path = new HashSet<int> { emitter };
        var mirrors = new List<int>();
        var orient = new List<int>();
        int cur = emitter;
        for (int k = 0; k < onPath; k++)
        {
            int steps = rng.RangeInt(1, k == 0 ? 4 : 5);
            for (int t = 0; t < steps; t++)
            {
                cur = PuzzleSolver.Step(cur, dir);
                if (cur < 0 || !Inner(cur) || path.Contains(cur) || room.NearDoor(cur)) return false;
                path.Add(cur);
            }
            int turn = rng.Chance(0.5f) ? (dir + 1) % 4 : (dir + 3) % 4;
            mirrors.Add(cur);
            orient.Add(PuzzleSolver.OrientationFor(dir, turn));
            dir = turn;
        }
        while (true)
        {
            cur = PuzzleSolver.Step(cur, dir);
            if (cur < 0 || path.Contains(cur) || room.NearDoor(cur)) return false;
            path.Add(cur);
            if (Edge(cur)) break;
        }
        int receiver = cur;
        if (receiver == emitter) return false;

        room.EmitterCell = emitter;
        room.EmitterDir = (wall + 2) % 4;
        room.ReceiverCell = receiver;

        // Pillars and decoy mirrors, off the beam's true path.
        var spare = FreeInner(room).Where(c => !path.Contains(c)).ToList();
        for (int i = 0; i < 1 + rng.RangeInt(0, d + 1) && spare.Count > 0; i++) room.Pillar[Take(rng, spare)] = true;
        var cellsAll = new List<int>(mirrors);
        for (int i = 0; i < decoys && spare.Count > 0; i++) cellsAll.Add(Take(rng, spare));
        room.MirrorCells = cellsAll.ToArray();

        int start = 0;
        for (int i = 0; i < cellsAll.Count; i++) if (rng.Chance(0.5f)) start |= 1 << i;
        if (PuzzleSolver.Beam(room, start).Hit) start ^= 1;
        // At least two turns to make, so a room is never one press from done.
        int answer = PuzzleSolver.SolveMirrors(room, start);
        if (answer < 0) return false;
        if (PuzzleSolver.PopCount(answer ^ start) < 2)
        {
            for (int i = 0; i < mirrors.Count; i++)
                if (((answer ^ start) >> i & 1) == 0) { start ^= 1 << i; break; }
            answer = PuzzleSolver.SolveMirrors(room, start);
            if (answer < 0 || PuzzleSolver.Beam(room, start).Hit) return false;
        }
        room.MirrorStart = new int[cellsAll.Count];
        for (int i = 0; i < cellsAll.Count; i++) room.MirrorStart[i] = (start >> i) & 1;

        if (!PuzzleSolver.DoorsConnected(room)) return false;
        // Every mirror must have a floor cell beside it to stand on and turn it.
        int entry = room.EntryCell;
        foreach (int m in room.MirrorCells)
        {
            bool reachable = false;
            for (int s = 0; s < 4 && !reachable; s++)
            {
                int n = PuzzleSolver.Step(m, s);
                reachable = n >= 0 && !PuzzleSolver.Solid(room, n)
                            && PuzzleSolver.Path(room, entry, n, c => PuzzleSolver.Solid(room, c)) is not null;
            }
            if (!reachable) return false;
        }
        return true;
    }

    public static int MirrorStartBits(RoomPlan room)
    {
        int bits = 0;
        for (int i = 0; i < room.MirrorStart.Length; i++) bits |= room.MirrorStart[i] << i;
        return bits;
    }

    // --------------------------------------------------------------- wind

    private static void GenWind(RoomPlan room, Rng rng, int d)
    {
        int plates = d >= 2 ? 2 : 1;
        room.Calm = Mathf.Max(1.6f, 3.2f - 0.3f * d);
        room.Gust = Mathf.Min(2.8f, 1.8f + 0.2f * d);
        room.WindInner = 1.2f;
        if (plates == 1)
        {
            room.WindOuter = 5.2f;
            room.WindCentres = new[] { Vector2.Zero };
        }
        else
        {
            room.WindOuter = 3.4f;
            // The diagonal whose corners sit farther from the doors.
            Vector2 a = new(4.2f, 4.2f), b = new(-4.2f, 4.2f);
            float Clear(Vector2 c)
            {
                float best = float.MaxValue;
                foreach (int side in new[] { room.EntrySide, room.ExitSide })
                    if (side >= 0)
                    {
                        Vector2 door = RoomPlan.Local(RoomPlan.DoorCell(side));
                        best = Mathf.Min(best, Mathf.Min(door.DistanceTo(c), door.DistanceTo(-c)));
                    }
                return best;
            }
            Vector2 pick = Clear(a) >= Clear(b) ? a : b;
            if (rng.Chance(0.5f)) pick = -pick;
            room.WindCentres = new[] { pick, -pick };
        }
        room.WindPhase = new float[plates];
        for (int i = 0; i < plates; i++) room.WindPhase[i] = rng.Range(0f, room.Calm + room.Gust);
    }

    /// <summary>Is the gust ring around plate <paramref name="i"/> blowing at
    /// puzzle time <paramref name="t"/>? The cycle is calm first, then gust.</summary>
    public static bool Gusting(RoomPlan room, int i, float t) => WindCycle(room, i, t) >= room.Calm;

    /// <summary>Seconds into the calm/gust cycle of ring <paramref name="i"/>.</summary>
    public static float WindCycle(RoomPlan room, int i, float t)
    {
        float period = room.Calm + room.Gust;
        float p = (t + room.WindPhase[Math.Min(i, room.WindPhase.Length - 1)]) % period;
        return p < 0f ? p + period : p;
    }

    // -------------------------------------------------------------- runes

    private static void GenRunes(RoomPlan room, Rng rng, int d)
    {
        int bells = 4 + Math.Min(d, 2);
        float offset = rng.Range(0f, Mathf.Tau);
        room.Bells = new Vector2[bells];
        for (int i = 0; i < bells; i++)
        {
            float a = offset + Mathf.Tau * i / bells;
            room.Bells[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 5f;
        }
        var symbols = Enum.GetValues<RuneSymbol>().ToList();
        for (int i = symbols.Count - 1; i > 0; i--)
        {
            int j = rng.RangeInt(0, i + 1);
            (symbols[i], symbols[j]) = (symbols[j], symbols[i]);
        }
        room.BellSymbols = symbols.Take(bells).ToArray();
        int length = 3 + Math.Min(d, 3);
        room.Sequence = new int[length];
        for (int i = 0; i < length; i++)
        {
            int b;
            do b = rng.RangeInt(0, bells); while (i > 0 && b == room.Sequence[i - 1]);
            room.Sequence[i] = b;
        }
        if (d >= 1) AddSpikes(room, rng, c => RoomPlan.Local(c).Length() < 2.5f || NearAny(RoomPlan.Local(c), room.Bells, 2.2f));
    }

    private static bool NearAny(Vector2 at, Vector2[] points, float r)
    {
        foreach (Vector2 p in points) if (p.DistanceTo(at) < r) return true;
        return false;
    }

    /// <summary>How long one bell of the shown order stays lit.</summary>
    public const float RuneBeat = 0.85f;

    // ------------------------------------------------------------- levers

    private static void GenLevers(RoomPlan room, Rng rng, int d)
    {
        int k = 3 + Math.Min(d, 2);
        int turn = rng.RangeInt(0, 4);
        Vector2 Turn(Vector2 v) => turn switch
        {
            1 => new Vector2(-v.Y, v.X),
            2 => -v,
            3 => new Vector2(v.Y, -v.X),
            _ => v,
        };
        room.Levers = new Vector2[k];
        room.Lamps = new Vector2[k];
        for (int i = 0; i < k; i++)
        {
            float x = (i - (k - 1) * 0.5f) * 3f;
            room.Levers[i] = Turn(new Vector2(x, 5.6f));
            room.Lamps[i] = Turn(new Vector2(x, -2.6f));
        }
        int full = (1 << k) - 1;
        for (int attempt = 0; attempt < 200; attempt++)
        {
            room.LeverMasks = new int[k];
            for (int i = 0; i < k; i++)
            {
                int mask = 1 << i, extra = 0;
                var others = Enumerable.Range(0, k).Where(j => j != i).ToList();
                while (others.Count > 0 && extra < 2)
                {
                    int j = Take(rng, others);
                    if (!rng.Chance(0.35f + 0.05f * d) && !(d >= 1 && extra == 0)) continue;
                    mask |= 1 << j;
                    extra++;
                }
                room.LeverMasks[i] = mask;
            }
            int scramble = 0;
            while (PuzzleSolver.PopCount(scramble) < Math.Min(k - 1, 2 + d / 2)) scramble |= 1 << rng.RangeInt(0, k);
            room.LampStart = full;
            for (int i = 0; i < k; i++) if ((scramble >> i & 1) != 0) room.LampStart ^= room.LeverMasks[i];
            if (room.LampStart == full) continue;
            int answer = PuzzleSolver.SolveLevers(room, 0);
            if (answer < 0 || PuzzleSolver.PopCount(answer) < Math.Min(2, k)) continue;
            break;
        }
        if (d >= 2) AddSpikes(room, rng, c => NearAny(RoomPlan.Local(c), room.Levers, 2.2f) || NearAny(RoomPlan.Local(c), room.Lamps, 2.2f));
    }

    // --------------------------------------------------------------- twin

    private static void GenTwin(RoomPlan room, Rng rng, int d)
    {
        room.Latch = Mathf.Max(5f, 9f - d);
        for (int attempt = 0; attempt < 200; attempt++)
        {
            ClearPillars(room);
            bool vertical = rng.Chance(0.5f);
            bool gapLow = rng.Chance(0.5f);
            // A divider down the middle with a gap at one end, so the plates
            // are a walk apart, not a step.
            for (int t = 0; t < RoomPlan.N; t++)
            {
                if (gapLow ? t <= 1 : t >= RoomPlan.N - 2) continue;
                int c = vertical ? RoomPlan.CellOf(RoomPlan.Mid, t) : RoomPlan.CellOf(t, RoomPlan.Mid);
                if (!room.NearDoor(c)) room.Pillar[c] = true;
            }
            int a = vertical ? RoomPlan.CellOf(1 + rng.RangeInt(0, 2), 1 + rng.RangeInt(1, 6)) : RoomPlan.CellOf(1 + rng.RangeInt(1, 6), 1 + rng.RangeInt(0, 2));
            int b = vertical ? RoomPlan.CellOf(RoomPlan.N - 2 - rng.RangeInt(0, 2), 1 + rng.RangeInt(1, 6))
                             : RoomPlan.CellOf(1 + rng.RangeInt(1, 6), RoomPlan.N - 2 - rng.RangeInt(0, 2));
            if (room.NearDoor(a) || room.NearDoor(b) || room.Pillar[a] || room.Pillar[b]) continue;
            room.PlateA = a;
            room.PlateB = b;
            if (!PuzzleSolver.DoorsConnected(room) || !PuzzleSolver.TwinSoloSolvable(room)) continue;
            return;
        }
        ClearPillars(room);
        room.PlateA = RoomPlan.CellOf(2, 2);
        room.PlateB = RoomPlan.CellOf(6, 6);
    }
}
