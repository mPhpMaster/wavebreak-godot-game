using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Audio;
using Wavebreak.Core;

namespace Wavebreak.Game.Puzzle.Kinds;

/// <summary>
/// Ice Hall: the puzzle floor is ice and a rune stone stands on it. Push the
/// stone (walk into it from the next cell, as with a crate) and it slides
/// until something stops it: a pillar, another stone, the reset post, the
/// grid's frosted rim or the cell in front of a door. Rest it on the numbered
/// sockets in order (1, then 2...): the stone can only stop where something
/// stands in its way, so each socket is a chain of stops.
///
/// What else is on the ice, by difficulty:
/// <list type="bullet">
/// <item><b>Arrows</b> (from 1): a sliding stone that crosses an arrow tile
/// turns the arrow's way (and stops on the arrow if that way is blocked).</item>
/// <item><b>Cracked ice</b> (from 0): once a stone has slid over or off a
/// cracked tile it breaks into a ridge of ice that stops stones for good
/// (players step over it). A wrong push can wall the way off: the post
/// resets the hall.</item>
/// <item><b>Ice blocks</b> (from 2): plain blocks that slide by the same
/// rules. They are the stoppers you move into place, or the ones in the way.</item>
/// </list>
/// Difficulty scales the sockets (2 to 4), cracks (1 to 5), arrows (0 to 3),
/// blocks (0 to 1), pillars and the fewest pushes the room needs (4 to 14).
///
/// State: RA rune stone cell; RB, RX ice block cells (-1 for none); RD
/// broken cracks (bits); RY sockets done; RC pushes made; RResets resets.
/// Solver: breadth-first search over (stone cells, broken cracks, sockets
/// done, the player's walkable region), fewest pushes first, the region a
/// bit-board flood (<see cref="GridBits"/>).
/// </summary>
public sealed class GlideKind : PuzzleKind
{
    /// <summary>One push: which stone (0 is the rune stone, then the blocks),
    /// where to stand, and which way.</summary>
    public readonly record struct Move(int Stone, int Stand, int Side);

    public sealed class Data
    {
        public int Stone = -1, ResetCell = -1, Pushes;
        public int[] Blocks = Array.Empty<int>();
        public int[] Sockets = Array.Empty<int>();
        public int[] Cracks = Array.Empty<int>();
        public int[] Arrows = Array.Empty<int>();
        public int[] ArrowDirs = Array.Empty<int>();
        public RuneSymbol Rune;
        // Per cell: which crack (or -1) and which way its arrow points (or -1).
        public int[] CrackAt = Array.Empty<int>();
        public int[] ArrowAt = Array.Empty<int>();
        // The last plan asked for (the bot and the hint ask every frame).
        public long CacheKey = -1;
        public List<Move>? CachePlan;

        public void Index()
        {
            CrackAt = new int[RoomPlan.N * RoomPlan.N];
            ArrowAt = new int[RoomPlan.N * RoomPlan.N];
            Array.Fill(CrackAt, -1);
            Array.Fill(ArrowAt, -1);
            for (int i = 0; i < Cracks.Length; i++) CrackAt[Cracks[i]] = i;
            for (int i = 0; i < Arrows.Length; i++) ArrowAt[Arrows[i]] = ArrowDirs[i];
            CacheKey = -1;
            CachePlan = null;
        }
    }

    private sealed class View
    {
        public readonly List<StaticBody3D> Bodies = new();
        public readonly List<Node3D> Meshes = new();
        public readonly List<StandardMaterial3D> SocketMats = new();
        public readonly List<Node3D> SocketDiscs = new();
        public Node3D SocketRune = null!;
        public Node3D Top = null!;
        public readonly List<Node3D> CrackTiles = new();
        public readonly List<Node3D> Ridges = new();
        public float PushHeld, PushCooldown;
    }

    private const string VerbReset = "Reset the hall";
    private const string PushesLine = "Sockets: {0}/{1}  ·  pushes: {2}  ·  fewest: {3}";

    public override string Id => "glide";
    public override string Title => "Ice Hall";
    public override string Objective => "Slide the rune stone onto its sockets in number order: it slides until something stops it. Arrows turn it, cracked ice becomes a ridge once crossed, and ice blocks slide too.";
    public override string Hint => "Hint: push the marked stone the way the arrow points. The post resets the hall.";
    public override IEnumerable<string> Strings => new[] { VerbReset, PushesLine };
    public override HallStyle Style => HallStyle.Frost;
    public override float ParSeconds => 55f;

    private static readonly Color Ice = new(0.78f, 0.90f, 0.98f, 0.55f);
    private static readonly Color StoneTint = new(0.36f, 0.50f, 0.78f);
    private static readonly Color BlockTint = new(0.80f, 0.92f, 1f);
    private static readonly Color RuneGlow = new(0.55f, 0.85f, 1f);
    private static readonly Color CrackTint = new(0.62f, 0.74f, 0.86f);

    private const int SearchCap = 40000;

    // ------------------------------------------------------------- data

    public override void Generate(RoomPlan room, Rng rng, int d)
    {
        var l = new Data();
        room.Data = l;
        d = Math.Clamp(d, 0, 5);
        int sockets = new[] { 2, 2, 3, 3, 3, 4 }[d];
        int cracks = new[] { 1, 2, 3, 3, 4, 5 }[d];
        int arrows = new[] { 0, 1, 1, 2, 2, 3 }[d];
        int blocks = new[] { 0, 0, 1, 1, 1, 1 }[d];
        int lo = new[] { 4, 6, 8, 10, 12, 14 }[d], hi = lo + 10;

        Data? best = null;
        bool[]? bestPillars = null;
        for (int attempt = 0; attempt < 70; attempt++)
        {
            if (attempt == 45) lo = Math.Max(3, lo - 3);
            PuzzleGen.Clear(room);
            var t = new Data { ResetCell = PuzzleGen.Corner(rng.RangeInt(0, 4)), Rune = (RuneSymbol)rng.RangeInt(0, 6) };
            room.Fixed[t.ResetCell] = true;
            PuzzleGen.Scatter(room, rng, rng.RangeInt(5, 8 + d));
            if (!PuzzleSolver.DoorsConnected(room)) continue;
            List<int> free = PuzzleGen.FreeInner(room);
            if (free.Count < 2 + blocks + cracks + arrows + sockets) continue;
            t.Stone = PuzzleGen.Take(rng, free);
            t.Blocks = new int[blocks];
            for (int i = 0; i < blocks; i++) t.Blocks[i] = PuzzleGen.Take(rng, free);
            t.Cracks = new int[cracks];
            for (int i = 0; i < cracks; i++) t.Cracks[i] = PuzzleGen.Take(rng, free);
            t.Arrows = new int[arrows];
            t.ArrowDirs = new int[arrows];
            for (int i = 0; i < arrows; i++)
            {
                t.Arrows[i] = PuzzleGen.Take(rng, free);
                t.ArrowDirs[i] = rng.RangeInt(0, 4);
            }
            t.Index();
            // Sockets along a long run of the stone: wherever it rests on the
            // way to a deep state, in order, so the room is solvable by
            // construction; the search below then measures the real fewest.
            List<int>? trail = DeepTrail(room, t, rng, lo);
            if (trail is null) continue;
            var stops = new List<int>();
            foreach (int c in trail)
                if (c != t.Stone && !room.NearDoor(c) && t.CrackAt[c] < 0 && t.ArrowAt[c] < 0 && !stops.Contains(c)) stops.Add(c);
                else if (stops.Contains(c)) { stops.Remove(c); stops.Add(c); }
            if (stops.Count < sockets) continue;
            t.Sockets = new int[sockets];
            // Spread over the trail, the last socket its end.
            for (int i = 0; i < sockets; i++) t.Sockets[i] = stops[(int)Math.Round((i + 1) * (stops.Count - 1) / (double)sockets)];
            bool distinct = true;
            for (int i = 1; i < sockets; i++) distinct &= Array.IndexOf(t.Sockets, t.Sockets[i]) == i;
            if (!distinct) continue;
            List<Move>? moves = Search(room, t, Start(t), 0, 0, room.EntryCell, SearchCap);
            if (moves is null || moves.Count < 2) continue;
            t.Pushes = moves.Count;
            if (best is null || Score(t.Pushes, lo, hi) > Score(best.Pushes, lo, hi))
            {
                best = t;
                bestPillars = (bool[])room.Pillar.Clone();
            }
            if (t.Pushes >= lo && t.Pushes <= hi) break;
        }
        if (best is not null)
        {
            PuzzleGen.Clear(room);
            Array.Copy(bestPillars!, room.Pillar, room.Pillar.Length);
            room.Fixed[best.ResetCell] = true;
            room.Data = best;
            best.Index();
            return;
        }
        // Fallback: a clear floor, two pillars, two sockets.
        PuzzleGen.Clear(room);
        l.ResetCell = PuzzleGen.Corner(0);
        room.Fixed[l.ResetCell] = true;
        room.Pillar[RoomPlan.CellOf(6, 2)] = true;
        room.Pillar[RoomPlan.CellOf(2, 5)] = true;
        l.Stone = RoomPlan.CellOf(2, 2);
        l.Sockets = new[] { RoomPlan.CellOf(5, 2), RoomPlan.CellOf(5, 7) };
        l.Index();
        l.Pushes = Search(room, l, Start(l), 0, 0, room.EntryCell, SearchCap)?.Count ?? 2;
    }

    private static int Score(int pushes, int lo, int hi) => pushes > hi ? 1000 - pushes : pushes >= lo ? 2000 + pushes : pushes;

    private static int[] Start(Data l)
    {
        var s = new int[1 + l.Blocks.Length];
        s[0] = l.Stone;
        Array.Copy(l.Blocks, 0, s, 1, l.Blocks.Length);
        return s;
    }

    public override void InitState(PuzzleState st, RoomPlan room)
    {
        Data l = room.Layout<Data>();
        st.V[room.Slot + PuzzleState.RA] = l.Stone;
        st.V[room.Slot + PuzzleState.RB] = l.Blocks.Length > 0 ? l.Blocks[0] : -1;
        st.V[room.Slot + PuzzleState.RX] = l.Blocks.Length > 1 ? l.Blocks[1] : -1;
    }

    private static readonly int[] StoneSlots = { PuzzleState.RA, PuzzleState.RB, PuzzleState.RX };

    public static int[] Stones(PuzzleState st, RoomPlan room)
    {
        var s = new int[1 + room.Layout<Data>().Blocks.Length];
        for (int i = 0; i < s.Length; i++) s[i] = st.Get(room, StoneSlots[i]);
        return s;
    }

    // ---------------------------------------------------------- physics

    private static bool Blocked(RoomPlan room, Data l, int[] stones, int self, int cell, int broken)
    {
        if (cell < 0 || PuzzleSolver.Solid(room, cell) || room.CrateForbidden(cell)) return true;
        for (int i = 0; i < stones.Length; i++) if (i != self && stones[i] == cell) return true;
        int crack = l.CrackAt[cell];
        return crack >= 0 && (broken >> crack & 1) != 0;
    }

    /// <summary>Where stone <paramref name="self"/> stops when pushed towards
    /// <paramref name="side"/>: it turns on arrows, and cracks it leaves break
    /// (into <paramref name="broken"/>). Returns its own cell when it cannot move.</summary>
    public static int Slide(RoomPlan room, Data l, int[] stones, int self, int side, ref int broken)
    {
        int cur = stones[self], dir = side, nb = broken;
        Span<bool> seen = stackalloc bool[RoomPlan.N * RoomPlan.N * 4];
        for (int guard = 0; guard < 400; guard++)
        {
            int next = PuzzleSolver.Step(cur, dir);
            if (Blocked(room, l, stones, self, next, nb)) break;
            if (l.CrackAt[cur] >= 0) nb |= 1 << l.CrackAt[cur];
            cur = next;
            if (l.ArrowAt[cur] >= 0) dir = l.ArrowAt[cur];
            if (seen[cur * 4 + dir]) break;
            seen[cur * 4 + dir] = true;
        }
        if (cur == stones[self]) return cur;
        broken = nb;
        return cur;
    }

    private static UInt128 Free(UInt128 open, int[] stones)
    {
        UInt128 f = open;
        foreach (int s in stones) if (s >= 0) f &= ~GridBits.Bit(s);
        return f;
    }

    private static long Key(int[] stones, int broken, int done, int region)
    {
        long k = region & 127;
        k |= (long)(done & 7) << 7;
        k |= (long)(broken & 1023) << 10;
        for (int i = 0; i < stones.Length; i++) k |= (long)(stones[i] & 127) << (20 + 7 * i);
        return k;
    }

    private readonly record struct Node(int[] Stones, int Broken, int Done, int Player, long Key);

    /// <summary>
    /// The fewest pushes from this state to the last socket, or null (none,
    /// or more than <paramref name="cap"/> states searched). With no sockets
    /// left to find it is the whole tree (see <see cref="DeepTrail"/>).
    /// </summary>
    public static List<Move>? Search(RoomPlan room, Data l, int[] stones, int broken, int done, int player, int cap)
    {
        if (done >= l.Sockets.Length) return new List<Move>();
        UInt128 open = GridBits.Open(room);
        UInt128 reach0 = GridBits.Flood(player, Free(open, stones));
        if (reach0 == UInt128.Zero) return null;
        long k0 = Key(stones, broken, done, GridBits.Lowest(reach0));
        var parent = new Dictionary<long, (long Prev, Move Move)> { [k0] = (-1, default) };
        var queue = new Queue<Node>();
        queue.Enqueue(new Node(stones, broken, done, player, k0));
        while (queue.Count > 0 && parent.Count < cap)
        {
            Node n = queue.Dequeue();
            UInt128 reach = GridBits.Flood(n.Player, Free(open, n.Stones));
            for (int i = 0; i < n.Stones.Length; i++)
                for (int side = 0; side < 4; side++)
                {
                    int stand = PuzzleSolver.Step(n.Stones[i], (side + 2) % 4);
                    if (stand < 0 || !GridBits.Has(reach, stand)) continue;
                    int nb = n.Broken;
                    int to = Slide(room, l, n.Stones, i, side, ref nb);
                    if (to == n.Stones[i]) continue;
                    var ns = (int[])n.Stones.Clone();
                    ns[i] = to;
                    int nd = n.Done + (i == 0 && to == l.Sockets[n.Done] ? 1 : 0);
                    int region = GridBits.Lowest(GridBits.Flood(stand, Free(open, ns)));
                    long nk = Key(ns, nb, nd, region);
                    if (parent.ContainsKey(nk)) continue;
                    var move = new Move(i, stand, side);
                    parent[nk] = (n.Key, move);
                    if (nd >= l.Sockets.Length)
                    {
                        var moves = new List<Move>();
                        for (long c = nk; parent[c].Prev >= 0; c = parent[c].Prev) moves.Add(parent[c].Move);
                        moves.Reverse();
                        return moves;
                    }
                    queue.Enqueue(new Node(ns, nb, nd, stand, nk));
                }
        }
        return null;
    }

    /// <summary>The rune stone's resting cells on the way to one of the
    /// deepest states a breadth-first search over every push reaches (at
    /// least <paramref name="want"/> pushes deep when it can), or null.</summary>
    private static List<int>? DeepTrail(RoomPlan room, Data l, Rng rng, int want)
    {
        UInt128 open = GridBits.Open(room);
        int[] stones = Start(l);
        UInt128 reach0 = GridBits.Flood(room.EntryCell, Free(open, stones));
        if (reach0 == UInt128.Zero) return null;
        long k0 = Key(stones, 0, 0, GridBits.Lowest(reach0));
        var parent = new Dictionary<long, (long Prev, int Stone, int Depth)> { [k0] = (-1, l.Stone, 0) };
        var queue = new Queue<Node>();
        queue.Enqueue(new Node(stones, 0, 0, room.EntryCell, k0));
        var deepest = new List<long>();
        int maxDepth = 0;
        while (queue.Count > 0 && parent.Count < 12000)
        {
            Node n = queue.Dequeue();
            int depth = parent[n.Key].Depth;
            if (depth >= want + 4) break;
            UInt128 reach = GridBits.Flood(n.Player, Free(open, n.Stones));
            for (int i = 0; i < n.Stones.Length; i++)
                for (int side = 0; side < 4; side++)
                {
                    int stand = PuzzleSolver.Step(n.Stones[i], (side + 2) % 4);
                    if (stand < 0 || !GridBits.Has(reach, stand)) continue;
                    int nb = n.Broken;
                    int to = Slide(room, l, n.Stones, i, side, ref nb);
                    if (to == n.Stones[i]) continue;
                    var ns = (int[])n.Stones.Clone();
                    ns[i] = to;
                    long nk = Key(ns, nb, 0, GridBits.Lowest(GridBits.Flood(stand, Free(open, ns))));
                    if (parent.ContainsKey(nk)) continue;
                    parent[nk] = (n.Key, ns[0], depth + 1);
                    queue.Enqueue(new Node(ns, nb, 0, stand, nk));
                    // Only states whose stone rests somewhere a socket may go.
                    if (ns[0] == l.Stone || room.NearDoor(ns[0]) || l.CrackAt[ns[0]] >= 0 || l.ArrowAt[ns[0]] >= 0) continue;
                    if (depth + 1 > maxDepth && maxDepth < want + 4)
                    {
                        maxDepth = depth + 1;
                        deepest.Clear();
                    }
                    if (depth + 1 == maxDepth) deepest.Add(nk);
                }
        }
        if (deepest.Count == 0) return null;
        var trail = new List<int>();
        for (long c = deepest[rng.RangeInt(0, deepest.Count)]; c >= 0; c = parent[c].Prev) trail.Add(parent[c].Stone);
        trail.Reverse();
        return trail;
    }

    /// <summary>The plan from the room as it stands, for a player at
    /// <paramref name="player"/> (cached: the bot and the hint ask often).</summary>
    private static List<Move>? Plan(PuzzleState st, RoomPlan room, int player)
    {
        Data l = room.Layout<Data>();
        int[] stones = Stones(st, room);
        int broken = st.Get(room, PuzzleState.RD), done = st.Get(room, PuzzleState.RY);
        UInt128 reach = GridBits.Flood(player, Free(GridBits.Open(room), stones));
        long key = Key(stones, broken, done, GridBits.Lowest(reach));
        if (key == l.CacheKey) return l.CachePlan;
        l.CacheKey = key;
        l.CachePlan = Search(room, l, stones, broken, done, player, SearchCap);
        return l.CachePlan;
    }

    public override bool Act(PuzzleState st, RoomPlan room, int r, PuzzleAction action, int a, int b, float now)
    {
        Data l = room.Layout<Data>();
        switch (action)
        {
            case PuzzleAction.Push:
            {
                int[] stones = Stones(st, room);
                if (b is < 0 or > 3 || a < 0 || a >= stones.Length) return true;
                int broken = st.Get(room, PuzzleState.RD);
                int to = Slide(room, l, stones, a, b, ref broken);
                if (to == stones[a]) return true;
                st.SetR(room, StoneSlots[a], to);
                st.SetR(room, PuzzleState.RD, broken);
                st.SetR(room, PuzzleState.RC, st.Get(room, PuzzleState.RC) + 1);
                int done = st.Get(room, PuzzleState.RY);
                if (a == 0 && done < l.Sockets.Length && to == l.Sockets[done])
                {
                    st.SetR(room, PuzzleState.RY, done + 1);
                    if (done + 1 >= l.Sockets.Length) st.MarkSolved(r, now);
                }
                return true;
            }
            case PuzzleAction.Reset:
                st.SetR(room, PuzzleState.RA, l.Stone);
                for (int i = 0; i < l.Blocks.Length; i++) st.SetR(room, StoneSlots[i + 1], l.Blocks[i]);
                st.SetR(room, PuzzleState.RD, 0);
                st.SetR(room, PuzzleState.RY, 0);
                st.SetR(room, PuzzleState.RResets, st.Get(room, PuzzleState.RResets) + 1);
                return true;
        }
        return false;
    }

    public override async Task<string?> Solve(IPuzzleDriver d, RoomPlan room, int r)
    {
        List<Move>? moves = Plan(d.State, room, room.EntryCell);
        if (moves is null)
        {
            await d.Ask(PuzzleAction.Reset, 0);
            moves = Plan(d.State, room, room.EntryCell);
        }
        if (moves is null) return "no way to the sockets";
        if (moves.Count < 2) return "one push solves it";
        foreach (Move m in moves)
        {
            await d.Stand(room.World(m.Stand));
            await d.Ask(PuzzleAction.Push, m.Stone, m.Side);
            await d.Wait(0.3f);
        }
        return null;
    }

    // ------------------------------------------------------------ scene

    public override object Build(PieceKit kit, RoomPlan room)
    {
        Data l = room.Layout<Data>();
        var v = new View();
        // The ice: a glossy sheet over the grid, and a frosted rim round it
        // (the stones stop at the rim; players step over it).
        float g = RoomPlan.N * RoomPlan.Cell * 0.5f;
        kit.Put(PieceKit.Mesh(new BoxMesh { Size = new Vector3(g * 2f, 0.03f, g * 2f) }, room.Centre + Vector3.Up * 0.05f, new StandardMaterial3D
        {
            AlbedoColor = Ice,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Roughness = 0.05f,
            Metallic = 0.2f,
        }, false));
        var rim = PieceKit.Flat(new Color(0.90f, 0.96f, 1f));
        foreach (int s in new[] { -1, 1 })
        {
            kit.Put(PieceKit.Mesh(new BoxMesh { Size = new Vector3(0.25f, 0.22f, g * 2f + 0.5f) }, room.Centre + new Vector3(s * (g + 0.12f), 0.11f, 0f), rim, false));
            kit.Put(PieceKit.Mesh(new BoxMesh { Size = new Vector3(g * 2f + 0.5f, 0.22f, 0.25f) }, room.Centre + new Vector3(0f, 0.11f, s * (g + 0.12f)), rim, false));
        }

        // Sockets: sunken rings, each with its number standing over it.
        for (int i = 0; i < l.Sockets.Length; i++)
        {
            Vector3 so = room.World(l.Sockets[i]);
            var mat = PieceKit.Flat(PieceKit.BronzeDark);
            var ring = PieceKit.Mesh(new TorusMesh { InnerRadius = 0.6f, OuterRadius = 0.85f, Rings = 28, RingSegments = 8 }, so + Vector3.Up * 0.08f, mat, false);
            kit.Put(ring);
            v.SocketMats.Add(mat);
            var disc = PieceKit.Mesh(new CylinderMesh { TopRadius = 0.62f, BottomRadius = 0.62f, Height = 0.04f, RadialSegments = 20 }, so + Vector3.Up * 0.07f, PieceKit.Glow(RuneGlow.Darkened(0.35f), 0.8f), false);
            kit.Put(disc);
            v.SocketDiscs.Add(disc);
            kit.Label((i + 1).ToString(), so + Vector3.Up * 1.0f, 96);
        }
        // The stone's rune floats over the socket it wants next.
        v.SocketRune = PieceKit.Symbol(l.Rune, PieceKit.Glow(RuneGlow, 2.2f));
        v.SocketRune.Position = room.World(l.Sockets[0]) + Vector3.Up * 2.4f;
        v.SocketRune.Scale = Vector3.One * 1.1f;
        kit.Put(v.SocketRune);

        // Cracked tiles (a pale slab scored with cracks) and the ridge each becomes.
        var crackLine = PieceKit.Flat(new Color(0.25f, 0.32f, 0.42f));
        foreach (int c in l.Cracks)
        {
            Vector3 at = room.World(c);
            var tile = new Node3D { Position = at + Vector3.Up * 0.07f };
            kit.Put(tile);
            tile.AddChild(PieceKit.Mesh(new BoxMesh { Size = new Vector3(1.8f, 0.03f, 1.8f) }, Vector3.Zero, PieceKit.Flat(CrackTint), false));
            foreach ((float x, float z, float yaw, float len) in new[] { (0f, 0f, 0.5f, 1.7f), (-0.3f, 0.35f, -0.9f, 0.9f), (0.35f, -0.3f, 1.9f, 0.8f), (0.2f, 0.45f, 0.1f, 0.6f) })
            {
                var line = PieceKit.Mesh(new BoxMesh { Size = new Vector3(len, 0.04f, 0.07f) }, new Vector3(x, 0.02f, z), crackLine, false);
                line.Rotation = new Vector3(0f, yaw, 0f);
                tile.AddChild(line);
            }
            v.CrackTiles.Add(tile);
            var ridge = new Node3D { Position = at, Visible = false };
            kit.Put(ridge);
            var shard = PieceKit.Flat(BlockTint.Darkened(0.1f));
            foreach ((float x, float z, float h, float yaw) in new[] { (-0.45f, -0.3f, 0.55f, 0.3f), (0.4f, -0.4f, 0.4f, 1.1f), (0f, 0.35f, 0.7f, 0.7f), (0.5f, 0.45f, 0.35f, 0.2f), (-0.5f, 0.5f, 0.3f, 1.4f) })
            {
                var m = PieceKit.Mesh(new BoxMesh { Size = new Vector3(0.6f, h, 0.5f) }, new Vector3(x, h * 0.5f, z), shard);
                m.Rotation = new Vector3(0.15f, yaw, -0.1f);
                ridge.AddChild(m);
            }
            v.Ridges.Add(ridge);
        }
        // Arrows: a flat shaft and head on the ice, pointing the way a stone turns.
        var arrowMat = PieceKit.Glow(new Color(0.85f, 0.95f, 1f), 1.1f);
        for (int i = 0; i < l.Arrows.Length; i++)
        {
            int dir = l.ArrowDirs[i];
            var node = new Node3D { Position = room.World(l.Arrows[i]) + Vector3.Up * 0.08f, Rotation = new Vector3(0f, Mathf.Atan2(RoomPlan.Dx[dir], RoomPlan.Dz[dir]), 0f) };
            kit.Put(node);
            node.AddChild(PieceKit.Mesh(new BoxMesh { Size = new Vector3(0.3f, 0.04f, 0.9f) }, new Vector3(0f, 0f, -0.25f), arrowMat, false));
            var head = PieceKit.Mesh(new CylinderMesh { TopRadius = 0.55f, BottomRadius = 0.55f, Height = 0.04f, RadialSegments = 3 }, new Vector3(0f, 0f, 0.35f), arrowMat, false);
            head.Rotation = new Vector3(0f, Mathf.Pi * 0.5f, 0f);
            node.AddChild(head);
        }

        // The stones: the rune stone (its rune on every side) and plain ice blocks.
        int[] start = Start(l);
        for (int i = 0; i < start.Length; i++)
        {
            var body = new StaticBody3D { Name = i == 0 ? "RuneStone" : "IceBlock", Position = room.World(start[i]) + Vector3.Up * 0.8f, CollisionLayer = (1u << 6) | PuzzleMap.PieceLayer, CollisionMask = 0 };
            body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(1.6f, 1.6f, 1.6f) } });
            kit.Put(body);
            v.Bodies.Add(body);
            var mesh = new Node3D { Name = i == 0 ? "RuneStoneMesh" : "IceBlockMesh", Position = body.Position };
            kit.Put(mesh);
            v.Meshes.Add(mesh);
            if (i > 0)
            {
                mesh.AddChild(PieceKit.Mesh(new BoxMesh { Size = new Vector3(1.5f, 1.5f, 1.5f) }, Vector3.Zero, new StandardMaterial3D
                {
                    AlbedoColor = new Color(BlockTint, 0.8f),
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    Roughness = 0.1f,
                }));
                mesh.AddChild(PieceKit.Mesh(new BoxMesh { Size = new Vector3(1.0f, 1.56f, 1.0f) }, Vector3.Zero, PieceKit.Flat(BlockTint.Darkened(0.2f))));
                continue;
            }
            mesh.AddChild(PieceKit.Mesh(new BoxMesh { Size = new Vector3(1.56f, 1.56f, 1.56f) }, Vector3.Zero, PieceKit.Flat(StoneTint)));
            mesh.AddChild(PieceKit.Mesh(new BoxMesh { Size = new Vector3(1.64f, 0.16f, 1.64f) }, Vector3.Up * 0.62f, PieceKit.Flat(StoneTint.Darkened(0.25f))));
            Node3D top = PieceKit.Symbol(l.Rune, PieceKit.Glow(RuneGlow, 2.6f));
            top.Position = Vector3.Up * 1.35f;
            top.Scale = Vector3.One * 0.9f;
            mesh.AddChild(top);
            v.Top = top;
            for (int side = 0; side < 4; side++)
            {
                Node3D sym = PieceKit.Symbol(l.Rune, PieceKit.Glow(RuneGlow, 2.6f));
                var dir = new Vector3(RoomPlan.Dx[side], 0f, RoomPlan.Dz[side]);
                sym.Position = dir * 0.8f;
                sym.Rotation = new Vector3(0f, Mathf.Atan2(dir.X, dir.Z), 0f);
                sym.Scale = Vector3.One * 0.7f;
                mesh.AddChild(sym);
            }
        }
        kit.ResetPost(room.World(l.ResetCell));
        return v;
    }

    public override void Sync(PieceKit kit, object view, RoomPlan room, PuzzleState st, float t, float dt, bool changed)
    {
        var v = (View)view;
        Data l = room.Layout<Data>();
        int[] stones = Stones(st, room);
        for (int i = 0; i < stones.Length && i < v.Bodies.Count; i++)
        {
            Vector3 target = room.World(stones[i]) + Vector3.Up * 0.8f;
            if (changed) v.Bodies[i].Position = target;
            // Slides: quick, then eases into the stop.
            float dist = v.Meshes[i].Position.DistanceTo(target);
            v.Meshes[i].Position = v.Meshes[i].Position.MoveToward(target, dt * Mathf.Clamp(dist * 6f, 3f, 14f));
        }
        bool solved = st.Solved(room);
        int done = solved ? l.Sockets.Length : st.Get(room, PuzzleState.RY);
        for (int i = 0; i < v.SocketMats.Count; i++)
        {
            PieceKit.Lit(v.SocketMats[i], i < done, RuneGlow, PieceKit.BronzeDark, 2f);
            // A done socket's disc sinks out of sight.
            Vector3 p = v.SocketDiscs[i].Position;
            v.SocketDiscs[i].Position = new Vector3(p.X, Mathf.MoveToward(p.Y, i < done ? -0.2f : 0.07f, dt), p.Z);
        }
        Vector3 want = room.World(l.Sockets[Math.Min(done, l.Sockets.Length - 1)]) + Vector3.Up * 2.4f;
        v.SocketRune.Position = v.SocketRune.Position.MoveToward(want, dt * 8f);
        v.SocketRune.RotateY(dt * (solved ? 3f : 0.8f));
        v.Top.RotateY(dt * 0.8f);
        int broken = st.Get(room, PuzzleState.RD);
        for (int i = 0; i < v.Ridges.Count; i++)
        {
            bool b = (broken >> i & 1) != 0;
            v.Ridges[i].Visible = b;
            v.CrackTiles[i].Visible = !b;
        }
    }

    public override void Sounds(object view, RoomPlan room, Func<int, int> was, Func<int, int> now)
    {
        if (now(PuzzleState.RY) > was(PuzzleState.RY)) AudioDirector.PlayPuzzle(PuzzleSound.Bell, 1.2f);
        else if ((now(PuzzleState.RD) & ~was(PuzzleState.RD)) != 0) AudioDirector.PlayPuzzle(PuzzleSound.Wrong, 1.4f);
        else if (was(PuzzleState.RA) != now(PuzzleState.RA) || was(PuzzleState.RB) != now(PuzzleState.RB) || was(PuzzleState.RX) != now(PuzzleState.RX))
            AudioDirector.PlayPuzzle(PuzzleSound.Thunk, 0.8f);
    }

    public override void Offer(PuzzleMap map, RoomPlan room, FocusOffer offer)
        => offer(PuzzleAction.Reset, 0, room.World(room.Layout<Data>().ResetCell), VerbReset);

    /// <summary>Standing in the cell next to a stone and walking into it for
    /// a moment pushes it (and it slides).</summary>
    public override void LocalStep(PuzzleMap map, object view, Player me, RoomPlan room, int r, float dt, bool control)
    {
        var v = (View)view;
        v.PushCooldown = Mathf.Max(0f, v.PushCooldown - dt);
        int side = control ? CratesKind.PushSide(me.MoveWish) : -1;
        int[] stones = Stones(map.State, room);
        int hit = -1;
        if (side >= 0 && room.OnGrid(me.GlobalPosition))
            for (int i = 0; i < stones.Length && hit < 0; i++)
            {
                Vector3 d = room.World(stones[i]) - me.GlobalPosition;
                float along = d.X * RoomPlan.Dx[side] + d.Z * RoomPlan.Dz[side];
                float across = Mathf.Abs(d.X * RoomPlan.Dz[side] - d.Z * RoomPlan.Dx[side]);
                if (along > 0.6f && along < 1.9f && across < 0.75f) hit = i;
            }
        if (hit < 0 || v.PushCooldown > 0f)
        {
            v.PushHeld = 0f;
            return;
        }
        v.PushHeld += dt;
        if (v.PushHeld < 0.16f) return;
        v.PushHeld = 0f;
        v.PushCooldown = 0.6f;
        map.Ask(PuzzleAction.Push, r, hit, side);
    }

    private static int From(RoomPlan room, Vector3? me, int[] stones)
    {
        int from = me is { } at ? room.CellAt(at) : room.EntryCell;
        if (from < 0 || PuzzleSolver.Solid(room, from) || Array.IndexOf(stones, from) >= 0) from = room.EntryCell;
        return from;
    }

    public override PuzzleHint? HintAt(PuzzleState st, RoomPlan room, Vector3? me, float now)
    {
        int[] stones = Stones(st, room);
        List<Move>? moves = Plan(st, room, From(room, me, stones));
        if (moves is null || moves.Count == 0) return new PuzzleHint(room.World(room.Layout<Data>().ResetCell), "!");
        return new PuzzleHint(room.World(stones[moves[0].Stone]) + Vector3.Up * 0.6f, "!", moves[0].Side);
    }

    public override string Status(PuzzleMap map, RoomPlan room)
    {
        Data l = room.Layout<Data>();
        return Meta.Loc.F(PushesLine, map.State.Get(room, PuzzleState.RY), l.Sockets.Length, map.State.Get(room, PuzzleState.RC), l.Pushes);
    }

    public override Vector3 Bot(PuzzleBot bot, RoomPlan room, Vector3 me)
    {
        int[] stones = Stones(bot.State, room);
        bool IsStone(int c) => Array.IndexOf(stones, c) >= 0;
        List<Move>? moves = Plan(bot.State, room, From(room, me, stones));
        if (moves is null) return bot.Use(room, me, room.World(room.Layout<Data>().ResetCell), PuzzleAction.Reset, 0);
        if (moves.Count == 0) return Vector3.Zero;
        return PuzzleBot.PushFrom(room, me, moves[0].Stand, moves[0].Side, IsStone);
    }

    public override void Glyph(CanvasItem ci, Vector2 at, float size, Color c)
    {
        float h = size * 0.65f;
        ci.DrawPolyline(new[] { at + new Vector2(0f, -h), at + new Vector2(h, 0f), at + new Vector2(0f, h), at + new Vector2(-h, 0f), at + new Vector2(0f, -h) }, c, Mathf.Max(1f, size * 0.14f));
    }
}
