using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Wavebreak.Core;

namespace Wavebreak.Game.Puzzle;

/// <summary>
/// Builds a puzzle map from the run's seed: a winding chain of rooms (start,
/// three to five puzzle rooms, the reward vault), each with a
/// <see cref="PuzzleKind"/> and a layout drawn for this run. Every machine in
/// a co-op session calls it with the same inputs (seed, puzzle number, tier)
/// and gets the same map, so only the puzzle's state ever travels.
///
/// <b>No repeats.</b> The kinds come from one shuffle bag per run
/// (<see cref="PuzzleKinds.Sequence"/>): puzzle i takes the rooms after
/// those of puzzles 0..i-1, so every kind turns up once before any comes
/// back, and a map never holds the same kind twice.
///
/// <b>Never unsolvable.</b> Each kind's <see cref="PuzzleKind.Generate"/>
/// keeps only layouts its own solver solves (alone) and whose doors stay
/// connected, redrawing and finally falling back to a fixed layout that
/// passes the same checks.
///
/// <b>Difficulty</b> is the puzzle's number in the run plus a step for Hard
/// and up (<see cref="DifficultyFor"/>). It adds rooms, and each kind reads
/// it for its own knobs (crates, mirrors, bells, tiles, weights...).
/// </summary>
public static class PuzzleGen
{
    public const int MaxDifficulty = 5;

    /// <summary>--puzzle-rooms=trail,crates: force the puzzle rooms (tests and captures).</summary>
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

    /// <summary>Puzzle rooms before puzzle <paramref name="puzzleIndex"/> in a run on this tier.</summary>
    public static int RoomsBefore(int puzzleIndex, int tierIndex)
    {
        int n = 0;
        for (int j = 0; j < puzzleIndex; j++) n += RoomsFor(DifficultyFor(j, tierIndex));
        return n;
    }

    /// <summary>Puzzle <paramref name="puzzleIndex"/> of a run on tier <paramref name="tierIndex"/>.</summary>
    public static PuzzlePlan ForRun(ulong runSeed, int puzzleIndex, int tierIndex, int players, string? forced = null, bool gauntlet = false)
        => Build(runSeed, puzzleIndex, DifficultyFor(puzzleIndex, tierIndex), players, forced, RoomsBefore(puzzleIndex, tierIndex), gauntlet);

    /// <summary>A puzzle map. <paramref name="bagOffset"/> is where its rooms
    /// start in the run's shuffle bag (<see cref="RoomsBefore"/>).</summary>
    public static PuzzlePlan Build(ulong runSeed, int puzzleIndex, int difficulty, int players, string? forced = null, int bagOffset = 0, bool gauntlet = false)
    {
        ulong seed = SeedFor(runSeed, puzzleIndex);
        var rng = new Rng(seed);
        var plan = new PuzzlePlan
        {
            Seed = seed,
            PuzzleIndex = puzzleIndex,
            Difficulty = Math.Clamp(difficulty, 0, MaxDifficulty),
            Players = Math.Max(1, players),
            Gauntlet = gauntlet,
        };
        int d = plan.Difficulty;
        plan.Name = NameFor(runSeed, puzzleIndex);

        List<PuzzleKind> kinds = PickKinds(runSeed, Math.Max(0, bagOffset), RoomsFor(d), forced);
        List<Vector2I> cells = Layout(rng, kinds.Count + 2);

        for (int i = 0; i < cells.Count; i++)
        {
            RoomKind kind = i == 0 ? RoomKind.Start : i == cells.Count - 1 ? RoomKind.Reward : RoomKind.Puzzle;
            var room = new RoomPlan
            {
                Kind = kind,
                Def = kind == RoomKind.Puzzle ? kinds[i - 1] : null,
                Index = i,
                MacroX = cells[i].X,
                MacroZ = cells[i].Y,
                EntrySide = i > 0 ? SideBetween(cells[i], cells[i - 1]) : -1,
                ExitSide = i < cells.Count - 1 ? SideBetween(cells[i], cells[i + 1]) : -1,
                Slot = PuzzleState.Globals + i * PuzzleState.PerRoom,
            };
            var roomRng = new Rng(seed ^ ((ulong)(i + 1) * 0xD1B54A32D192ED03UL));
            if (room.Def is { } def)
            {
                def.Generate(room, roomRng, d);
                if (!gauntlet) room.Enemies = Math.Min(4, 2 + d / 2);
            }
            plan.Rooms.Add(room);
        }
        plan.StateLength = PuzzleState.Globals + plan.Rooms.Count * PuzzleState.PerRoom;
        float par = 0f;
        foreach (RoomPlan r in plan.Rooms) if (r.Def is { } k) par += k.ParSeconds;
        plan.GoldSeconds = par + 12f;
        plan.SilverSeconds = par * (40f / 22f) + 20f;
        return plan;
    }

    // ------------------------------------------------------------- layout

    private static List<PuzzleKind> PickKinds(ulong runSeed, int offset, int count, string? forced)
    {
        if (forced is { Length: > 0 })
        {
            var list = new List<PuzzleKind>();
            foreach (string s in forced.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (PuzzleKinds.ById(s) is { } k) list.Add(k);
            if (list.Count > 0) return list;
        }
        return PuzzleKinds.Sequence(runSeed, offset + count).Skip(offset).ToList();
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

    // ------------------------------------------------- helpers for kinds

    /// <summary>Not on the grid's outer ring.</summary>
    public static bool Inner(int cell)
    {
        int x = RoomPlan.X(cell), z = RoomPlan.Z(cell);
        return x >= 1 && z >= 1 && x <= RoomPlan.N - 2 && z <= RoomPlan.N - 2;
    }

    /// <summary>On the grid's outer ring.</summary>
    public static bool Edge(int cell)
    {
        int x = RoomPlan.X(cell), z = RoomPlan.Z(cell);
        return x == 0 || z == 0 || x == RoomPlan.N - 1 || z == RoomPlan.N - 1;
    }

    /// <summary>Inner cells away from the doors with nothing on them.</summary>
    public static List<int> FreeInner(RoomPlan room)
    {
        var list = new List<int>();
        for (int c = 0; c < RoomPlan.N * RoomPlan.N; c++)
            if (Inner(c) && !room.NearDoor(c) && !PuzzleSolver.Solid(room, c)) list.Add(c);
        return list;
    }

    /// <summary>Remove and return a random entry.</summary>
    public static int Take(Rng rng, List<int> from)
    {
        int i = rng.RangeInt(0, from.Count);
        int c = from[i];
        from.RemoveAt(i);
        return c;
    }

    public static void Shuffle<T>(Rng rng, IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.RangeInt(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    /// <summary>Forget a failed draw: pillars, fixed cells, spots, spikes.</summary>
    public static void Clear(RoomPlan room)
    {
        Array.Clear(room.Pillar);
        Array.Clear(room.Fixed);
        room.Spots.Clear();
        room.Spikes = Array.Empty<int>();
    }

    public static void Scatter(RoomPlan room, Rng rng, int count)
    {
        List<int> free = FreeInner(room);
        for (int i = 0; i < count && free.Count > 0; i++) room.Pillar[Take(rng, free)] = true;
    }

    /// <summary>A two-cell spike strip on free floor, away from the doors.</summary>
    public static void AddSpikes(RoomPlan room, Rng rng, Func<int, bool> keepClear)
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

    public static bool NearAny(Vector2 at, IEnumerable<Vector2> points, float r)
    {
        foreach (Vector2 p in points) if (p.DistanceTo(at) < r) return true;
        return false;
    }

    /// <summary>A corner cell of the grid, for a reset bell.</summary>
    public static int Corner(int i) => (i & 3) switch
    {
        0 => RoomPlan.CellOf(0, 0),
        1 => RoomPlan.CellOf(RoomPlan.N - 1, 0),
        2 => RoomPlan.CellOf(0, RoomPlan.N - 1),
        _ => RoomPlan.CellOf(RoomPlan.N - 1, RoomPlan.N - 1),
    };
}
