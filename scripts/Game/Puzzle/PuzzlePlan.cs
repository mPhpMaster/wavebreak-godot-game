using System.Collections.Generic;
using Godot;

namespace Wavebreak.Game.Puzzle;

/// <summary>
/// One room of a puzzle map as data: where it sits, which sides have doors,
/// its grid of cells and the pieces on it. The generator writes it, the
/// solver and the rules read it, and <see cref="PuzzleMap"/> builds nodes from
/// it. Nothing in here is a node, so <c>--check-puzzles</c> can generate,
/// solve and play hundreds of rooms without a scene.
///
/// The floor is a <see cref="N"/> x <see cref="N"/> grid of
/// <see cref="Cell"/>-metre cells. Cell (x, z) is index <c>z * N + x</c>;
/// side 0 is +X, 1 is +Z, 2 is -X, 3 is -Z (<see cref="Dx"/>, <see cref="Dz"/>).
/// </summary>
public sealed class RoomPlan
{
    public const int N = 9;
    public const float Cell = 2f;
    /// <summary>Centre to centre between neighbouring rooms: the floor plus a wall.</summary>
    public const float Pitch = N * Cell + 2f;
    public const int Mid = N / 2;

    public static readonly int[] Dx = { 1, 0, -1, 0 };
    public static readonly int[] Dz = { 0, 1, 0, -1 };

    public RoomKind Kind;
    public int Index;
    public int MacroX, MacroZ;
    /// <summary>Side the team comes in through, or -1 (the start room).</summary>
    public int EntrySide = -1;
    /// <summary>Side the next room is on, or -1 (the reward room).</summary>
    public int ExitSide = -1;
    /// <summary>First index of this room's values in <see cref="PuzzleState"/>.</summary>
    public int Slot;

    public readonly bool[] Pillar = new bool[N * N];

    // Crates: pushable crates and the plates they (and one player) hold down.
    public int[] CrateStart = System.Array.Empty<int>();
    public int[] Plates = System.Array.Empty<int>();
    public int ResetCell = -1;
    public int CratePushes;

    // Mirrors: an emitter on a wall cell shining inward, a crystal on another.
    public int EmitterCell = -1;
    public int EmitterDir;
    public int ReceiverCell = -1;
    public int[] MirrorCells = System.Array.Empty<int>();
    /// <summary>Starting orientation per mirror: 0 is "/", 1 is "\".</summary>
    public int[] MirrorStart = System.Array.Empty<int>();

    // Wind: plates on islands inside gusting rings.
    public Vector2[] WindCentres = System.Array.Empty<Vector2>();
    public float WindInner = 1.2f, WindOuter = 4f;
    public float Calm = 3f, Gust = 2f;
    public float[] WindPhase = System.Array.Empty<float>();

    // Runes: bells round the room, the order to strike them, an echo bell.
    public Vector2[] Bells = System.Array.Empty<Vector2>();
    public RuneSymbol[] BellSymbols = System.Array.Empty<RuneSymbol>();
    public int[] Sequence = System.Array.Empty<int>();

    // Levers: each flips a mask of lamps; the start is a scrambled board.
    public Vector2[] Levers = System.Array.Empty<Vector2>();
    public Vector2[] Lamps = System.Array.Empty<Vector2>();
    public int[] LeverMasks = System.Array.Empty<int>();
    public int LampStart;

    // Twin plates: A and B, apart; the latch a lone player gets.
    public int PlateA = -1, PlateB = -1;
    public float Latch = 8f;

    /// <summary>Spike strips: cells that rise and fall on the puzzle clock.</summary>
    public int[] Spikes = System.Array.Empty<int>();
    public float SpikePhase;

    /// <summary>Enemies the host sends in when the team first enters.</summary>
    public int Enemies;

    /// <summary>Room centre on the floor, world space.</summary>
    public Vector3 Centre => new(MacroX * Pitch, 0f, MacroZ * Pitch);

    public static int CellOf(int x, int z) => z * N + x;
    public static int X(int cell) => cell % N;
    public static int Z(int cell) => cell / N;
    public static bool InGrid(int x, int z) => x >= 0 && z >= 0 && x < N && z < N;

    /// <summary>Cell centre in room-local metres (the room centre is 0, 0).</summary>
    public static Vector2 Local(int cell) => new((X(cell) - Mid) * Cell, (Z(cell) - Mid) * Cell);

    public Vector3 World(int cell)
    {
        Vector2 l = Local(cell);
        return Centre + new Vector3(l.X, 0f, l.Y);
    }

    public Vector3 World(Vector2 local) => Centre + new Vector3(local.X, 0f, local.Y);

    /// <summary>The cell under a world position, or -1 off the floor.</summary>
    public int CellAt(Vector3 world)
    {
        Vector3 d = world - Centre;
        int x = Mathf.RoundToInt(d.X / Cell) + Mid, z = Mathf.RoundToInt(d.Z / Cell) + Mid;
        return InGrid(x, z) ? CellOf(x, z) : -1;
    }

    /// <summary>Inside this room's floor (with a margin in from the walls).</summary>
    public bool Contains(Vector3 world, float margin = 0f)
    {
        Vector3 d = world - Centre;
        float half = N * Cell * 0.5f - margin;
        return Mathf.Abs(d.X) <= half && Mathf.Abs(d.Z) <= half;
    }

    /// <summary>The edge cell in front of the door on <paramref name="side"/>.</summary>
    public static int DoorCell(int side) => side switch
    {
        0 => CellOf(N - 1, Mid),
        1 => CellOf(Mid, N - 1),
        2 => CellOf(0, Mid),
        3 => CellOf(Mid, 0),
        _ => -1,
    };

    public int EntryCell => DoorCell(EntrySide);
    public int ExitCell => DoorCell(ExitSide);

    /// <summary>A door cell of this room, or the cell just inside it: kept clear
    /// of pillars and pieces so the way through never closes.</summary>
    public bool NearDoor(int cell)
    {
        foreach (int side in new[] { EntrySide, ExitSide })
        {
            if (side < 0) continue;
            int door = DoorCell(side);
            if (cell == door) return true;
            int inX = X(door) - Dx[side], inZ = Z(door) - Dz[side];
            if (cell == CellOf(inX, inZ)) return true;
        }
        return false;
    }

    /// <summary>The cell a crate may never enter: in front of either door.</summary>
    public bool CrateForbidden(int cell) => cell == EntryCell || cell == ExitCell;
}

/// <summary>A whole puzzle map: the rooms in order, from the start room
/// through the puzzle rooms to the reward vault.</summary>
public sealed class PuzzlePlan
{
    public ulong Seed;
    public int PuzzleIndex;
    public int Difficulty;
    public int Players;
    public string Name = "";
    public readonly List<RoomPlan> Rooms = new();
    /// <summary>Values in a <see cref="PuzzleState"/> for this plan.</summary>
    public int StateLength;
    /// <summary>Seconds after which the SKIP portal opens.</summary>
    public float SkipSeconds = PuzzleState.SkipSeconds;
    /// <summary>Solve time for a gold medal (no hints either).</summary>
    public float GoldSeconds;
    /// <summary>Solve time for silver (at most one hint).</summary>
    public float SilverSeconds;

    /// <summary>Puzzle rooms (not the start room or the vault).</summary>
    public int PuzzleRooms => Rooms.Count - 2;

    public RoomPlan Start => Rooms[0];
    public RoomPlan Vault => Rooms[^1];

    /// <summary>Distance from the start room's centre (the origin) to the
    /// farthest corner of any room, for the map's clamp.</summary>
    public float Reach
    {
        get
        {
            float r = 0f;
            foreach (RoomPlan room in Rooms)
                r = Mathf.Max(r, new Vector2(room.Centre.X, room.Centre.Z).Length() + RoomPlan.Pitch * 0.75f);
            return r;
        }
    }

    /// <summary>The room under a world position, or -1.</summary>
    public int RoomAt(Vector3 world)
    {
        for (int i = 0; i < Rooms.Count; i++)
            if (Rooms[i].Contains(world, -0.6f)) return i;
        return -1;
    }

    /// <summary>The middle of the doorway between room <paramref name="i"/> and the next.</summary>
    public Vector3 DoorBetween(int i)
    {
        RoomPlan a = Rooms[i];
        int s = a.ExitSide;
        return a.Centre + new Vector3(RoomPlan.Dx[s], 0f, RoomPlan.Dz[s]) * (RoomPlan.Pitch * 0.5f);
    }

    public string Describe()
    {
        var parts = new List<string>();
        foreach (RoomPlan r in Rooms) parts.Add(r.Kind.ToString());
        return string.Join(",", parts);
    }
}
