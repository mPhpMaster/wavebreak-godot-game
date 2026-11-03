using System;
using System.Collections.Generic;
using Godot;

namespace Wavebreak.Game.Puzzle;

/// <summary>
/// Grid helpers every kind's solver shares: what is solid, shortest walks,
/// a step to a neighbour, whether a room's doors stay connected, and the
/// walking speed the timing rooms are tuned for. Each kind's own solver lives
/// in its file (Game/Puzzle/Kinds); all of them are small exhaustive
/// searches, so "no answer" really means there is none.
/// </summary>
public static class PuzzleSolver
{
    /// <summary>A cell nothing can stand in: a pillar or a fixed piece.</summary>
    public static bool Solid(RoomPlan room, int cell) => cell < 0 || room.Pillar[cell] || room.Fixed[cell];

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
        if (cell < 0) return -1;
        int x = RoomPlan.X(cell) + RoomPlan.Dx[side], z = RoomPlan.Z(cell) + RoomPlan.Dz[side];
        return RoomPlan.InGrid(x, z) ? RoomPlan.CellOf(x, z) : -1;
    }

    /// <summary>The side from <paramref name="a"/> to its neighbour <paramref name="b"/>, or -1.</summary>
    public static int SideTo(int a, int b)
    {
        for (int s = 0; s < 4; s++) if (Step(a, s) == b && b >= 0) return s;
        return -1;
    }

    /// <summary>Walking speed the checks assume: the base 6 m/s with no items.</summary>
    public const float WalkSpeed = 6f;

    /// <summary>Margin the timing rooms leave over a straight walk.</summary>
    public const float TimingMargin = 1.4f;

    /// <summary>The way between the two doors stays open (every room must be
    /// crossable whatever its pieces).</summary>
    public static bool DoorsConnected(RoomPlan room)
    {
        if (room.EntrySide < 0 || room.ExitSide < 0) return true;
        return Path(room, room.EntryCell, room.ExitCell, c => Solid(room, c)) is not null;
    }

    /// <summary>Every cell beside <paramref name="cell"/> that a player can
    /// reach from the entrance (to stand at a piece and use it).</summary>
    public static bool Reachable(RoomPlan room, int cell)
    {
        for (int s = 0; s < 4; s++)
        {
            int n = Step(cell, s);
            if (n >= 0 && !Solid(room, n) && Path(room, room.EntryCell, n, c => Solid(room, c)) is not null) return true;
        }
        return false;
    }

    public static int PopCount(int v)
    {
        int n = 0;
        for (; v != 0; v &= v - 1) n++;
        return n;
    }
}
