using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Wavebreak.Audio;
using Wavebreak.Core;

namespace Wavebreak.Game.Puzzle.Kinds;

/// <summary>
/// Lantern Path: a patch of floor tiles with a starting tile (carved with a
/// star) and, from difficulty 1, a last tile (carved with a moon). Step on
/// the start, then onto tiles next to the last one lit: each lights as you
/// arrive, and the path may never cross itself. Light every tile in one
/// unbroken path, ending on the moon (it only lights as the very last tile).
/// From difficulty 2, low fences stand between some neighbouring tiles: a
/// step across a fence does not count. From difficulty 3 the lanterns go
/// dark if the path stalls: the next tile must be lit within a few seconds
/// of the last, so the whole route has to be planned before the first step.
/// Walking on lit tiles or off the patch does nothing; a dead end needs the
/// reset post. Any player's steps count, so one is enough and a team must
/// agree who walks.
///
/// Difficulty: d0 a 4 x 4 patch with a hole, no moon (15 tiles); d1 4 x 5,
/// moon; d2 5 x 5, two holes, two fences; d3 5 x 6, three fences, 5 s per
/// step; d4 6 x 6, five holes, four fences, 4 s; d5 6 x 6, five holes, six
/// fences, 3.2 s. Generation keeps a start and moon with the fewest paths
/// between them (found by search).
///
/// State: RA tiles lit (bits, at most 31 tiles); RB the last tile lit (-1
/// none); RD when it was lit (tenths); RResets resets; RFails paths that
/// went dark. Solver: depth-first search for a path through every unlit
/// tile ending on the moon, pruned by connectivity.
/// </summary>
public sealed class TrailKind : PuzzleKind
{
    public sealed class Data
    {
        public int[] Cells = Array.Empty<int>();
        public int Start;
        /// <summary>The tile that must be lit last, or -1 (any).</summary>
        public int End = -1;
        /// <summary>Neighbouring tile pairs (indices into <see cref="Cells"/>) a step may not cross.</summary>
        public (int A, int B)[] Fences = Array.Empty<(int, int)>();
        /// <summary>Seconds a lit path waits for its next tile (0: forever).</summary>
        public float StepWindow;
        public int ResetCell = -1;
        /// <summary>Derived: neighbouring tiles as bits (fences cut).</summary>
        public int[]? Neighbours;
    }

    private sealed class View
    {
        public readonly List<Node3D> Tiles = new();
        public readonly List<StandardMaterial3D> Mats = new();
        public Node3D Head = null!;
        public Node3D? Star;
        public Node3D? Moon;
    }

    private const string VerbReset = "Put the lanterns out";
    private const string LitLine = "Tiles lit: {0}/{1}";
    private const string LitTimedLine = "Tiles lit: {0}/{1}  ·  the lanterns go dark in {2} s";

    public override string Id => "trail";
    public override string Title => "Lantern Path";
    public override string Objective => "Start on the star tile and light every tile in one unbroken path that never crosses itself, ending on the moon tile. Fences cannot be stepped across; a stalled path goes dark.";
    public override string Hint => "Hint: step onto the marked tile next. The post starts the path over.";
    public override IEnumerable<string> Strings => new[] { VerbReset, LitLine, LitTimedLine };
    public override HallStyle Style => HallStyle.Garden;
    public override float ParSeconds => 26f;

    private static readonly Color TileIdle = new(0.36f, 0.34f, 0.30f);
    private static readonly Color TileLit = new(1f, 0.80f, 0.42f);

    /// <summary>Patch width and depth, holes, fences, a moon, and the step window.</summary>
    private static (int W, int H, int Holes, int Fences, bool Moon, float Window) Knobs(int d) => Math.Clamp(d, 0, 5) switch
    {
        0 => (4, 4, 1, 0, false, 0f),
        1 => (4, 5, 1, 0, true, 0f),
        2 => (5, 5, 2, 2, true, 0f),
        3 => (5, 6, 2, 3, true, 5f),
        4 => (6, 6, 5, 4, true, 4f),
        _ => (6, 6, 5, 6, true, 3.2f),
    };

    private static int Full(Data l) => (int)((1L << l.Cells.Length) - 1);

    // ------------------------------------------------------------- data

    public override void Generate(RoomPlan room, Rng rng, int d)
    {
        var l = new Data();
        room.Data = l;
        (int w, int h, int holes, int fences, bool moon, float window) = Knobs(d);
        l.StepWindow = window;
        for (int attempt = 0; attempt < 120; attempt++)
        {
            PuzzleGen.Clear(room);
            l.ResetCell = PuzzleGen.Corner(rng.RangeInt(0, 4));
            room.Fixed[l.ResetCell] = true;
            bool flip = rng.Chance(0.5f);
            int ww = flip ? h : w, hh = flip ? w : h;
            int x0 = RoomPlan.Mid - ww / 2, z0 = RoomPlan.Mid - hh / 2;
            var cells = new List<int>();
            for (int z = 0; z < hh; z++)
                for (int x = 0; x < ww; x++) cells.Add(RoomPlan.CellOf(x0 + x, z0 + z));
            for (int i = 0; i < holes && cells.Count > 6; i++) cells.RemoveAt(rng.RangeInt(0, cells.Count));
            if (cells.Count > 31) continue;
            l.Cells = cells.ToArray();
            l.Fences = Array.Empty<(int, int)>();
            l.Neighbours = null;
            l.End = -1;
            if (!Connected(l)) continue;
            // Fences on random inner edges, keeping the patch in one piece.
            if (fences > 0)
            {
                var edges = new List<(int, int)>();
                for (int a = 0; a < l.Cells.Length; a++)
                    for (int b = a + 1; b < l.Cells.Length; b++)
                        if (Adjacent(l.Cells[a], l.Cells[b])) edges.Add((a, b));
                PuzzleGen.Shuffle(rng, edges);
                var kept = new List<(int, int)>();
                foreach ((int a, int b) e in edges)
                {
                    if (kept.Count >= fences) break;
                    kept.Add(e);
                    l.Fences = kept.ToArray();
                    l.Neighbours = null;
                    // No tile left with a single way in or out except at most two.
                    if (!Connected(l) || DeadEnds(l) > 2) kept.RemoveAt(kept.Count - 1);
                }
                l.Fences = kept.ToArray();
                l.Neighbours = null;
            }
            if (!Pick(l, rng, moon)) continue;
            if (!PuzzleSolver.DoorsConnected(room)) continue;
            room.ParSeconds = 16f + 1.7f * l.Cells.Length + 4f * l.Fences.Length + (window > 0f ? 10f : 0f);
            return;
        }
        // Fallback: a 3 x 3 patch, start in a corner, no moon (a snake always works).
        PuzzleGen.Clear(room);
        l.ResetCell = PuzzleGen.Corner(0);
        room.Fixed[l.ResetCell] = true;
        var f = new List<int>();
        for (int z = 3; z <= 5; z++) for (int x = 3; x <= 5; x++) f.Add(RoomPlan.CellOf(x, z));
        l.Cells = f.ToArray();
        l.Fences = Array.Empty<(int, int)>();
        l.Neighbours = null;
        l.Start = 0;
        l.End = -1;
        Cache.Remove(l);
    }

    /// <summary>Choose the start (and the moon): the pair with the fewest
    /// paths through every tile, from a bounded search.</summary>
    private static bool Pick(Data l, Rng rng, bool moon)
    {
        var starts = new List<int>();
        for (int i = 0; i < l.Cells.Length; i++) if (StartPossible(l, i)) starts.Add(i);
        PuzzleGen.Shuffle(rng, starts);
        int bestStart = -1, bestEnd = -1, bestCount = int.MaxValue;
        int tried = 0;
        foreach (int s in starts)
        {
            if (++tried > 3) break;
            l.End = -1;
            var ends = new Dictionary<int, int>();
            int nodes = 0, found = 0;
            Search(l, s, 1 << s, p =>
            {
                int e = p.Count > 0 ? p[^1] : s;
                ends[e] = ends.GetValueOrDefault(e) + 1;
                return ++found < 600;
            }, new List<int>(), ref nodes, 12000);
            if (found == 0) continue;
            if (!moon)
            {
                if (found < bestCount) { bestCount = found; bestStart = s; }
                continue;
            }
            foreach ((int e, int n) in ends)
                if (n < bestCount || n == bestCount && rng.Chance(0.5f)) { bestCount = n; bestStart = s; bestEnd = e; }
            if (bestCount <= 2) break;
        }
        if (bestStart < 0) return false;
        l.Start = bestStart;
        l.End = moon ? bestEnd : -1;
        // The fixed path must still be found from scratch (an earlier draw's
        // answers were cached for this same layout object).
        Cache.Remove(l);
        return Solve(l, 0, -1) is not null;
    }

    private static int IndexOf(Data l, int cell) => Array.IndexOf(l.Cells, cell);

    private static bool Adjacent(int a, int b) => Math.Abs(RoomPlan.X(a) - RoomPlan.X(b)) + Math.Abs(RoomPlan.Z(a) - RoomPlan.Z(b)) == 1;

    private static bool Fenced(Data l, int a, int b)
    {
        foreach ((int x, int y) in l.Fences) if (x == a && y == b || x == b && y == a) return true;
        return false;
    }

    /// <summary>Per tile, its neighbouring tiles as bits, fences cut (built once per layout).</summary>
    private static int[] Nbr(Data l)
    {
        if (l.Neighbours is { } n && n.Length == l.Cells.Length) return n;
        n = new int[l.Cells.Length];
        for (int i = 0; i < n.Length; i++)
            for (int k = 0; k < n.Length; k++)
                if (Adjacent(l.Cells[i], l.Cells[k]) && !Fenced(l, i, k)) n[i] |= 1 << k;
        return l.Neighbours = n;
    }

    /// <summary>Can a step go from tile <paramref name="a"/> to tile <paramref name="b"/>?</summary>
    public static bool Linked(Data l, int a, int b) => a >= 0 && b >= 0 && (Nbr(l)[a] >> b & 1) != 0;

    private static bool Connected(Data l) => Reach(l, 0, 0) == Full(l);

    private static int DeadEnds(Data l)
    {
        int[] nbr = Nbr(l);
        int n = 0;
        foreach (int m in nbr) if (PuzzleSolver.PopCount(m) <= 1) n++;
        return n;
    }

    /// <summary>Tiles (bits) reachable from tile <paramref name="from"/> through tiles not in <paramref name="blocked"/>.</summary>
    private static int Reach(Data l, int from, int blocked)
    {
        int[] nbr = Nbr(l);
        int seen = 1 << from, frontier = seen;
        while (frontier != 0)
        {
            int grow = 0;
            for (int f = frontier; f != 0; f &= f - 1) grow |= nbr[System.Numerics.BitOperations.TrailingZeroCount(f)];
            frontier = grow & ~seen & ~blocked;
            seen |= frontier;
        }
        return seen;
    }

    /// <summary>Can a path through every tile start at <paramref name="start"/>
    /// at all? (Colour count on the checkerboard, and the dead-end tiles.)</summary>
    private static bool StartPossible(Data l, int start)
    {
        int[] nbr = Nbr(l);
        int dark = 0, ends = 0;
        bool startIsEnd = false;
        for (int i = 0; i < l.Cells.Length; i++)
        {
            if ((RoomPlan.X(l.Cells[i]) + RoomPlan.Z(l.Cells[i])) % 2 == 0) dark++;
            if (PuzzleSolver.PopCount(nbr[i]) == 1) { ends++; startIsEnd |= i == start; }
        }
        int light = l.Cells.Length - dark;
        bool startDark = (RoomPlan.X(l.Cells[start]) + RoomPlan.Z(l.Cells[start])) % 2 == 0;
        if (Math.Abs(dark - light) > 1) return false;
        if (dark > light && !startDark || light > dark && startDark) return false;
        if (ends > 2 || ends == 2 && !startIsEnd) return false;
        return true;
    }

    /// <summary>Depth-first search from (head, lit): every way through the
    /// unlit tiles that ends on the moon (<paramref name="found"/> for each,
    /// false to stop).</summary>
    private static void Search(Data l, int head, int lit, Func<List<int>, bool> found, List<int> path, ref int nodes, int maxNodes)
    {
        int full = Full(l);
        if (lit == full) { if (!found(path)) nodes = int.MaxValue; return; }
        if (++nodes > maxNodes) return;
        int[] nbr = Nbr(l);
        for (int m = nbr[head] & ~lit; m != 0 && nodes <= maxNodes; m &= m - 1)
        {
            int n = System.Numerics.BitOperations.TrailingZeroCount(m);
            int next = lit | 1 << n;
            // The moon lights only as the last tile.
            if (n == l.End && next != full) continue;
            // Every unlit tile must still be reachable from the new head.
            if ((Reach(l, n, next & ~(1 << n)) | next) != full) continue;
            path.Add(n);
            Search(l, n, next, found, path, ref nodes, maxNodes);
            path.RemoveAt(path.Count - 1);
        }
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Data, Dictionary<long, List<int>?>> Cache = new();

    /// <summary>The tiles still to light, in order, from this state; null at a
    /// dead end. Cached per state: the bot and the hint ask every frame.</summary>
    public static List<int>? Solve(Data l, int lit, int head)
    {
        Dictionary<long, List<int>?> memo = Cache.GetValue(l, _ => new Dictionary<long, List<int>?>());
        long key = (long)(uint)lit << 8 | (uint)(head + 1);
        if (memo.TryGetValue(key, out List<int>? known)) return known;
        List<int>? result;
        if (head < 0)
        {
            result = StartPossible(l, l.Start) && l.Start != l.End ? Solve(l, 1 << l.Start, l.Start) : null;
            if (result is not null) result = new List<int>(result);
            result?.Insert(0, l.Start);
        }
        else if (lit == Full(l)) result = new List<int>();
        else
        {
            List<int>? answer = null;
            int nodes = 0;
            Search(l, head, lit, p => { answer = new List<int>(p); return false; }, new List<int>(), ref nodes, 200000);
            result = answer;
        }
        if (memo.Count > 4096) memo.Clear();
        memo[key] = result;
        return result;
    }

    public override void InitState(PuzzleState st, RoomPlan room) => st.V[room.Slot + PuzzleState.RB] = -1;

    /// <summary>Seconds before a stalled path goes dark, or -1 (no timer, or no path yet).</summary>
    public static float DarkIn(PuzzleState st, RoomPlan room, float now)
    {
        Data l = room.Layout<Data>();
        if (l.StepWindow <= 0f || st.Get(room, PuzzleState.RB) < 0) return -1f;
        return Mathf.Max(0f, l.StepWindow - (now - st.Get(room, PuzzleState.RD) / 10f));
    }

    private static void Dark(PuzzleState st, RoomPlan room)
    {
        st.SetR(room, PuzzleState.RA, 0);
        st.SetR(room, PuzzleState.RB, -1);
    }

    public override void HostStep(PuzzleState st, RoomPlan room, int r, IReadOnlyList<Vector3> players, float now)
    {
        Data l = room.Layout<Data>();
        int lit = st.Get(room, PuzzleState.RA), head = st.Get(room, PuzzleState.RB);
        int full = Full(l);
        bool stepped = false;
        foreach (Vector3 p in players)
        {
            if (p.Y > room.Centre.Y + 1.6f) continue;
            int t = IndexOf(l, room.CellAt(p));
            if (t < 0 || (lit >> t & 1) != 0) continue;
            if (head < 0 ? t != l.Start : !Linked(l, head, t)) continue;
            if (t == l.End && (lit | 1 << t) != full) continue;
            lit |= 1 << t;
            head = t;
            stepped = true;
        }
        if (stepped)
        {
            st.SetR(room, PuzzleState.RA, lit);
            st.SetR(room, PuzzleState.RB, head);
            st.SetR(room, PuzzleState.RD, PuzzleState.Tenths(now));
            if (lit == full) { st.MarkSolved(r, now); return; }
        }
        // A stalled path: every lantern goes dark.
        if (DarkIn(st, room, now) == 0f)
        {
            Dark(st, room);
            st.SetR(room, PuzzleState.RFails, st.Get(room, PuzzleState.RFails) + 1);
        }
    }

    public override bool Act(PuzzleState st, RoomPlan room, int r, PuzzleAction action, int a, int b, float now)
    {
        if (action != PuzzleAction.Reset) return false;
        Dark(st, room);
        st.SetR(room, PuzzleState.RResets, st.Get(room, PuzzleState.RResets) + 1);
        return true;
    }

    public override async Task<string?> Solve(IPuzzleDriver d, RoomPlan room, int r)
    {
        Data l = room.Layout<Data>();
        if (l.Cells.Length < 6) return "too few tiles";
        List<int>? path = Solve(l, d.State.Get(room, PuzzleState.RA), d.State.Get(room, PuzzleState.RB));
        if (path is null)
        {
            await d.Ask(PuzzleAction.Reset, 0);
            path = Solve(l, 0, -1);
        }
        if (path is null) return "no path through every tile";
        if (l.End >= 0 && path.Count > 0 && path[^1] != l.End) return "the path does not end on the moon";
        foreach (int t in path)
        {
            await d.Stand(room.World(l.Cells[t]));
            await d.Wait(0.15f);
        }
        return null;
    }

    // ------------------------------------------------------------ scene

    public override object Build(PieceKit kit, RoomPlan room)
    {
        Data l = room.Layout<Data>();
        var v = new View();
        for (int i = 0; i < l.Cells.Length; i++)
        {
            Vector3 at = room.World(l.Cells[i]);
            kit.Put(PieceKit.Mesh(new BoxMesh { Size = new Vector3(1.9f, 0.06f, 1.9f) }, at + Vector3.Up * 0.03f, PieceKit.Flat(PieceKit.BronzeDark), false));
            var mat = PieceKit.Flat(TileIdle);
            var tile = new Node3D { Position = at + Vector3.Up * 0.06f };
            kit.Put(tile);
            tile.AddChild(PieceKit.Mesh(new BoxMesh { Size = new Vector3(1.6f, 0.08f, 1.6f) }, Vector3.Up * 0.04f, mat, false));
            // A lantern stub in the tile's middle that rises when lit (shape, not just colour).
            tile.AddChild(PieceKit.Mesh(new CylinderMesh { TopRadius = 0.18f, BottomRadius = 0.24f, Height = 0.3f, RadialSegments = 8 }, Vector3.Up * 0.12f, mat, false));
            v.Tiles.Add(tile);
            v.Mats.Add(mat);
            if (i == l.Start)
            {
                // The start: a star turning over the tile, and a ring round it.
                v.Star = PieceKit.Symbol(RuneSymbol.Star, PieceKit.Glow(PieceKit.Warm, 1.6f));
                v.Star.Position = at + Vector3.Up * 1.5f;
                kit.Put(v.Star);
                kit.Put(PieceKit.Mesh(new TorusMesh { InnerRadius = 0.85f, OuterRadius = 0.98f, Rings = 28, RingSegments = 6 }, at + Vector3.Up * 0.08f, PieceKit.Glow(PieceKit.Warm, 1.2f), false));
            }
            if (i == l.End)
            {
                // The end: a crescent moon (a ring with a bite out of it) and a square frame.
                v.Moon = new Node3D { Position = at + Vector3.Up * 1.5f };
                kit.Put(v.Moon);
                var glow = PieceKit.Glow(new Color(0.75f, 0.85f, 1f), 1.6f);
                var arc = PieceKit.Mesh(new TorusMesh { InnerRadius = 0.28f, OuterRadius = 0.42f, Rings = 20, RingSegments = 6 }, Vector3.Zero, glow, false);
                arc.Rotation = new Vector3(Mathf.Pi * 0.5f, 0f, 0f);
                v.Moon.AddChild(arc);
                v.Moon.AddChild(PieceKit.Mesh(new SphereMesh { Radius = 0.3f, Height = 0.6f }, new Vector3(0.2f, 0.12f, 0f), PieceKit.Flat(PieceKit.Iron), false));
                foreach ((float x, float z, float sx, float sz) in new[] { (0f, 0.85f, 1.8f, 0.1f), (0f, -0.85f, 1.8f, 0.1f), (0.85f, 0f, 0.1f, 1.8f), (-0.85f, 0f, 0.1f, 1.8f) })
                    kit.Put(PieceKit.Mesh(new BoxMesh { Size = new Vector3(sx, 0.05f, sz) }, at + new Vector3(x, 0.1f, z), glow, false));
            }
        }
        // Fences: a low iron rail with spikes on the edge between two tiles.
        var iron = PieceKit.Flat(PieceKit.Iron);
        foreach ((int a, int b) in l.Fences)
        {
            Vector3 pa = room.World(l.Cells[a]), pb = room.World(l.Cells[b]);
            Vector3 mid = (pa + pb) * 0.5f;
            bool alongX = Mathf.Abs(pa.X - pb.X) > 0.1f;
            var size = alongX ? new Vector3(0.12f, 0.35f, 1.8f) : new Vector3(1.8f, 0.35f, 0.12f);
            kit.Put(PieceKit.Mesh(new BoxMesh { Size = size }, mid + Vector3.Up * 0.25f, iron, false));
            for (int s = -2; s <= 2; s++)
            {
                Vector3 off = alongX ? new Vector3(0f, 0f, s * 0.4f) : new Vector3(s * 0.4f, 0f, 0f);
                kit.Put(PieceKit.Mesh(new CylinderMesh { TopRadius = 0f, BottomRadius = 0.07f, Height = 0.3f, RadialSegments = 4 }, mid + off + Vector3.Up * 0.57f, iron, false));
            }
        }
        // The head marker: a small flame over the last tile lit (it shrinks as the path stalls).
        v.Head = new Node3D { Visible = false };
        kit.Put(v.Head);
        v.Head.AddChild(PieceKit.Mesh(new CylinderMesh { TopRadius = 0f, BottomRadius = 0.3f, Height = 0.7f, RadialSegments = 8 }, Vector3.Up * 1.2f, PieceKit.Glow(PieceKit.Flame, 2.5f), false));
        kit.ResetPost(room.World(l.ResetCell));
        return v;
    }

    public override void Sync(PieceKit kit, object view, RoomPlan room, PuzzleState st, float t, float dt, bool changed)
    {
        var v = (View)view;
        Data l = room.Layout<Data>();
        int lit = st.Get(room, PuzzleState.RA), head = st.Get(room, PuzzleState.RB);
        bool solved = st.Solved(room);
        for (int i = 0; i < v.Tiles.Count; i++)
        {
            bool on = solved || (lit >> i & 1) != 0;
            Node3D tile = v.Tiles[i];
            float y = Mathf.MoveToward(tile.Position.Y, on ? 0.16f : 0.06f, dt * 1.2f);
            tile.Position = tile.Position with { Y = y };
            PieceKit.Lit(v.Mats[i], on, TileLit, TileIdle, 1.4f);
        }
        v.Star?.RotateY(dt * 1.2f);
        if (v.Star is not null) v.Star.Visible = head < 0 && !solved;
        if (v.Moon is not null)
        {
            v.Moon.RotateY(dt * 0.8f);
            v.Moon.Visible = !solved;
        }
        v.Head.Visible = head >= 0 && !solved;
        if (head >= 0)
        {
            v.Head.Position = room.World(l.Cells[head]) + Vector3.Up * (Mathf.Sin(t * 5f) * 0.12f);
            float dark = DarkIn(st, room, t);
            v.Head.Scale = Vector3.One * (dark < 0f ? 1f : 0.35f + 0.65f * dark / Mathf.Max(0.1f, l.StepWindow));
        }
    }

    public override void Sounds(object view, RoomPlan room, Func<int, int> was, Func<int, int> now)
    {
        int gained = now(PuzzleState.RA) & ~was(PuzzleState.RA);
        if (gained != 0) AudioDirector.PlayPuzzle(PuzzleSound.Bell, 1f + 0.04f * PuzzleSolver.PopCount(now(PuzzleState.RA)));
        else if (now(PuzzleState.RResets) != was(PuzzleState.RResets) || now(PuzzleState.RFails) > was(PuzzleState.RFails)) AudioDirector.PlayPuzzle(PuzzleSound.Wrong);
    }

    public override void Offer(PuzzleMap map, RoomPlan room, FocusOffer offer)
        => offer(PuzzleAction.Reset, 0, room.World(room.Layout<Data>().ResetCell), VerbReset);

    public override PuzzleHint? HintAt(PuzzleState st, RoomPlan room, Vector3? me, float now)
    {
        Data l = room.Layout<Data>();
        List<int>? path = Solve(l, st.Get(room, PuzzleState.RA), st.Get(room, PuzzleState.RB));
        if (path is null) return new PuzzleHint(room.World(l.ResetCell), "!");
        return path.Count == 0 ? null : new PuzzleHint(room.World(l.Cells[path[0]]), "!");
    }

    public override string Status(PuzzleMap map, RoomPlan room)
    {
        int lit = PuzzleSolver.PopCount(map.State.Get(room, PuzzleState.RA)), total = room.Layout<Data>().Cells.Length;
        float dark = DarkIn(map.State, room, map.Elapsed);
        return dark >= 0f ? Meta.Loc.F(LitTimedLine, lit, total, Mathf.CeilToInt(dark)) : Meta.Loc.F(LitLine, lit, total);
    }

    public override Vector3 Bot(PuzzleBot bot, RoomPlan room, Vector3 me)
    {
        Data l = room.Layout<Data>();
        int lit = bot.State.Get(room, PuzzleState.RA), head = bot.State.Get(room, PuzzleState.RB);
        List<int>? path = Solve(l, lit, head);
        if (path is null) return bot.Use(room, me, room.World(l.ResetCell), PuzzleAction.Reset, 0);
        if (path.Count == 0) return Vector3.Zero;
        int target = l.Cells[path[0]];
        // Never cross an unlit tile the path does not want next: it would light.
        bool Avoid(int c)
        {
            int t = IndexOf(l, c);
            if (t < 0 || c == target || (lit >> t & 1) != 0) return false;
            return head < 0 ? t == l.Start : Linked(l, head, t);
        }
        return PuzzleBot.WalkGrid(room, me, target, false, Avoid);
    }

    public override void Glyph(CanvasItem ci, Vector2 at, float size, Color c)
    {
        float h = size * 0.6f, w = Mathf.Max(1f, size * 0.16f);
        ci.DrawPolyline(new[] { at + new Vector2(-h, h), at + new Vector2(-h, -h), at + new Vector2(0f, -h), at + new Vector2(0f, h), at + new Vector2(h, h), at + new Vector2(h, -h) }, c, w);
    }
}
