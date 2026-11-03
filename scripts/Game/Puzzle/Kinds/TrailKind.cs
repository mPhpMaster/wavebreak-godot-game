using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Wavebreak.Audio;
using Wavebreak.Core;

namespace Wavebreak.Game.Puzzle.Kinds;

/// <summary>
/// Lantern Path: a patch of floor tiles with one starting tile (carved with a
/// star). Step on the start, then onto tiles next to the last one lit: each
/// lights as you arrive. Light every tile in one unbroken path. Walking on
/// lit tiles or off the patch does nothing, so only the order is the puzzle;
/// a dead end needs the reset post. Any player's steps count, so one is
/// enough and a team must agree who walks.
///
/// Generation cuts a rectangle with a few holes and keeps only a start from
/// which a path through every tile exists (found by search), preferring the
/// start with the fewest such paths.
///
/// State: RA tiles lit (bits); RB the last tile lit (-1 none); RResets resets.
/// Solver: depth-first search for a path through every unlit tile, pruned by
/// connectivity.
/// </summary>
public sealed class TrailKind : PuzzleKind
{
    public sealed class Data
    {
        public int[] Cells = Array.Empty<int>();
        public int Start;
        public int ResetCell = -1;
        /// <summary>Derived: neighbouring tiles as bits.</summary>
        public int[]? Neighbours;
    }

    private sealed class View
    {
        public readonly List<Node3D> Tiles = new();
        public readonly List<StandardMaterial3D> Mats = new();
        public Node3D Head = null!;
        public Node3D? Star;
    }

    private const string VerbReset = "Put the lanterns out";
    private const string LitLine = "Tiles lit: {0}/{1}";

    public override string Id => "trail";
    public override string Title => "Lantern Path";
    public override string Objective => "Start on the star tile and light every tile in one unbroken path: each step must go to a tile next to the last one lit.";
    public override string Hint => "Hint: step onto the marked tile next. The post starts the path over.";
    public override IEnumerable<string> Strings => new[] { VerbReset, LitLine };
    public override HallStyle Style => HallStyle.Garden;
    public override float ParSeconds => 26f;

    private static readonly Color TileIdle = new(0.36f, 0.34f, 0.30f);
    private static readonly Color TileLit = new(1f, 0.80f, 0.42f);

    // ------------------------------------------------------------- data

    public override void Generate(RoomPlan room, Rng rng, int d)
    {
        var l = new Data();
        room.Data = l;
        (int w, int h) = d switch { 0 => (3, 4), 1 => (4, 4), 2 => (4, 5), _ => (5, 5) };
        int holes = d switch { 0 => 1, 1 => 1, 2 => 2, _ => 3 };
        for (int attempt = 0; attempt < 200; attempt++)
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
            l.Cells = cells.ToArray();
            l.Neighbours = null;
            if (!Connected(l)) continue;
            // A start a path through every tile leaves from; the fewest paths wins.
            var starts = new List<int>();
            for (int i = 0; i < l.Cells.Length; i++) starts.Add(i);
            PuzzleGen.Shuffle(rng, starts);
            int best = -1, bestCount = int.MaxValue;
            foreach (int s in starts)
            {
                int count = CountPaths(l, s, 64);
                if (count > 0 && count < bestCount) { best = s; bestCount = count; }
                if (bestCount <= 2) break;
            }
            if (best < 0) continue;
            l.Start = best;
            if (!PuzzleSolver.DoorsConnected(room)) continue;
            return;
        }
        // Fallback: a 3 x 3 patch, start in a corner (a snake always works).
        PuzzleGen.Clear(room);
        l.ResetCell = PuzzleGen.Corner(0);
        room.Fixed[l.ResetCell] = true;
        var f = new List<int>();
        for (int z = 3; z <= 5; z++) for (int x = 3; x <= 5; x++) f.Add(RoomPlan.CellOf(x, z));
        l.Cells = f.ToArray();
        l.Neighbours = null;
        l.Start = 0;
    }

    private static int IndexOf(Data l, int cell) => Array.IndexOf(l.Cells, cell);

    private static bool Adjacent(int a, int b) => Math.Abs(RoomPlan.X(a) - RoomPlan.X(b)) + Math.Abs(RoomPlan.Z(a) - RoomPlan.Z(b)) == 1;

    /// <summary>Per tile, its neighbouring tiles as bits (built once per layout).</summary>
    private static int[] Nbr(Data l)
    {
        if (l.Neighbours is { } n && n.Length == l.Cells.Length) return n;
        n = new int[l.Cells.Length];
        for (int i = 0; i < n.Length; i++)
            for (int k = 0; k < n.Length; k++)
                if (Adjacent(l.Cells[i], l.Cells[k])) n[i] |= 1 << k;
        return l.Neighbours = n;
    }

    private static bool Connected(Data l) => Reach(l, 0, 0) == (1 << l.Cells.Length) - 1;

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
    /// unlit tiles (<paramref name="onPath"/> for each step, false to stop).</summary>
    private static void Search(Data l, int head, int lit, Func<List<int>, bool> found, List<int> path, ref int nodes, int maxNodes)
    {
        int full = (1 << l.Cells.Length) - 1;
        if (lit == full) { if (!found(path)) nodes = int.MaxValue; return; }
        if (++nodes > maxNodes) return;
        int[] nbr = Nbr(l);
        for (int m = nbr[head] & ~lit; m != 0 && nodes <= maxNodes; m &= m - 1)
        {
            int n = System.Numerics.BitOperations.TrailingZeroCount(m);
            int next = lit | 1 << n;
            // Every unlit tile must still be reachable from the new head.
            if ((Reach(l, n, next & ~(1 << n)) | next) != full) continue;
            path.Add(n);
            Search(l, n, next, found, path, ref nodes, maxNodes);
            path.RemoveAt(path.Count - 1);
        }
    }

    /// <summary>Paths through every tile starting at <paramref name="start"/>, counted up to <paramref name="cap"/>.</summary>
    private static int CountPaths(Data l, int start, int cap)
    {
        if (!StartPossible(l, start)) return 0;
        int count = 0, nodes = 0;
        Search(l, start, 1 << start, _ => ++count < cap, new List<int>(), ref nodes, 60000);
        return count;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Data, Dictionary<long, List<int>?>> Cache = new();

    /// <summary>The tiles still to light, in order, from this state; null at a
    /// dead end. Cached per state: the bot and the hint ask every frame.</summary>
    public static List<int>? Solve(Data l, int lit, int head)
    {
        Dictionary<long, List<int>?> memo = Cache.GetValue(l, _ => new Dictionary<long, List<int>?>());
        long key = (long)lit << 8 | (uint)(head + 1);
        if (memo.TryGetValue(key, out List<int>? known)) return known;
        List<int>? result;
        if (head < 0)
        {
            result = StartPossible(l, l.Start) ? Solve(l, 1 << l.Start, l.Start) : null;
            if (result is not null) result = new List<int>(result) { };
            result?.Insert(0, l.Start);
        }
        else if (lit == (1 << l.Cells.Length) - 1) result = new List<int>();
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

    public override void HostStep(PuzzleState st, RoomPlan room, int r, IReadOnlyList<Vector3> players, float now)
    {
        Data l = room.Layout<Data>();
        int lit = st.Get(room, PuzzleState.RA), head = st.Get(room, PuzzleState.RB);
        foreach (Vector3 p in players)
        {
            if (p.Y > room.Centre.Y + 1.6f) continue;
            int t = IndexOf(l, room.CellAt(p));
            if (t < 0 || (lit >> t & 1) != 0) continue;
            if (head < 0 ? t != l.Start : !Adjacent(l.Cells[head], l.Cells[t])) continue;
            lit |= 1 << t;
            head = t;
        }
        st.SetR(room, PuzzleState.RA, lit);
        st.SetR(room, PuzzleState.RB, head);
        if (lit == (1 << l.Cells.Length) - 1) st.MarkSolved(r, now);
    }

    public override bool Act(PuzzleState st, RoomPlan room, int r, PuzzleAction action, int a, int b, float now)
    {
        if (action != PuzzleAction.Reset) return false;
        st.SetR(room, PuzzleState.RA, 0);
        st.SetR(room, PuzzleState.RB, -1);
        st.SetR(room, PuzzleState.RResets, st.Get(room, PuzzleState.RResets) + 1);
        return true;
    }

    public override async Task<string?> Solve(IPuzzleDriver d, RoomPlan room, int r)
    {
        Data l = room.Layout<Data>();
        List<int>? path = Solve(l, d.State.Get(room, PuzzleState.RA), d.State.Get(room, PuzzleState.RB));
        if (path is null) return "no path through every tile";
        if (l.Cells.Length < 6) return "too few tiles";
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
        }
        // The head marker: a small flame over the last tile lit.
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
        v.Head.Visible = head >= 0 && !solved;
        if (head >= 0) v.Head.Position = room.World(l.Cells[head]) + Vector3.Up * (Mathf.Sin(t * 5f) * 0.12f);
    }

    public override void Sounds(object view, RoomPlan room, Func<int, int> was, Func<int, int> now)
    {
        int gained = now(PuzzleState.RA) & ~was(PuzzleState.RA);
        if (gained != 0) AudioDirector.PlayPuzzle(PuzzleSound.Bell, 1f + 0.06f * PuzzleSolver.PopCount(now(PuzzleState.RA)));
        else if (now(PuzzleState.RResets) != was(PuzzleState.RResets)) AudioDirector.PlayPuzzle(PuzzleSound.Wrong);
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
        => Meta.Loc.F(LitLine, PuzzleSolver.PopCount(map.State.Get(room, PuzzleState.RA)), room.Layout<Data>().Cells.Length);

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
            return head < 0 ? t == l.Start : Adjacent(l.Cells[head], c);
        }
        return PuzzleBot.WalkGrid(room, me, target, false, Avoid);
    }

    public override void Glyph(CanvasItem ci, Vector2 at, float size, Color c)
    {
        float h = size * 0.6f, w = Mathf.Max(1f, size * 0.16f);
        ci.DrawPolyline(new[] { at + new Vector2(-h, h), at + new Vector2(-h, -h), at + new Vector2(0f, -h), at + new Vector2(0f, h), at + new Vector2(h, h), at + new Vector2(h, -h) }, c, w);
    }
}
