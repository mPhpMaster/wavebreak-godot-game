using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Wavebreak.Audio;
using Wavebreak.Core;

namespace Wavebreak.Game.Puzzle.Kinds;

/// <summary>
/// Aqueduct Room: a square of pipe tiles set in the floor between spouts and
/// basins. Stand on a tile and use it to turn it a quarter. Water runs from
/// every spout through every pipe that lines up; the room is solved when it
/// fills every basin WITHOUT A LEAK: no wet pipe may open onto a tile that
/// does not open back, or off the square anywhere but at a spout or basin.
/// The leak rule is what makes it a puzzle: a pipe that carries water must
/// fit its neighbours on every side, so spare tees and elbows next to the
/// channel can only be passed by, never used carelessly.
///
/// Bolted tiles (an iron frame and bolts) never turn. A chained tile (a
/// bronze bar with an arrowhead to its partner) also turns the tile its
/// chain points to, so the order matters: set the chained tile first, its
/// partner after. Difficulty: a 5 x 5 square with one basin (d0) to 7 x 7
/// with two spouts and three basins (d5), bolts from d1, chains from d2, more
/// tees among the spares, and more of the channel turned wrong at the start.
///
/// Generation lays a random tree of channels joining every spout and basin
/// (a winding walk between the first two, then each other joins it) and fits
/// each of its tiles with the piece that carries it, so a solution always
/// exists; it fills the rest with spare pieces and turns everything at
/// random until the water no longer gets through.
///
/// State: tile turns, two bits each, fifteen tiles per slot in RA, RB, RC, RD.
/// Solver: the laid tree (a tile is right when its openings match), chained
/// tiles first; a flood from the spouts for "solved".
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
        /// <summary>Per tile: the openings it needs on the laid tree, or 0 off it.</summary>
        public int[] Need = Array.Empty<int>();
        public bool[] Locked = Array.Empty<bool>();
        /// <summary>Per tile: the tile turning it also turns, or -1.</summary>
        public int[] LinkTo = Array.Empty<int>();
        public int[] SpoutTile = Array.Empty<int>(), SpoutSide = Array.Empty<int>(), Spout = Array.Empty<int>();
        public int[] BasinTile = Array.Empty<int>(), BasinSide = Array.Empty<int>(), Basin = Array.Empty<int>();
    }

    private sealed class View
    {
        public readonly List<Node3D> Pipes = new();
        /// <summary>Per tile, per arm: the arm's side unturned, its water and its spray.</summary>
        public readonly List<List<(int Side, MeshInstance3D Water, Node3D Spray)>> Arms = new();
        public readonly List<float> Shown = new();
        public readonly List<StandardMaterial3D> BasinWater = new();
        public readonly List<Node3D> BasinLevel = new();
        public int Filled;
        public bool Drawn;
    }

    private const string Verb = "Turn the pipe";
    private const string Line = "Basins filled: {0}/{1}  ·  leaks: {2}";

    public override string Id => "pipes";
    public override string Title => "Aqueduct Room";
    public override string Objective => "Turn the pipe tiles until water from the spouts fills every basin without a single leak. Bolted tiles never turn; a chained tile also turns the tile its chain points to.";
    public override string Hint => "Hint: turn the marked tile as many times as its number says.";
    public override IEnumerable<string> Strings => new[] { Verb, Line };
    public override HallStyle Style => HallStyle.Aqueduct;
    public override float ParSeconds => 55f;

    private static readonly Color Pipe = new(0.78f, 0.52f, 0.30f);
    private static readonly Color PipeLocked = new(0.42f, 0.40f, 0.38f);
    private static readonly Color WaterColour = new(0.35f, 0.80f, 1f);
    private static readonly int[] BaseMask = { 0b0101, 0b0011, 0b0111 };

    private static readonly int[] Sizes = { 5, 5, 6, 6, 7, 7 };
    private static readonly int[] Spouts = { 1, 1, 1, 1, 2, 2 };
    private static readonly int[] Basins = { 1, 2, 2, 3, 2, 3 };
    private static readonly int[] Bolts = { 0, 2, 3, 4, 5, 6 };
    private static readonly int[] Chains = { 0, 0, 1, 2, 3, 4 };
    private static readonly int[] MinWrong = { 5, 7, 9, 11, 13, 15 };
    private static readonly float[] TeeChance = { 0.15f, 0.25f, 0.3f, 0.35f, 0.4f, 0.45f };
    private static readonly int[] Slots = { PuzzleState.RA, PuzzleState.RB, PuzzleState.RC, PuzzleState.RD };

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

    public override void Generate(RoomPlan room, Rng rng, int d)
    {
        d = Math.Clamp(d, 0, 5);
        var l = new Data();
        room.Data = l;
        for (int attempt = 0; attempt < 500; attempt++)
        {
            int dd = attempt < 400 ? d : Math.Max(0, d - 2);
            PuzzleGen.Clear(room);
            if (TryBuild(room, l, rng, dd, attempt < 300 ? MinWrong[dd] : MinWrong[dd] - 3)) return;
        }
        Fallback(room, l);
    }

    private static bool TryBuild(RoomPlan room, Data l, Rng rng, int d, int minWrong)
    {
        l.Size = Sizes[d];
        l.X0 = l.Z0 = l.Size == 5 ? 2 : 1;
        int tiles = l.Size * l.Size;
        int spouts = Spouts[d], basins = Basins[d], terms = spouts + basins;

        // Terminals round the square: each on its own tile and its own floor cell.
        var tTile = new List<int>();
        var tSide = new List<int>();
        var tCell = new List<int>();
        for (int tries = 0; tries < 60 && tTile.Count < terms; tries++)
        {
            int side = rng.RangeInt(0, 4), along = rng.RangeInt(0, l.Size);
            int tile = EdgeTile(l, side, along);
            int cell = PuzzleSolver.Step(Cell(l, tile), side);
            if (cell < 0 || room.NearDoor(cell) || tTile.Contains(tile) || tCell.Contains(cell)) continue;
            // Spouts and basins apart, so the channels have room to wind.
            bool close = false;
            foreach (int c in tCell) close |= Mathf.Abs(RoomPlan.X(c) - RoomPlan.X(cell)) + Mathf.Abs(RoomPlan.Z(c) - RoomPlan.Z(cell)) < 3;
            if (close) continue;
            tTile.Add(tile);
            tSide.Add(side);
            tCell.Add(cell);
        }
        if (tTile.Count < terms) return false;

        // The tree: a winding walk between the first two, then each other walks in to join it.
        List<int>? first = Walk(l, rng, tTile[0], t => t == tTile[1], new HashSet<int>(), l.Size + 2);
        if (first is null) return false;
        var tree = new HashSet<int>(first);
        var need = new int[tiles];
        void Join(List<int> path)
        {
            for (int i = 0; i + 1 < path.Count; i++)
            {
                int s = SideBetween(l, path[i], path[i + 1]);
                need[path[i]] |= 1 << s;
                need[path[i + 1]] |= 1 << ((s + 2) & 3);
            }
        }
        Join(first);
        for (int k = 2; k < terms; k++)
        {
            if (tree.Contains(tTile[k])) return false;
            List<int>? branch = Walk(l, rng, tTile[k], t => tree.Contains(t), tree, 3);
            if (branch is null) return false;
            Join(branch);
            foreach (int t in branch) tree.Add(t);
        }
        for (int k = 0; k < terms; k++) need[tTile[k]] |= 1 << tSide[k];
        foreach (int t in tree)
        {
            int n = PuzzleSolver.PopCount(need[t]);
            if (n < 2 || n > 3) return false;
        }

        l.Need = need;
        l.Shape = new int[tiles];
        for (int t = 0; t < tiles; t++)
        {
            if (need[t] == 0) { l.Shape[t] = rng.Chance(TeeChance[d]) ? 2 : rng.RangeInt(0, 2); continue; }
            int n = PuzzleSolver.PopCount(need[t]);
            l.Shape[t] = n == 3 ? 2 : (need[t] == 0b0101 || need[t] == 0b1010) ? 0 : 1;
        }
        l.SpoutTile = tTile.Take(spouts).ToArray();
        l.SpoutSide = tSide.Take(spouts).ToArray();
        l.Spout = tCell.Take(spouts).ToArray();
        l.BasinTile = tTile.Skip(spouts).ToArray();
        l.BasinSide = tSide.Skip(spouts).ToArray();
        l.Basin = tCell.Skip(spouts).ToArray();

        // Start turns at random; bolts: half on the tree (already right), half on spares beside it.
        l.StartTurn = new int[tiles];
        for (int t = 0; t < tiles; t++) l.StartTurn[t] = rng.RangeInt(0, 4);
        l.Locked = new bool[tiles];
        var treeList = tree.ToList();
        var beside = new List<int>();
        for (int t = 0; t < tiles; t++)
        {
            if (tree.Contains(t)) continue;
            for (int s = 0; s < 4; s++) if (Next(l, t, s) is int n && n >= 0 && tree.Contains(n)) { beside.Add(t); break; }
        }
        int bolts = Bolts[d];
        for (int i = 0; i < bolts; i++)
        {
            List<int> from = i % 2 == 0 ? treeList : beside;
            if (from.Count == 0) continue;
            int t = PuzzleGen.Take(rng, from);
            l.Locked[t] = true;
            if (need[t] != 0) l.StartTurn[t] = SolutionTurn(l, t);
        }
        // Chains: a driver and the partner it turns, near each other, never bolted, never both roles.
        l.LinkTo = new int[tiles];
        Array.Fill(l.LinkTo, -1);
        var role = new bool[tiles];
        int chains = 0;
        for (int tries = 0; tries < 200 && chains < Chains[d]; tries++)
        {
            int a = rng.RangeInt(0, tiles), b = rng.RangeInt(0, tiles);
            if (a == b || role[a] || role[b] || l.Locked[a] || l.Locked[b]) continue;
            int dx = Math.Abs(a % l.Size - b % l.Size), dz = Math.Abs(a / l.Size - b / l.Size);
            if (dx + dz > 2 || dx + dz == 0) continue;
            // At least one end on the tree, so the chain matters.
            if (need[a] == 0 && need[b] == 0) continue;
            l.LinkTo[a] = b;
            role[a] = role[b] = true;
            chains++;
        }

        // Enough of the tree turned wrong that it is a puzzle, and the water must not get through yet.
        int wrong = 0;
        foreach (int t in tree) if (!l.Locked[t] && TurnsNeeded(l, t, l.StartTurn[t]) != 0) wrong++;
        if (wrong < Math.Min(tree.Count - 1, minWrong)) return false;
        if (Solved(l, l.StartTurn)) return false;
        foreach (int c in tCell) room.Fixed[c] = true;
        return PuzzleSolver.DoorsConnected(room);
    }

    /// <summary>A random self-avoiding walk from <paramref name="start"/> until
    /// <paramref name="done"/> (at least <paramref name="minLen"/> tiles),
    /// never through <paramref name="avoid"/> except its last step.</summary>
    private static List<int>? Walk(Data l, Rng rng, int start, Func<int, bool> done, HashSet<int> avoid, int minLen)
    {
        for (int tries = 0; tries < 40; tries++)
        {
            var path = new List<int> { start };
            var used = new HashSet<int> { start };
            int budget = 500;
            bool Go()
            {
                if (--budget < 0) return false;
                int cur = path[^1];
                if (path.Count > 1 && done(cur)) return path.Count >= minLen;
                var sides = new List<int> { 0, 1, 2, 3 };
                PuzzleGen.Shuffle(rng, sides);
                foreach (int s in sides)
                {
                    int n = Next(l, cur, s);
                    if (n < 0 || used.Contains(n)) continue;
                    if (avoid.Contains(n) && !done(n)) continue;
                    path.Add(n);
                    used.Add(n);
                    if (Go()) return true;
                    path.RemoveAt(path.Count - 1);
                    used.Remove(n);
                }
                return false;
            }
            if (Go()) return path;
        }
        return null;
    }

    /// <summary>A straight run across the middle of a 5 x 5 square.</summary>
    private static void Fallback(RoomPlan room, Data l)
    {
        PuzzleGen.Clear(room);
        l.Size = 5;
        l.X0 = l.Z0 = 2;
        int tiles = 25;
        int row = room.NearDoor(RoomPlan.CellOf(1, 4)) || room.NearDoor(RoomPlan.CellOf(7, 4)) ? 1 : 2;
        l.SpoutTile = new[] { row * 5 };
        l.SpoutSide = new[] { 2 };
        l.Spout = new[] { RoomPlan.CellOf(1, 2 + row) };
        l.BasinTile = new[] { row * 5 + 4 };
        l.BasinSide = new[] { 0 };
        l.Basin = new[] { RoomPlan.CellOf(7, 2 + row) };
        l.Shape = new int[tiles];
        l.Need = new int[tiles];
        l.StartTurn = new int[tiles];
        l.Locked = new bool[tiles];
        l.LinkTo = new int[tiles];
        Array.Fill(l.LinkTo, -1);
        for (int x = 0; x < 5; x++) l.Need[row * 5 + x] = 0b0101;
        for (int t = 0; t < tiles; t++) l.StartTurn[t] = l.Need[t] != 0 ? 1 : t % 4;
        room.Fixed[l.Spout[0]] = room.Fixed[l.Basin[0]] = true;
    }

    // ----------------------------------------------------------- rules

    /// <summary>Where water runs with these turns: wet tiles, basins filled (bits) and leaks.</summary>
    public static (bool[] Wet, int Filled, int Leaks) Flood(Data l, int[] turns)
    {
        var wet = new bool[l.Size * l.Size];
        var queue = new Queue<int>();
        for (int i = 0; i < l.SpoutTile.Length; i++)
        {
            int t = l.SpoutTile[i];
            if ((Mask(l.Shape[t], turns[t]) >> l.SpoutSide[i] & 1) == 0 || wet[t]) continue;
            wet[t] = true;
            queue.Enqueue(t);
        }
        int filled = 0, leaks = 0;
        while (queue.Count > 0)
        {
            int t = queue.Dequeue();
            int m = Mask(l.Shape[t], turns[t]);
            for (int s = 0; s < 4; s++)
            {
                if ((m >> s & 1) == 0) continue;
                int n = Next(l, t, s);
                if (n < 0)
                {
                    int b = Terminal(l.BasinTile, l.BasinSide, t, s);
                    if (b >= 0) filled |= 1 << b;
                    else if (Terminal(l.SpoutTile, l.SpoutSide, t, s) < 0) leaks++;
                    continue;
                }
                if ((Mask(l.Shape[n], turns[n]) >> ((s + 2) & 3) & 1) == 0) { leaks++; continue; }
                if (wet[n]) continue;
                wet[n] = true;
                queue.Enqueue(n);
            }
        }
        return (wet, filled, leaks);
    }

    private static int Terminal(int[] tiles, int[] sides, int t, int s)
    {
        for (int i = 0; i < tiles.Length; i++) if (tiles[i] == t && sides[i] == s) return i;
        return -1;
    }

    /// <summary>Is the arm of tile <paramref name="t"/> on side <paramref name="s"/> leaking (the tile wet)?</summary>
    private static bool Leaking(Data l, int[] turns, int t, int s)
    {
        int n = Next(l, t, s);
        if (n < 0) return Terminal(l.BasinTile, l.BasinSide, t, s) < 0 && Terminal(l.SpoutTile, l.SpoutSide, t, s) < 0;
        return (Mask(l.Shape[n], turns[n]) >> ((s + 2) & 3) & 1) == 0;
    }

    public static bool Solved(Data l, int[] turns)
    {
        (_, int filled, int leaks) = Flood(l, turns);
        return filled == (1 << l.BasinTile.Length) - 1 && leaks == 0;
    }

    public static int[] Turns(PuzzleState st, RoomPlan room)
    {
        Data l = room.Layout<Data>();
        var t = new int[l.Size * l.Size];
        for (int i = 0; i < t.Length; i++) t[i] = st.Get(room, Slots[i / 15]) >> (2 * (i % 15)) & 3;
        return t;
    }

    private static void SetTurn(PuzzleState st, RoomPlan room, int i, int turn)
    {
        int slot = Slots[i / 15];
        int shift = 2 * (i % 15);
        st.SetR(room, slot, st.Get(room, slot) & ~(3 << shift) | (turn & 3) << shift);
    }

    public override void InitState(PuzzleState st, RoomPlan room)
    {
        Data l = room.Layout<Data>();
        for (int i = 0; i < l.StartTurn.Length; i++)
            st.V[room.Slot + Slots[i / 15]] |= (l.StartTurn[i] & 3) << (2 * (i % 15));
    }

    public override bool Act(PuzzleState st, RoomPlan room, int r, PuzzleAction action, int a, int b, float now)
    {
        if (action != PuzzleAction.Use) return false;
        Data l = room.Layout<Data>();
        if (a < 0 || a >= l.Shape.Length || l.Locked[a]) return true;
        int[] turns = Turns(st, room);
        turns[a] = (turns[a] + 1) & 3;
        SetTurn(st, room, a, turns[a]);
        int partner = l.LinkTo[a];
        if (partner >= 0)
        {
            turns[partner] = (turns[partner] + 1) & 3;
            SetTurn(st, room, partner, turns[partner]);
        }
        if (Solved(l, turns)) st.MarkSolved(r, now);
        return true;
    }

    private static int SolutionTurn(Data l, int i)
    {
        for (int k = 0; k < 4; k++) if (Mask(l.Shape[i], k) == l.Need[i]) return k;
        return 0;
    }

    /// <summary>Quarter turns tile <paramref name="i"/> still needs for the laid tree (0 when right, off it or bolted).</summary>
    public static int TurnsNeeded(Data l, int i, int turn)
    {
        if (l.Need[i] == 0 || l.Locked[i]) return 0;
        for (int k = 0; k < 4; k++) if (Mask(l.Shape[i], turn + k) == l.Need[i]) return k;
        return 0;
    }

    /// <summary>The tile to turn next and how many times: chained tiles first
    /// (they move their partners), then the nearest wrong tile.</summary>
    private static (int Tile, int Times) NextTile(PuzzleState st, RoomPlan room, Vector3? me)
    {
        Data l = room.Layout<Data>();
        int[] turns = Turns(st, room);
        for (int pass = 0; pass < 2; pass++)
        {
            int best = -1;
            float bestD = float.MaxValue;
            for (int i = 0; i < turns.Length; i++)
            {
                if (pass == 0 && l.LinkTo[i] < 0) continue;
                if (TurnsNeeded(l, i, turns[i]) == 0) continue;
                float dd = me is { } at ? room.World(Cell(l, i)).DistanceSquaredTo(at) : i;
                if (dd < bestD) { bestD = dd; best = i; }
            }
            if (best >= 0) return (best, TurnsNeeded(l, best, turns[best]));
        }
        return (-1, 0);
    }

    public override async Task<string?> Solve(IPuzzleDriver d, RoomPlan room, int r)
    {
        Data l = room.Layout<Data>();
        if (Solved(l, Turns(d.State, room))) return "water already through";
        int[] start = Turns(d.State, room);
        for (int i = 0; i < l.Shape.Length; i++)
            if (l.Locked[i] && l.Need[i] != 0 && Mask(l.Shape[i], start[i]) != l.Need[i]) return "a bolted tile on the channel is turned wrong";
        // Chained tiles first, then the rest; read the turns afresh after each tile.
        for (int pass = 0; pass < 2; pass++)
            for (int i = 0; i < l.Shape.Length; i++)
            {
                if ((pass == 0) != (l.LinkTo[i] >= 0)) continue;
                int k = TurnsNeeded(l, i, Turns(d.State, room)[i]);
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
        var slabLocked = PieceKit.Flat(new Color(0.17f, 0.17f, 0.18f));
        var iron = PieceKit.Flat(PieceKit.Iron);
        var pipe = PieceKit.Flat(Pipe);
        var pipeLocked = PieceKit.Flat(PipeLocked);
        var spray = PieceKit.Glow(WaterColour, 2.2f);
        for (int i = 0; i < l.Shape.Length; i++)
        {
            Vector3 at = room.World(Cell(l, i));
            bool locked = l.Locked[i];
            kit.Put(PieceKit.Mesh(new BoxMesh { Size = new Vector3(1.86f, 0.1f, 1.86f) }, at + Vector3.Up * 0.05f, locked ? slabLocked : slab, false));
            if (locked)
            {
                // Bolted: an iron frame round the tile and a bolt in each corner.
                foreach (int s in new[] { 0, 1, 2, 3 })
                {
                    var dir = new Vector3(RoomPlan.Dx[s], 0f, RoomPlan.Dz[s]);
                    kit.Put(PieceKit.Mesh(new BoxMesh { Size = s % 2 == 0 ? new Vector3(0.12f, 0.16f, 1.86f) : new Vector3(1.86f, 0.16f, 0.12f) }, at + dir * 0.87f + Vector3.Up * 0.12f, iron, false));
                }
                foreach (var c in new[] { new Vector3(1, 0, 1), new Vector3(-1, 0, 1), new Vector3(1, 0, -1), new Vector3(-1, 0, -1) })
                    kit.Put(PieceKit.Mesh(new SphereMesh { Radius = 0.13f, Height = 0.18f, RadialSegments = 8, Rings = 3 }, at + c * 0.7f + Vector3.Up * 0.2f, iron, false));
            }
            var node = new Node3D { Position = at + Vector3.Up * 0.1f, Rotation = new Vector3(0f, Angle(l.StartTurn[i]), 0f) };
            kit.Put(node);
            Material pm = locked ? pipeLocked : pipe;
            node.AddChild(PieceKit.Mesh(new CylinderMesh { TopRadius = 0.3f, BottomRadius = 0.3f, Height = 0.36f, RadialSegments = 10 }, Vector3.Up * 0.18f, pm));
            var arms = new List<(int, MeshInstance3D, Node3D)>();
            for (int s = 0; s < 4; s++)
            {
                if ((BaseMask[l.Shape[i]] >> s & 1) == 0) continue;
                var dir = new Vector3(RoomPlan.Dx[s], 0f, RoomPlan.Dz[s]);
                node.AddChild(PieceKit.Mesh(new BoxMesh { Size = new Vector3(s % 2 == 0 ? 0.95f : 0.42f, 0.34f, s % 2 == 0 ? 0.42f : 0.95f) }, dir * 0.47f + Vector3.Up * 0.17f, pm));
                // The water channel on top of the arm: shown only when water runs in it.
                var w = PieceKit.Mesh(new BoxMesh { Size = new Vector3(s % 2 == 0 ? 0.95f : 0.2f, 0.06f, s % 2 == 0 ? 0.2f : 0.95f) }, dir * 0.47f + Vector3.Up * 0.37f, PieceKit.Glow(WaterColour, 1.6f), false);
                w.Visible = false;
                node.AddChild(w);
                // A leak: water spraying up out of the arm's open end.
                var leak = new Node3D { Position = dir * 0.95f + Vector3.Up * 0.3f, Visible = false };
                leak.AddChild(PieceKit.Mesh(new SphereMesh { Radius = 0.28f, Height = 0.56f, RadialSegments = 8, Rings = 4 }, Vector3.Zero, spray, false));
                leak.AddChild(PieceKit.Mesh(new CylinderMesh { TopRadius = 0.32f, BottomRadius = 0.06f, Height = 1.1f, RadialSegments = 6 }, Vector3.Up * 0.6f, spray, false));
                node.AddChild(leak);
                arms.Add((s, w, leak));
            }
            v.Pipes.Add(node);
            v.Arms.Add(arms);
            v.Shown.Add(Angle(l.StartTurn[i]));
        }
        // Chains: a bronze bar over the tiles from a tile to the one it turns, ending in an arrowhead.
        var bronze = PieceKit.Glow(new Color(0.95f, 0.66f, 0.3f), 1.1f);
        for (int i = 0; i < l.LinkTo.Length; i++)
        {
            int j = l.LinkTo[i];
            if (j < 0) continue;
            Vector3 a = room.World(Cell(l, i)) + Vector3.Up * 0.85f, b = room.World(Cell(l, j)) + Vector3.Up * 0.85f;
            Vector3 dir = (b - a).Normalized();
            var holder = new Node3D();
            kit.Put(holder);
            PieceKit.Segment(holder, a, b - dir * 0.45f, bronze, 0.16f);
            kit.Put(PieceKit.Mesh(new TorusMesh { InnerRadius = 0.18f, OuterRadius = 0.3f, Rings = 16, RingSegments = 6 }, a, bronze, false));
            var head = PieceKit.Mesh(new CylinderMesh { TopRadius = 0f, BottomRadius = 0.3f, Height = 0.55f, RadialSegments = 4 }, b - dir * 0.3f, bronze, false);
            head.Rotation = new Vector3(Mathf.Pi * 0.5f, Mathf.Atan2(dir.X, dir.Z), 0f);
            kit.Put(head);
        }
        // Spouts: stone heads with water falling into the first tile.
        for (int k = 0; k < l.Spout.Length; k++)
        {
            Vector3 sp = room.World(l.Spout[k]);
            var into = new Vector3(-RoomPlan.Dx[l.SpoutSide[k]], 0f, -RoomPlan.Dz[l.SpoutSide[k]]);
            kit.Put(PieceKit.Mesh(new BoxMesh { Size = new Vector3(1.3f, 1.8f, 1.3f) }, sp + Vector3.Up * 0.9f, PieceKit.Flat(PieceKit.WallCap)));
            var lip = PieceKit.Mesh(new CylinderMesh { TopRadius = 0.22f, BottomRadius = 0.3f, Height = 0.6f, RadialSegments = 10 }, sp + Vector3.Up * 1.3f + into * 0.75f, PieceKit.Flat(PieceKit.Bronze));
            lip.Rotation = l.SpoutSide[k] % 2 == 0 ? new Vector3(0f, 0f, Mathf.Pi * 0.5f) : new Vector3(Mathf.Pi * 0.5f, 0f, 0f);
            kit.Put(lip);
            kit.Put(PieceKit.Mesh(new CylinderMesh { TopRadius = 0.12f, BottomRadius = 0.18f, Height = 1.1f, RadialSegments = 8 }, sp + Vector3.Up * 0.75f + into * 1.05f, PieceKit.Glow(WaterColour, 1.4f), false));
            kit.Solid(new BoxShape3D { Size = new Vector3(1.3f, 1.8f, 1.3f) }, sp + Vector3.Up * 0.9f);
        }
        // Basins: round bowls whose water rises when filled.
        for (int k = 0; k < l.Basin.Length; k++)
        {
            Vector3 bs = room.World(l.Basin[k]);
            kit.Put(PieceKit.Mesh(new CylinderMesh { TopRadius = 0.85f, BottomRadius = 0.7f, Height = 0.8f, RadialSegments = 18 }, bs + Vector3.Up * 0.4f, PieceKit.Flat(PieceKit.WallCap)));
            var water = PieceKit.Flat(new Color(0.2f, 0.28f, 0.32f));
            var level = new Node3D { Position = bs + Vector3.Up * 0.3f };
            kit.Put(level);
            level.AddChild(PieceKit.Mesh(new CylinderMesh { TopRadius = 0.72f, BottomRadius = 0.72f, Height = 0.06f, RadialSegments = 18 }, Vector3.Zero, water, false));
            kit.Post(bs, 0.85f, 0.8f);
            v.BasinWater.Add(water);
            v.BasinLevel.Add(level);
        }
        return v;
    }

    public override void Sync(PieceKit kit, object view, RoomPlan room, PuzzleState st, float t, float dt, bool changed)
    {
        var v = (View)view;
        Data l = room.Layout<Data>();
        int[] turns = Turns(st, room);
        bool solved = st.Solved(room);
        if (changed || !v.Drawn)
        {
            v.Drawn = true;
            (bool[] wet, int filled, _) = Flood(l, turns);
            if (solved) filled = (1 << l.Basin.Length) - 1;
            for (int i = 0; i < turns.Length; i++)
                foreach (var (side, w, leak) in v.Arms[i])
                {
                    w.Visible = wet[i];
                    leak.Visible = wet[i] && !solved && Leaking(l, turns, i, (side + turns[i]) & 3);
                }
            v.Filled = filled;
            for (int k = 0; k < l.Basin.Length; k++)
                PieceKit.Lit(v.BasinWater[k], (filled >> k & 1) != 0, WaterColour, new Color(0.2f, 0.28f, 0.32f), 1.6f);
        }
        for (int i = 0; i < turns.Length; i++)
        {
            float target = Angle(turns[i]);
            v.Shown[i] = Mathf.LerpAngle(v.Shown[i], target, Mathf.Min(1f, dt * 12f));
            v.Pipes[i].Rotation = new Vector3(0f, v.Shown[i], 0f);
            foreach (var (_, _, leak) in v.Arms[i])
                if (leak.Visible) leak.Scale = Vector3.One * (0.85f + 0.2f * Mathf.Sin(t * 14f + i));
        }
        for (int k = 0; k < l.Basin.Length; k++)
        {
            bool full = solved || (v.Filled >> k & 1) != 0;
            Vector3 home = room.World(l.Basin[k]) + Vector3.Up * (full ? 0.76f : 0.3f);
            v.BasinLevel[k].Position = v.BasinLevel[k].Position.MoveToward(home, dt * 0.8f);
        }
    }

    public override void Sounds(object view, RoomPlan room, Func<int, int> was, Func<int, int> now)
    {
        foreach (int s in Slots)
            if (was(s) != now(s)) { AudioDirector.PlayPuzzle(PuzzleSound.Click, 1.05f); break; }
    }

    public override void Offer(PuzzleMap map, RoomPlan room, FocusOffer offer)
    {
        Data l = room.Layout<Data>();
        for (int i = 0; i < l.Shape.Length; i++)
            if (!l.Locked[i]) offer(PuzzleAction.Use, i, room.World(Cell(l, i)), Verb);
    }

    public override PuzzleHint? HintAt(PuzzleState st, RoomPlan room, Vector3? me, float now)
    {
        Data l = room.Layout<Data>();
        (int i, int k) = NextTile(st, room, me);
        if (i < 0) return null;
        return new PuzzleHint(room.World(Cell(l, i)), k.ToString());
    }

    public override string Status(PuzzleMap map, RoomPlan room)
    {
        Data l = room.Layout<Data>();
        (_, int filled, int leaks) = Flood(l, Turns(map.State, room));
        if (map.State.Solved(room)) { filled = (1 << l.Basin.Length) - 1; leaks = 0; }
        return Meta.Loc.F(Line, PuzzleSolver.PopCount(filled), l.Basin.Length, leaks);
    }

    public override Vector3 Bot(PuzzleBot bot, RoomPlan room, Vector3 me)
    {
        Data l = room.Layout<Data>();
        (int i, _) = NextTile(bot.State, room, me);
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
