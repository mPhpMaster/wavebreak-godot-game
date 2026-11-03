using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Wavebreak.Audio;
using Wavebreak.Core;

namespace Wavebreak.Game.Puzzle.Kinds;

/// <summary>
/// Aqueduct Room: a square of pipe tiles set in the floor between a spout and
/// a basin. Stand on a tile and use it to turn it a quarter; water runs from
/// the spout through every pipe that lines up, and the room is solved when it
/// reaches the basin. Straight pipes, elbows, and from difficulty 3 tees.
///
/// Generation lays a random winding channel from spout to basin and fits each
/// of its tiles with the piece that carries it (so a solution always exists),
/// fills the rest with spare pieces, and turns everything at random until the
/// water no longer gets through.
///
/// State: tile turns, two bits each: tiles 0-14 in RA, 15-24 in RB.
/// Solver: the laid channel (a tile is right when its openings match), and a
/// flood from the spout for "solved".
/// </summary>
public sealed class PipesKind : PuzzleKind
{
    public sealed class Data
    {
        public int Size;
        public int X0, Z0;
        /// <summary>Per tile: 0 straight, 1 elbow, 2 tee.</summary>
        public int[] Shape = Array.Empty<int>();
        public int[] StartTurn = Array.Empty<int>();
        /// <summary>Per tile: the openings it needs on the laid channel, or 0 off it.</summary>
        public int[] Need = Array.Empty<int>();
        public int SourceTile, SourceSide, DrainTile, DrainSide;
        public int Spout = -1, Basin = -1;
    }

    private sealed class View
    {
        public readonly List<Node3D> Pipes = new();
        public readonly List<List<MeshInstance3D>> Water = new();
        public readonly List<float> Shown = new();
        public StandardMaterial3D BasinWater = null!;
        public Node3D BasinLevel = null!;
        public int Flowing = -1;
    }

    private const string Verb = "Turn the pipe";
    private const string FilledLine = "Pipes carrying water: {0}";

    public override string Id => "pipes";
    public override string Title => "Aqueduct Room";
    public override string Objective => "Stand on a pipe tile and turn it until the water runs from the spout to the basin.";
    public override string Hint => "Hint: turn the marked tile as many times as its number says.";
    public override IEnumerable<string> Strings => new[] { Verb, FilledLine };
    public override HallStyle Style => HallStyle.Aqueduct;
    public override float ParSeconds => 30f;

    private static readonly Color Pipe = new(0.78f, 0.52f, 0.30f);
    private static readonly Color WaterColour = new(0.35f, 0.80f, 1f);
    private static readonly int[] BaseMask = { 0b0101, 0b0011, 0b0111 };

    // ------------------------------------------------------------- data

    /// <summary>Openings of a shape after <paramref name="turn"/> quarter turns (side s becomes s + turn).</summary>
    public static int Mask(int shape, int turn)
    {
        int b = BaseMask[shape], m = 0;
        for (int s = 0; s < 4; s++) if ((b >> s & 1) != 0) m |= 1 << ((s + turn) & 3);
        return m;
    }

    private static int Cell(Data l, int tile) => RoomPlan.CellOf(l.X0 + tile % l.Size, l.Z0 + tile / l.Size);

    /// <summary>The tile beside <paramref name="tile"/> on <paramref name="side"/>, or -1 off the square.</summary>
    private static int Next(Data l, int tile, int side)
    {
        int x = tile % l.Size + RoomPlan.Dx[side], z = tile / l.Size + RoomPlan.Dz[side];
        return x < 0 || z < 0 || x >= l.Size || z >= l.Size ? -1 : z * l.Size + x;
    }

    public override void Generate(RoomPlan room, Rng rng, int d)
    {
        var l = new Data();
        room.Data = l;
        l.Size = d < 2 ? 4 : 5;
        l.X0 = l.Z0 = l.Size == 4 ? 3 : 2;
        int tiles = l.Size * l.Size;
        int minLen = l.Size + 1 + Math.Min(d, 3);
        for (int attempt = 0; attempt < 400; attempt++)
        {
            if (attempt == 300) minLen = l.Size;
            PuzzleGen.Clear(room);
            // Spout on one side of the square, basin on the opposite side.
            int inSide = rng.RangeInt(0, 4);
            int outSide = (inSide + 2) % 4;
            int a = rng.RangeInt(0, l.Size), b = rng.RangeInt(0, l.Size);
            l.SourceTile = EdgeTile(l, inSide, a);
            l.DrainTile = EdgeTile(l, outSide, b);
            l.SourceSide = inSide;
            l.DrainSide = outSide;
            l.Spout = PuzzleSolver.Step(Cell(l, l.SourceTile), inSide);
            l.Basin = PuzzleSolver.Step(Cell(l, l.DrainTile), outSide);
            if (l.Spout < 0 || l.Basin < 0 || room.NearDoor(l.Spout) || room.NearDoor(l.Basin)) continue;
            List<int>? path = Channel(l, rng, minLen);
            if (path is null) continue;

            l.Shape = new int[tiles];
            l.Need = new int[tiles];
            for (int i = 0; i < path.Count; i++)
            {
                int t = path[i];
                int from = i == 0 ? inSide : SideBetween(l, t, path[i - 1]);
                int to = i == path.Count - 1 ? outSide : SideBetween(l, t, path[i + 1]);
                l.Need[t] = (1 << from) | (1 << to);
                l.Shape[t] = from == (to + 2) % 4 ? 0 : 1;
            }
            for (int t = 0; t < tiles; t++)
                if (l.Need[t] == 0) l.Shape[t] = d >= 3 && rng.Chance(0.25f) ? 2 : rng.RangeInt(0, 2);
            l.StartTurn = new int[tiles];
            for (int t = 0; t < tiles; t++) l.StartTurn[t] = rng.RangeInt(0, 4);
            // Enough of the channel turned wrong that it is a puzzle, and the water must not get through yet.
            int wrong = 0;
            foreach (int t in path) if (Mask(l.Shape[t], l.StartTurn[t]) != l.Need[t]) wrong++;
            if (wrong < Math.Min(path.Count, 3 + d)) continue;
            if (Flood(l, l.StartTurn, out _)) continue;
            room.Fixed[l.Spout] = room.Fixed[l.Basin] = true;
            if (!PuzzleSolver.DoorsConnected(room)) continue;
            return;
        }
        // Fallback: a straight run across the middle row of a 4 x 4 square.
        PuzzleGen.Clear(room);
        l.Size = 4;
        l.X0 = l.Z0 = 3;
        tiles = 16;
        l.SourceSide = 2; l.DrainSide = 0;
        l.SourceTile = 1 * 4; l.DrainTile = 1 * 4 + 3;
        l.Spout = RoomPlan.CellOf(2, 4);
        l.Basin = RoomPlan.CellOf(7, 4);
        if (room.NearDoor(l.Spout) || room.NearDoor(l.Basin)) { l.SourceTile = 2 * 4; l.DrainTile = 2 * 4 + 3; l.Spout = RoomPlan.CellOf(2, 5); l.Basin = RoomPlan.CellOf(7, 5); }
        l.Shape = new int[tiles];
        l.Need = new int[tiles];
        l.StartTurn = new int[tiles];
        for (int x = 0; x < 4; x++) l.Need[l.SourceTile + x] = 0b0101;
        for (int t = 0; t < tiles; t++) l.StartTurn[t] = l.Need[t] != 0 ? 1 : t % 4;
        room.Fixed[l.Spout] = room.Fixed[l.Basin] = true;
    }

    private static int EdgeTile(Data l, int side, int along) => side switch
    {
        0 => along * l.Size + (l.Size - 1),
        1 => (l.Size - 1) * l.Size + along,
        2 => along * l.Size,
        _ => along,
    };

    private static int SideBetween(Data l, int from, int to)
    {
        for (int s = 0; s < 4; s++) if (Next(l, from, s) == to) return s;
        return -1;
    }

    /// <summary>A random self-avoiding walk from the source tile to the drain
    /// tile, at least <paramref name="minLen"/> tiles long.</summary>
    private static List<int>? Channel(Data l, Rng rng, int minLen)
    {
        for (int tries = 0; tries < 60; tries++)
        {
            var path = new List<int> { l.SourceTile };
            var used = new HashSet<int> { l.SourceTile };
            int budget = 400;
            bool Walk()
            {
                if (--budget < 0) return false;
                int cur = path[^1];
                if (cur == l.DrainTile) return path.Count >= minLen;
                var sides = new List<int> { 0, 1, 2, 3 };
                PuzzleGen.Shuffle(rng, sides);
                foreach (int s in sides)
                {
                    int n = Next(l, cur, s);
                    if (n < 0 || used.Contains(n)) continue;
                    path.Add(n);
                    used.Add(n);
                    if (Walk()) return true;
                    path.RemoveAt(path.Count - 1);
                    used.Remove(n);
                }
                return false;
            }
            if (Walk()) return path;
        }
        return null;
    }

    /// <summary>Does water from the spout reach the basin with these turns?
    /// <paramref name="wet"/> gets the tiles it runs through.</summary>
    public static bool Flood(Data l, int[] turns, out bool[] wet)
    {
        wet = new bool[l.Size * l.Size];
        if ((Mask(l.Shape[l.SourceTile], turns[l.SourceTile]) >> l.SourceSide & 1) == 0) return false;
        var queue = new Queue<int>();
        queue.Enqueue(l.SourceTile);
        wet[l.SourceTile] = true;
        bool done = false;
        while (queue.Count > 0)
        {
            int t = queue.Dequeue();
            int m = Mask(l.Shape[t], turns[t]);
            if (t == l.DrainTile && (m >> l.DrainSide & 1) != 0) done = true;
            for (int s = 0; s < 4; s++)
            {
                if ((m >> s & 1) == 0) continue;
                int n = Next(l, t, s);
                if (n < 0 || wet[n] || (Mask(l.Shape[n], turns[n]) >> ((s + 2) & 3) & 1) == 0) continue;
                wet[n] = true;
                queue.Enqueue(n);
            }
        }
        return done;
    }

    public static int[] Turns(PuzzleState st, RoomPlan room)
    {
        Data l = room.Layout<Data>();
        var t = new int[l.Size * l.Size];
        for (int i = 0; i < t.Length; i++)
        {
            int word = st.Get(room, i < 15 ? PuzzleState.RA : PuzzleState.RB);
            t[i] = word >> (2 * (i % 15)) & 3;
        }
        return t;
    }

    private static void SetTurn(PuzzleState st, RoomPlan room, int i, int turn)
    {
        int slot = i < 15 ? PuzzleState.RA : PuzzleState.RB;
        int shift = 2 * (i % 15);
        int word = st.Get(room, slot) & ~(3 << shift) | (turn & 3) << shift;
        st.SetR(room, slot, word);
    }

    public override void InitState(PuzzleState st, RoomPlan room)
    {
        Data l = room.Layout<Data>();
        for (int i = 0; i < l.StartTurn.Length; i++)
        {
            int slot = room.Slot + (i < 15 ? PuzzleState.RA : PuzzleState.RB);
            st.V[slot] |= (l.StartTurn[i] & 3) << (2 * (i % 15));
        }
    }

    public override bool Act(PuzzleState st, RoomPlan room, int r, PuzzleAction action, int a, int b, float now)
    {
        if (action != PuzzleAction.Use) return false;
        Data l = room.Layout<Data>();
        if (a < 0 || a >= l.Shape.Length) return true;
        int[] turns = Turns(st, room);
        SetTurn(st, room, a, turns[a] + 1);
        turns[a] = (turns[a] + 1) & 3;
        if (Flood(l, turns, out _)) st.MarkSolved(r, now);
        return true;
    }

    /// <summary>Quarter turns tile <paramref name="i"/> still needs for the laid channel (0 when right or off it).</summary>
    public static int TurnsNeeded(Data l, int i, int turn)
    {
        if (l.Need[i] == 0) return 0;
        for (int k = 0; k < 4; k++) if (Mask(l.Shape[i], turn + k) == l.Need[i]) return k;
        return 0;
    }

    public override async Task<string?> Solve(IPuzzleDriver d, RoomPlan room, int r)
    {
        Data l = room.Layout<Data>();
        int[] turns = Turns(d.State, room);
        if (Flood(l, turns, out _)) return "water already through";
        for (int i = 0; i < turns.Length; i++)
        {
            int k = TurnsNeeded(l, i, turns[i]);
            if (k == 0) continue;
            await d.Stand(room.World(Cell(l, i)));
            for (int n = 0; n < k; n++) await d.Ask(PuzzleAction.Use, i);
        }
        return null;
    }

    // ------------------------------------------------------------ scene

    /// <summary>Y rotation that turns side 0 (+X) onto side <paramref name="turn"/>.</summary>
    private static float Angle(int turn) => -turn * Mathf.Pi * 0.5f;

    public override object Build(PieceKit kit, RoomPlan room)
    {
        Data l = room.Layout<Data>();
        var v = new View();
        var slab = PieceKit.Flat(new Color(0.24f, 0.28f, 0.27f));
        var pipe = PieceKit.Flat(Pipe);
        for (int i = 0; i < l.Shape.Length; i++)
        {
            Vector3 at = room.World(Cell(l, i));
            kit.Put(PieceKit.Mesh(new BoxMesh { Size = new Vector3(1.86f, 0.1f, 1.86f) }, at + Vector3.Up * 0.05f, slab, false));
            var node = new Node3D { Position = at + Vector3.Up * 0.1f, Rotation = new Vector3(0f, Angle(l.StartTurn[i]), 0f) };
            kit.Put(node);
            node.AddChild(PieceKit.Mesh(new CylinderMesh { TopRadius = 0.3f, BottomRadius = 0.3f, Height = 0.36f, RadialSegments = 10 }, Vector3.Up * 0.18f, pipe));
            var water = new List<MeshInstance3D>();
            for (int s = 0; s < 4; s++)
            {
                if ((BaseMask[l.Shape[i]] >> s & 1) == 0) continue;
                var dir = new Vector3(RoomPlan.Dx[s], 0f, RoomPlan.Dz[s]);
                var arm = PieceKit.Mesh(new BoxMesh { Size = new Vector3(s % 2 == 0 ? 0.95f : 0.42f, 0.34f, s % 2 == 0 ? 0.42f : 0.95f) }, dir * 0.47f + Vector3.Up * 0.17f, pipe);
                node.AddChild(arm);
                // The water channel on top of the arm: shown only when water runs in it.
                var w = PieceKit.Mesh(new BoxMesh { Size = new Vector3(s % 2 == 0 ? 0.95f : 0.2f, 0.06f, s % 2 == 0 ? 0.2f : 0.95f) }, dir * 0.47f + Vector3.Up * 0.37f, PieceKit.Glow(WaterColour, 1.6f), false);
                w.Visible = false;
                node.AddChild(w);
                water.Add(w);
            }
            v.Pipes.Add(node);
            v.Water.Add(water);
            v.Shown.Add(Angle(l.StartTurn[i]));
        }
        // The spout: a stone head with water falling into the first tile.
        Vector3 sp = room.World(l.Spout);
        var into = new Vector3(-RoomPlan.Dx[l.SourceSide], 0f, -RoomPlan.Dz[l.SourceSide]);
        kit.Put(PieceKit.Mesh(new BoxMesh { Size = new Vector3(1.3f, 1.8f, 1.3f) }, sp + Vector3.Up * 0.9f, PieceKit.Flat(PieceKit.WallCap)));
        var lip = PieceKit.Mesh(new CylinderMesh { TopRadius = 0.22f, BottomRadius = 0.3f, Height = 0.6f, RadialSegments = 10 }, sp + Vector3.Up * 1.3f + into * 0.75f, PieceKit.Flat(PieceKit.Bronze));
        lip.Rotation = l.SourceSide % 2 == 0 ? new Vector3(0f, 0f, Mathf.Pi * 0.5f) : new Vector3(Mathf.Pi * 0.5f, 0f, 0f);
        kit.Put(lip);
        kit.Put(PieceKit.Mesh(new CylinderMesh { TopRadius = 0.12f, BottomRadius = 0.18f, Height = 1.1f, RadialSegments = 8 }, sp + Vector3.Up * 0.75f + into * 1.05f, PieceKit.Glow(WaterColour, 1.4f), false));
        kit.Solid(new BoxShape3D { Size = new Vector3(1.3f, 1.8f, 1.3f) }, sp + Vector3.Up * 0.9f);
        // The basin: a round bowl whose water rises when the channel is through.
        Vector3 bs = room.World(l.Basin);
        kit.Put(PieceKit.Mesh(new CylinderMesh { TopRadius = 0.85f, BottomRadius = 0.7f, Height = 0.8f, RadialSegments = 18 }, bs + Vector3.Up * 0.4f, PieceKit.Flat(PieceKit.WallCap)));
        v.BasinWater = PieceKit.Flat(new Color(0.2f, 0.28f, 0.32f));
        v.BasinLevel = new Node3D { Position = bs + Vector3.Up * 0.3f };
        kit.Put(v.BasinLevel);
        v.BasinLevel.AddChild(PieceKit.Mesh(new CylinderMesh { TopRadius = 0.72f, BottomRadius = 0.72f, Height = 0.06f, RadialSegments = 18 }, Vector3.Zero, v.BasinWater, false));
        kit.Post(bs, 0.85f, 0.8f);
        return v;
    }

    public override void Sync(PieceKit kit, object view, RoomPlan room, PuzzleState st, float t, float dt, bool changed)
    {
        var v = (View)view;
        Data l = room.Layout<Data>();
        int[] turns = Turns(st, room);
        if (changed || v.Flowing < 0)
        {
            bool through = Flood(l, turns, out bool[] wet);
            int n = 0;
            for (int i = 0; i < turns.Length; i++)
            {
                if (wet[i]) n++;
                foreach (MeshInstance3D w in v.Water[i]) w.Visible = wet[i];
            }
            v.Flowing = n;
            PieceKit.Lit(v.BasinWater, through || st.Solved(room), WaterColour, new Color(0.2f, 0.28f, 0.32f), 1.6f);
        }
        for (int i = 0; i < turns.Length; i++)
        {
            float target = Angle(turns[i]);
            v.Shown[i] = Mathf.LerpAngle(v.Shown[i], target, Mathf.Min(1f, dt * 12f));
            v.Pipes[i].Rotation = new Vector3(0f, v.Shown[i], 0f);
        }
        bool full = st.Solved(room);
        Vector3 home = room.World(l.Basin) + Vector3.Up * (full ? 0.76f : 0.3f);
        v.BasinLevel.Position = v.BasinLevel.Position.MoveToward(home, dt * 0.8f);
    }

    public override void Sounds(object view, RoomPlan room, Func<int, int> was, Func<int, int> now)
    {
        if (was(PuzzleState.RA) != now(PuzzleState.RA) || was(PuzzleState.RB) != now(PuzzleState.RB)) AudioDirector.PlayPuzzle(PuzzleSound.Click, 1.05f);
    }

    public override void Offer(PuzzleMap map, RoomPlan room, FocusOffer offer)
    {
        Data l = room.Layout<Data>();
        for (int i = 0; i < l.Shape.Length; i++) offer(PuzzleAction.Use, i, room.World(Cell(l, i)), Verb);
    }

    /// <summary>The nearest channel tile still turned wrong, or -1.</summary>
    private static int NextTile(PuzzleState st, RoomPlan room, Vector3? me)
    {
        Data l = room.Layout<Data>();
        int[] turns = Turns(st, room);
        int best = -1;
        float bestD = float.MaxValue;
        for (int i = 0; i < turns.Length; i++)
        {
            if (TurnsNeeded(l, i, turns[i]) == 0) continue;
            float dd = me is { } at ? room.World(Cell(l, i)).DistanceSquaredTo(at) : i;
            if (dd < bestD) { bestD = dd; best = i; }
        }
        return best;
    }

    public override PuzzleHint? HintAt(PuzzleState st, RoomPlan room, Vector3? me, float now)
    {
        Data l = room.Layout<Data>();
        int i = NextTile(st, room, me);
        if (i < 0) return null;
        int k = TurnsNeeded(l, i, Turns(st, room)[i]);
        return new PuzzleHint(room.World(Cell(l, i)), k.ToString());
    }

    public override string Status(PuzzleMap map, RoomPlan room)
    {
        Flood(room.Layout<Data>(), Turns(map.State, room), out bool[] wet);
        int n = 0;
        foreach (bool w in wet) if (w) n++;
        return Meta.Loc.F(FilledLine, n);
    }

    public override Vector3 Bot(PuzzleBot bot, RoomPlan room, Vector3 me)
    {
        Data l = room.Layout<Data>();
        int i = NextTile(bot.State, room, me);
        return i < 0 ? Vector3.Zero : bot.Use(room, me, room.World(Cell(l, i)), PuzzleAction.Use, i, onTop: true);
    }

    public override void Glyph(CanvasItem ci, Vector2 at, float size, Color c)
    {
        float h = size * 0.65f, w = Mathf.Max(1f, size * 0.2f);
        ci.DrawLine(at + new Vector2(-h, 0f), at, c, w);
        ci.DrawLine(at, at + new Vector2(0f, h), c, w);
        ci.DrawLine(at + new Vector2(0f, -h), at + new Vector2(h, -h), c, w * 0.7f);
    }
}
