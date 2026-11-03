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
///
/// The grid is the puzzle floor in the middle of a bigger hall
/// (<see cref="Half"/> metres from the centre to each wall), so the pieces
/// keep the spacing the solver and the timing checks were tuned for while
/// the room around them reads as a real interior. A short corridor joins a
/// room's exit doorway to the next room's entrance.
/// </summary>
public sealed class RoomPlan
{
    public const int N = 9;
    public const float Cell = 2f;
    /// <summary>Centre to each inner wall face: the hall is 30 m across, the
    /// puzzle grid (18 m) sits in its middle.</summary>
    public const float Half = 15f;
    /// <summary>Wall thickness.</summary>
    public const float WallThick = 1f;
    /// <summary>Corridor length between one room's outer wall face and the next one's.</summary>
    public const float Corridor = 8f;
    /// <summary>Centre to centre between neighbouring rooms: the hall, two walls and a corridor.</summary>
    public const float Pitch = 2f * (Half + WallThick) + Corridor;
    /// <summary>Width of a doorway and of the corridor behind it.</summary>
    public const float CorridorWidth = 4.4f;
    public const int Mid = N / 2;

    public static readonly int[] Dx = { 1, 0, -1, 0 };
    public static readonly int[] Dz = { 0, 1, 0, -1 };

    public RoomKind Kind;
    /// <summary>What a puzzle room holds (null for the start room and the vault).</summary>
    public PuzzleKind? Def;
    /// <summary>The kind's own layout (its class, set by <see cref="PuzzleKind.Generate"/>).</summary>
    public object? Data;
    public int Index;
    public int MacroX, MacroZ;
    /// <summary>Side the team comes in through, or -1 (the start room).</summary>
    public int EntrySide = -1;
    /// <summary>Side the next room is on, or -1 (the reward room).</summary>
    public int ExitSide = -1;
    /// <summary>First index of this room's values in <see cref="PuzzleState"/>.</summary>
    public int Slot;

    public readonly bool[] Pillar = new bool[N * N];
    /// <summary>Cells a fixed piece stands in (an emitter, a reset bell, a
    /// spout): solid to walking and to crates, like a pillar, but the kind
    /// draws them.</summary>
    public readonly bool[] Fixed = new bool[N * N];
    /// <summary>Pieces that stand off the grid's cell centres (levers, bells,
    /// braziers), in room-local metres: the bot walks round them.</summary>
    public readonly List<Vector2> Spots = new();

    /// <summary>The kind's layout as its own type.</summary>
    public T Layout<T>() where T : class => (T)Data!;

    /// <summary>Kind id for logs ("start", "crates", "reward").</summary>
    public string KindId => Def?.Id ?? Kind.ToString().ToLowerInvariant();

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

    /// <summary>Inside this room's hall (with a margin in from the walls).</summary>
    public bool Contains(Vector3 world, float margin = 0f)
    {
        Vector3 d = world - Centre;
        float half = Half - margin;
        return Mathf.Abs(d.X) <= half && Mathf.Abs(d.Z) <= half;
    }

    /// <summary>On the puzzle grid (the inlaid floor in the hall's middle).</summary>
    public bool OnGrid(Vector3 world, float margin = 0f)
    {
        Vector3 d = world - Centre;
        float half = N * Cell * 0.5f - margin;
        return Mathf.Abs(d.X) <= half && Mathf.Abs(d.Z) <= half;
    }

    /// <summary>The middle of the doorway on <paramref name="side"/>, in the wall.</summary>
    public Vector3 Doorway(int side) => Centre + new Vector3(Dx[side], 0f, Dz[side]) * (Half + WallThick * 0.5f);

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

    /// <summary>The cell a pushed piece may never enter: in front of either door.</summary>
    public bool CrateForbidden(int cell) => cell == EntryCell || cell == ExitCell;

    /// <summary>A piece stands near this local point (the bot walks round it).</summary>
    public bool NearSpot(Vector2 local, float r)
    {
        foreach (Vector2 p in Spots) if (p.DistanceTo(local) < r) return true;
        return false;
    }
}

/// <summary>A whole puzzle map: the rooms in order, from the start room
/// through the puzzle rooms to the reward vault.</summary>
public sealed class PuzzlePlan
{
    public ulong Seed;
    public int PuzzleIndex;
    public int Difficulty;
    public int Players;
    /// <summary>Puzzle Gauntlet: no guards and no prize pedestals.</summary>
    public bool Gauntlet;
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

    /// <summary>The room under a world position, or -1 (in a corridor or outside).</summary>
    public int RoomAt(Vector3 world)
    {
        for (int i = 0; i < Rooms.Count; i++)
            if (Rooms[i].Contains(world, -0.6f)) return i;
        return -1;
    }

    /// <summary>The middle of the doorway out of room <paramref name="i"/> into
    /// the corridor to the next (where its door stands).</summary>
    public Vector3 DoorBetween(int i) => Rooms[i].Doorway(Rooms[i].ExitSide);

    /// <summary>The corridor a world position stands in (corridor i runs from
    /// room i to room i + 1), or -1.</summary>
    public int CorridorAt(Vector3 world, float margin = 0.4f)
    {
        for (int i = 0; i + 1 < Rooms.Count; i++)
        {
            RoomPlan a = Rooms[i];
            int s = a.ExitSide;
            Vector3 d = world - a.Centre;
            float along = d.X * RoomPlan.Dx[s] + d.Z * RoomPlan.Dz[s];
            float across = Mathf.Abs(d.X * RoomPlan.Dz[s] - d.Z * RoomPlan.Dx[s]);
            if (along >= RoomPlan.Half - margin && along <= RoomPlan.Pitch - RoomPlan.Half + margin && across <= RoomPlan.CorridorWidth * 0.5f + margin) return i;
        }
        return -1;
    }

    /// <summary>In a room or a corridor: somewhere a player can stand.</summary>
    public bool Walkable(Vector3 world) => RoomAt(world) >= 0 || CorridorAt(world) >= 0;

    /// <summary>The room a position belongs to for drawing: its room, or for a
    /// corridor the room it leads out of (or into, past halfway).</summary>
    public int NearestRoom(Vector3 world)
    {
        int r = RoomAt(world);
        if (r >= 0) return r;
        int best = 0;
        float bestD = float.MaxValue;
        for (int i = 0; i < Rooms.Count; i++)
        {
            float d = new Vector2(world.X - Rooms[i].Centre.X, world.Z - Rooms[i].Centre.Z).LengthSquared();
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }

    public string Describe()
    {
        var parts = new List<string>();
        foreach (RoomPlan r in Rooms) parts.Add(r.KindId);
        return string.Join(",", parts);
    }
}
