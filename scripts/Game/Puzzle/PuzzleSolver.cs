using System;
using System.Collections.Generic;
using Godot;

namespace Wavebreak.Game.Puzzle;

/// <summary>
/// Solvers for every piece type, independent of the generator that made the
/// room. The generator only keeps rooms these can solve; <c>--check-puzzles</c>
/// asks them again from scratch and plays their answer through the real rules;
/// the autopilot asks them from the room's current state, so it recovers from
/// whatever a teammate (or a shove) did in between.
///
/// All of them are small exhaustive searches: a crate room is a breadth-first
/// search over (player cell, crate cells), mirrors and levers are a walk over
/// every on/off combination (at most 2^8). Nothing is a heuristic, so "no
/// answer" really means there is none.
/// </summary>
public static class PuzzleSolver
{
    // --------------------------------------------------------------- grid

    /// <summary>A cell nothing can stand in: a pillar or a fixed piece.</summary>
    public static bool Solid(RoomPlan room, int cell)
    {
        if (cell < 0 || room.Pillar[cell] || cell == room.ResetCell || cell == room.EmitterCell || cell == room.ReceiverCell) return true;
        foreach (int m in room.MirrorCells) if (m == cell) return true;
        return false;
    }

    /// <summary>Shortest walk between two cells (4-way), as the cells after
    /// <paramref name="from"/>; null when there is none.</summary>
    public static List<int>? Path(RoomPlan room, int from, int to, Func<int, bool> blocked)
    {
        if (from < 0 || to < 0) return null;
        if (from == to) return new List<int>();
        var prev = new int[RoomPlan.N * RoomPlan.N];
        Array.Fill(prev, -2);
        var queue = new Queue<int>();
        queue.Enqueue(from);
        prev[from] = -1;
        while (queue.Count > 0)
        {
            int c = queue.Dequeue();
            if (c == to) break;
            for (int s = 0; s < 4; s++)
            {
                int x = RoomPlan.X(c) + RoomPlan.Dx[s], z = RoomPlan.Z(c) + RoomPlan.Dz[s];
                if (!RoomPlan.InGrid(x, z)) continue;
                int n = RoomPlan.CellOf(x, z);
                if (prev[n] != -2 || (n != to && blocked(n))) continue;
                prev[n] = c;
                queue.Enqueue(n);
            }
        }
        if (prev[to] == -2) return null;
        var path = new List<int>();
        for (int c = to; c != from; c = prev[c]) path.Add(c);
        path.Reverse();
        return path;
    }

    public static int Step(int cell, int side)
    {
        int x = RoomPlan.X(cell) + RoomPlan.Dx[side], z = RoomPlan.Z(cell) + RoomPlan.Dz[side];
        return RoomPlan.InGrid(x, z) ? RoomPlan.CellOf(x, z) : -1;
    }

    // ------------------------------------------------------------- crates

    /// <summary>Every plate has a crate or the player on it.</summary>
    public static bool CratesGoal(RoomPlan room, int player, IReadOnlyList<int> crates)
    {
        foreach (int p in room.Plates)
        {
            if (p == player) continue;
            bool held = false;
            for (int i = 0; i < crates.Count; i++) if (crates[i] == p) { held = true; break; }
            if (!held) return false;
        }
        return true;
    }

    /// <summary>Can a crate move into <paramref name="cell"/>?</summary>
    public static bool CrateCanEnter(RoomPlan room, int cell, IReadOnlyList<int> crates)
    {
        if (cell < 0 || Solid(room, cell) || room.CrateForbidden(cell)) return false;
        for (int i = 0; i < crates.Count; i++) if (crates[i] == cell) return false;
        return true;
    }

    /// <summary>The player's moves (sides) to solve a crate room from this
    /// state, shortest in steps; null when it cannot be solved from here.
    /// <paramref name="pushes"/> counts the moves that push a crate.</summary>
    public static List<int>? SolveCrates(RoomPlan room, int player, IReadOnlyList<int> crates, out int pushes, int maxNodes = 200000)
    {
        pushes = 0;
        if (player < 0) return null;
        int n = crates.Count;
        int Key(int p, int[] c)
        {
            int k = p;
            for (int i = 0; i < n; i++) k |= c[i] << (7 * (i + 1));
            return k;
        }
        int[] Unpack(int key, out int p)
        {
            p = key & 127;
            var c = new int[n];
            for (int i = 0; i < n; i++) c[i] = (key >> (7 * (i + 1))) & 127;
            return c;
        }

        int[] start = new int[n];
        for (int i = 0; i < n; i++) start[i] = crates[i];
        Array.Sort(start);
        int startKey = Key(player, start);
        if (CratesGoal(room, player, start)) return new List<int>();

        var parent = new Dictionary<int, (int Prev, sbyte Side, bool Push)> { [startKey] = (-1, -1, false) };
        var queue = new Queue<int>();
        queue.Enqueue(startKey);
        int goal = -1;
        while (queue.Count > 0 && parent.Count < maxNodes)
        {
            int key = queue.Dequeue();
            int[] c = Unpack(key, out int p);
            for (int s = 0; s < 4; s++)
            {
                int next = Step(p, s);
                if (next < 0 || Solid(room, next)) continue;
                int hit = Array.IndexOf(c, next);
                int[] nc = c;
                bool push = false;
                if (hit >= 0)
                {
                    int beyond = Step(next, s);
                    if (!CrateCanEnter(room, beyond, c)) continue;
                    nc = (int[])c.Clone();
                    nc[hit] = beyond;
                    Array.Sort(nc);
                    push = true;
                }
                int nk = Key(next, nc);
                if (parent.ContainsKey(nk)) continue;
                parent[nk] = (key, (sbyte)s, push);
                if (CratesGoal(room, next, nc)) { goal = nk; break; }
                queue.Enqueue(nk);
            }
            if (goal >= 0) break;
        }
        if (goal < 0) return null;
        var moves = new List<int>();
        for (int k = goal; parent[k].Prev >= 0; k = parent[k].Prev)
        {
            moves.Add(parent[k].Side);
            if (parent[k].Push) pushes++;
        }
        moves.Reverse();
        return moves;
    }

    // ------------------------------------------------------------ mirrors

    /// <summary>Where a beam leaving <paramref name="side"/> goes off a mirror of
    /// orientation <paramref name="o"/> (0 "/", 1 "\").</summary>
    public static int Reflect(int side, int o)
    {
        int dx = RoomPlan.Dx[side], dz = RoomPlan.Dz[side];
        int nx = o == 0 ? -dz : dz, nz = o == 0 ? -dx : dx;
        for (int s = 0; s < 4; s++) if (RoomPlan.Dx[s] == nx && RoomPlan.Dz[s] == nz) return s;
        return side;
    }

    /// <summary>The orientation that turns a beam from <paramref name="inSide"/> to <paramref name="outSide"/>.</summary>
    public static int OrientationFor(int inSide, int outSide) => Reflect(inSide, 1) == outSide ? 1 : 0;

    /// <summary>The beam's cells from the emitter, and whether it lands on the
    /// crystal. <paramref name="orient"/> holds one bit per mirror.</summary>
    public static (List<int> Cells, bool Hit) Beam(RoomPlan room, int orient)
    {
        var cells = new List<int> { room.EmitterCell };
        int cur = room.EmitterCell, side = room.EmitterDir;
        for (int guard = 0; guard < RoomPlan.N * RoomPlan.N * 4; guard++)
        {
            int next = Step(cur, side);
            if (next < 0) return (cells, false);
            cells.Add(next);
            if (next == room.ReceiverCell) return (cells, true);
            if (room.Pillar[next]) return (cells, false);
            int m = Array.IndexOf(room.MirrorCells, next);
            if (m >= 0) side = Reflect(side, (orient >> m) & 1);
            cur = next;
        }
        return (cells, false);
    }

    /// <summary>The orientation bits that light the crystal with the fewest
    /// turns from <paramref name="current"/>; -1 when nothing does.</summary>
    public static int SolveMirrors(RoomPlan room, int current)
    {
        int m = room.MirrorCells.Length, best = -1, bestFlips = int.MaxValue;
        for (int bits = 0; bits < 1 << m; bits++)
        {
            if (!Beam(room, bits).Hit) continue;
            int flips = PopCount(bits ^ current);
            if (flips < bestFlips) { best = bits; bestFlips = flips; }
        }
        return best;
    }

    // ------------------------------------------------------------- levers

    public static int FullLamps(RoomPlan room) => (1 << room.Lamps.Length) - 1;

    /// <summary>Which lamps are lit with these levers pulled (one bit each).</summary>
    public static int Lamps(RoomPlan room, int pulled)
    {
        int lit = room.LampStart;
        for (int i = 0; i < room.LeverMasks.Length; i++)
            if (((pulled >> i) & 1) != 0) lit ^= room.LeverMasks[i];
        return lit;
    }

    /// <summary>The fewest extra levers to pull (one bit each) to light every
    /// lamp from <paramref name="pulled"/>; -1 when nothing does. Pulling a
    /// lever twice undoes it, so each one is pulled at most once.</summary>
    public static int SolveLevers(RoomPlan room, int pulled)
    {
        int k = room.LeverMasks.Length, best = -1, bestCount = int.MaxValue;
        for (int bits = 0; bits < 1 << k; bits++)
        {
            if (Lamps(room, pulled ^ bits) != FullLamps(room)) continue;
            int count = PopCount(bits);
            if (count < bestCount) { best = bits; bestCount = count; }
        }
        return best;
    }

    // --------------------------------------------------------- wind, twin

    /// <summary>Walking speed the checks assume: the base 6 m/s with no items.</summary>
    public const float WalkSpeed = 6f;

    /// <summary>Margin the timing rooms leave over a straight walk.</summary>
    public const float TimingMargin = 1.4f;

    /// <summary>A gust ring can be crossed in one calm, walking, with margin.</summary>
    public static bool WindCrossable(RoomPlan room)
        => room.WindCentres.Length > 0 && (room.WindOuter - room.WindInner) / WalkSpeed * TimingMargin <= room.Calm;

    /// <summary>Seconds of walking from plate A to plate B around the pillars.</summary>
    public static float TwinWalkSeconds(RoomPlan room)
    {
        List<int>? path = Path(room, room.PlateA, room.PlateB, c => Solid(room, c));
        return path is null ? float.PositiveInfinity : path.Count * RoomPlan.Cell / WalkSpeed;
    }

    /// <summary>A lone player can make it from one plate to the other in the latch.</summary>
    public static bool TwinSoloSolvable(RoomPlan room) => TwinWalkSeconds(room) * TimingMargin <= room.Latch;

    /// <summary>The way between the two doors stays open (every room must be
    /// crossable whatever its pieces).</summary>
    public static bool DoorsConnected(RoomPlan room)
    {
        if (room.EntrySide < 0 || room.ExitSide < 0) return true;
        return Path(room, room.EntryCell, room.ExitCell, c => Solid(room, c)) is not null;
    }

    public static int PopCount(int v)
    {
        int n = 0;
        for (; v != 0; v &= v - 1) n++;
        return n;
    }
}
