using System;

namespace Wavebreak.Game.Puzzle.Kinds;

/// <summary>
/// The 9 x 9 puzzle grid as one 81-bit board (a <see cref="UInt128"/>, bit
/// = cell), for the push puzzles' searches (crates, ice): a flood of where a
/// player can walk is a handful of shifts instead of a queue, which is what
/// lets their breadth-first searches visit tens of thousands of states in a
/// few milliseconds. Players can walk round the grid in the hall's open strip,
/// so every reachable edge cell joins every free edge cell.
/// </summary>
public static class GridBits
{
    private const int N = RoomPlan.N;

    public static readonly UInt128 All = (UInt128.One << (N * N)) - 1;
    public static readonly UInt128 West, East, Edge;

    static GridBits()
    {
        UInt128 w = 0, e = 0, edge = 0;
        for (int c = 0; c < N * N; c++)
        {
            if (RoomPlan.X(c) == 0) w |= Bit(c);
            if (RoomPlan.X(c) == N - 1) e |= Bit(c);
            if (PuzzleGen.Edge(c)) edge |= Bit(c);
        }
        West = w;
        East = e;
        Edge = edge;
    }

    public static UInt128 Bit(int cell) => UInt128.One << cell;

    public static bool Has(UInt128 b, int cell) => cell >= 0 && ((b >> cell) & UInt128.One) != UInt128.Zero;

    /// <summary>The lowest cell in the board, or -1 when empty.</summary>
    public static int Lowest(UInt128 b) => b == UInt128.Zero ? -1 : (int)UInt128.TrailingZeroCount(b);

    /// <summary>Every cell one step from the board (4-way).</summary>
    public static UInt128 Grow(UInt128 b)
        => b | ((b << 1) & ~West) | ((b >> 1) & ~East) | ((b << N) & All) | (b >> N);

    /// <summary>Cells a player at <paramref name="from"/> can walk to over
    /// <paramref name="free"/> (the strip round the grid included).</summary>
    public static UInt128 Flood(int from, UInt128 free)
    {
        if (!Has(free, from)) return UInt128.Zero;
        UInt128 r = Bit(from);
        bool strip = false;
        while (true)
        {
            UInt128 next = Grow(r) & free;
            if (!strip && (next & Edge) != UInt128.Zero)
            {
                strip = true;
                next |= Edge & free;
            }
            if (next == r) return r;
            r = next;
        }
    }

    /// <summary>Cells nothing stands on (no pillar, no fixed piece).</summary>
    public static UInt128 Open(RoomPlan room)
    {
        UInt128 b = UInt128.Zero;
        for (int c = 0; c < N * N; c++) if (!PuzzleSolver.Solid(room, c)) b |= Bit(c);
        return b;
    }
}
