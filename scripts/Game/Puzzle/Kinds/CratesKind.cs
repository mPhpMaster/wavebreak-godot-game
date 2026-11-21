using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Audio;
using Wavebreak.Core;

namespace Wavebreak.Game.Puzzle.Kinds;

/// <summary>
/// Weighing Room: a tight warehouse of pillars and crates (sokoban). Walking
/// into a crate along a grid line for a moment pushes it one cell; it never
/// enters the cell in front of a door; the reset post puts every crate back.
///
/// The rules that make it a plan:
/// <list type="bullet">
/// <item><b>Numbered plates, in order.</b> Only the next plate is open; the
/// later ones stay raised behind a grate and no crate can enter them. A crate
/// pushed onto the open plate sinks and locks there for good, so every placed
/// crate becomes a wall the later crates must get round.</item>
/// <item><b>Iron.</b> Round iron plates only sink under an iron crate; a
/// wooden one just rests on them. An iron crate also fits a wooden plate, so
/// spending it there leaves its iron plate empty: reset.</item>
/// <item><b>Spare crates</b> (from 3): more crates than plates. One of them
/// is in the way.</item>
/// <item><b>The star plate</b> opens last: stand on it to finish.</item>
/// </list>
/// Dead ends are real (a crate in a corner, an iron crate on the wrong plate,
/// a locked crate in the only corridor); the hint then points at the reset
/// post. Difficulty scales the plates (2 to 4), iron (0 to 2), spare crates,
/// pillars and the fewest pushes (6 to 17).
///
/// State: RA-RD crate cells; RPressed plates pressed (bits; the star plate
/// is the last bit); RResets resets.
/// Generation: a breadth-first search backwards from the solved room (pulls,
/// each checked as a legal push forwards) over (crate cells, the player's
/// walkable region); the start is one of the deepest states the player can
/// walk to from the door, so the room is solvable by construction and its
/// depth is the pushes it needs. That search is kept as a table: from any
/// state in it the next push is a look-up; elsewhere a forward search meets it.
/// </summary>
public sealed class CratesKind : PuzzleKind
{
    /// <summary>One push: the crate's cell, where to stand, which way.</summary>
    public readonly record struct Move(int From, int Stand, int Side);

    public sealed class Data
    {
        public int[] CrateStart = Array.Empty<int>();
        public bool[] Iron = Array.Empty<bool>();
        public int[] Plates = Array.Empty<int>();
        public bool[] PlateIron = Array.Empty<bool>();
        public int Star = -1;
        public int ResetCell = -1;
        public int Pushes;
        /// <summary>State key to the next push towards the solved room.</summary>
        public Dictionary<long, (long Next, Move Move)> Table = new();
        public long CacheKey = -1;
        public List<Move>? CachePlan;
    }

    private sealed class View
    {
        public readonly List<StaticBody3D> Bodies = new();
        public readonly List<Node3D> Meshes = new();
        public readonly List<StandardMaterial3D> Bands = new();
        public readonly List<PlateView> Plates = new();
        public readonly List<Node3D> Grates = new();
        public float PushHeld, PushCooldown;
    }

    private const string VerbReset = "Reset the crates";
    private const string PlatesLine = "Plates locked: {0}/{1}";

    public override string Id => "crates";
    public override string Title => "Weighing Room";
    public override string Objective => "Push crates onto the plates in number order: only the next plate is open, and a crate on it locks for good. Round iron plates need an iron crate. Then stand on the star plate.";
    public override string Hint => "Hint: the marked crate moves next, in the arrow's direction. The post resets the crates.";
    public override IEnumerable<string> Strings => new[] { VerbReset, PlatesLine };
    public override HallStyle Style => HallStyle.Workshop;
    public override float ParSeconds => 60f;

    private static readonly Color Crate = new(0.70f, 0.50f, 0.30f);
    private static readonly Color CrateBand = new(0.45f, 0.30f, 0.18f);
    private static readonly Color IronCrate = new(0.42f, 0.44f, 0.47f);
    private static readonly Color IronBand = new(0.22f, 0.22f, 0.24f);

    private const int TableCap = 7000, ForwardCap = 30000;

    // ------------------------------------------------------------- data

    public override void Generate(RoomPlan room, Rng rng, int d)
    {
        d = Math.Clamp(d, 0, 5);
        int plates = new[] { 2, 3, 3, 3, 3, 4 }[d];
        int iron = new[] { 0, 0, 1, 1, 1, 2 }[d];
        int spare = new[] { 0, 0, 0, 1, 1, 0 }[d];
        int lo = new[] { 6, 9, 11, 13, 15, 17 }[d], hi = new[] { 10, 14, 18, 22, 26, 30 }[d];
        Data? best = null;
        bool[]? bestPillars = null;
        for (int attempt = 0; attempt < 14; attempt++)
        {
            PuzzleGen.Clear(room);
            var t = new Data { ResetCell = PuzzleGen.Corner(rng.RangeInt(0, 4)) };
            room.Fixed[t.ResetCell] = true;
            PuzzleGen.Scatter(room, rng, rng.RangeInt(7 + d, 11 + 2 * d));
            List<int> free = PuzzleGen.FreeInner(room);
            if (free.Count < plates * 2 + spare + 4) continue;
            t.Plates = new int[plates];
            t.PlateIron = new bool[plates];
            for (int i = 0; i < plates; i++) t.Plates[i] = PuzzleGen.Take(rng, free);
            // Iron plates anywhere in the order (not always the first).
            var order = new List<int>();
            for (int i = 0; i < plates; i++) order.Add(i);
            for (int i = 0; i < iron; i++) t.PlateIron[PuzzleGen.Take(rng, order)] = true;
            t.Star = PuzzleGen.Take(rng, free);
            // Crates in the solved room: one on each plate, spares loose.
            int n = plates + spare;
            var solved = new int[n];
            t.Iron = new bool[n];
            for (int i = 0; i < plates; i++)
            {
                solved[i] = t.Plates[i];
                t.Iron[i] = t.PlateIron[i];
            }
            bool bad = false;
            for (int i = plates; i < n; i++)
            {
                solved[i] = PuzzleGen.Take(rng, free);
                bad |= DeadCorner(room, solved[i]);
            }
            if (bad) continue;
            // Locked crates are walls: the doors and the star plate must stay reachable.
            bool Wall(int c) => PuzzleSolver.Solid(room, c) || Array.IndexOf(t.Plates, c) >= 0;
            if (PuzzleSolver.Path(room, room.EntryCell, room.ExitCell, Wall) is null) continue;
            if (PuzzleSolver.Path(room, room.EntryCell, t.Star, Wall) is null) continue;
            t.CrateStart = solved;
            if (!Backwards(room, t, rng, lo, hi)) continue;
            if (best is null || Score(t.Pushes, lo, hi) > Score(best.Pushes, lo, hi))
            {
                best = t;
                bestPillars = (bool[])room.Pillar.Clone();
            }
            if (t.Pushes >= (attempt < 4 ? lo : lo - 3) && t.Pushes <= hi) break;
        }
        if (best is not null && best.Pushes >= 2)
        {
            PuzzleGen.Clear(room);
            Array.Copy(bestPillars!, room.Pillar, room.Pillar.Length);
            room.Fixed[best.ResetCell] = true;
            room.Data = best;
            return;
        }
        // Fallback: two crates short of their plates in clear rows.
        PuzzleGen.Clear(room);
        var l = new Data { ResetCell = PuzzleGen.Corner(0) };
        room.Data = l;
        room.Fixed[l.ResetCell] = true;
        int z = room.EntrySide == 1 || room.ExitSide == 1 ? 3 : 5;
        int z2 = z == 3 ? 2 : 6;
        l.Plates = new[] { RoomPlan.CellOf(6, z), RoomPlan.CellOf(6, z2) };
        l.PlateIron = new bool[2];
        l.Iron = new bool[2];
        l.Star = RoomPlan.CellOf(2, z2);
        l.CrateStart = new[] { RoomPlan.CellOf(6, z), RoomPlan.CellOf(6, z2) };
        Backwards(room, l, rng, 2, 99);
    }

    private static int Score(int pushes, int lo, int hi) => pushes > hi ? 1000 - pushes : pushes >= lo ? 2000 + pushes : pushes;

    /// <summary>A crate wedged between two walls at right angles can never move again.</summary>
    private static bool DeadCorner(RoomPlan room, int cell)
    {
        bool Blocked(int side)
        {
            int n = PuzzleSolver.Step(cell, side);
            return n < 0 || PuzzleSolver.Solid(room, n) || room.CrateForbidden(n);
        }
        return (Blocked(0) || Blocked(2)) && (Blocked(1) || Blocked(3));
    }

    public override void InitState(PuzzleState st, RoomPlan room)
    {
        Data l = room.Layout<Data>();
        for (int i = 0; i < l.CrateStart.Length; i++) st.V[room.Slot + PuzzleState.RA + i] = l.CrateStart[i];
    }

    public static int[] Crates(PuzzleState st, RoomPlan room)
    {
        Data l = room.Layout<Data>();
        var c = new int[l.CrateStart.Length];
        for (int i = 0; i < c.Length; i++) c[i] = st.Get(room, PuzzleState.RA + i);
        return c;
    }

    // ------------------------------------------------------------ rules

    /// <summary>How many plates are locked (in order) with the crates here.</summary>
    public static int Locked(Data l, int[] crates)
    {
        int n = 0;
        for (; n < l.Plates.Length; n++)
        {
            int at = Array.IndexOf(crates, l.Plates[n]);
            if (at < 0 || (l.PlateIron[n] && !l.Iron[at])) break;
        }
        return n;
    }

    /// <summary>Can a crate move into <paramref name="cell"/> (crates as they
    /// are, <paramref name="locked"/> plates locked)?</summary>
    public static bool CrateCanEnter(RoomPlan room, Data l, int cell, int[] crates, int locked)
    {
        if (cell < 0 || PuzzleSolver.Solid(room, cell) || room.CrateForbidden(cell) || cell == l.Star) return false;
        if (Array.IndexOf(crates, cell) >= 0) return false;
        int plate = Array.IndexOf(l.Plates, cell);
        return plate < 0 || plate == locked;   // later plates are raised
    }

    /// <summary>Push crate <paramref name="i"/> towards <paramref name="side"/>;
    /// the new crate cells, or null when it cannot move.</summary>
    public static int[]? Push(RoomPlan room, Data l, int[] crates, int i, int side)
    {
        int locked = Locked(l, crates);
        int plate = Array.IndexOf(l.Plates, crates[i]);
        if (plate >= 0 && plate < locked) return null;   // locked for good
        int target = PuzzleSolver.Step(crates[i], side);
        if (!CrateCanEnter(room, l, target, crates, locked)) return null;
        var next = (int[])crates.Clone();
        next[i] = target;
        return next;
    }

    // ----------------------------------------------------------- search

    private static UInt128 Free(UInt128 open, int[] crates)
    {
        UInt128 f = open;
        foreach (int c in crates) f &= ~GridBits.Bit(c);
        return f;
    }

    /// <summary>A state as a number: crates sorted within their kind (wood
    /// crates are alike, so are iron ones), then the player's region.</summary>
    private static long Key(Data l, int[] crates, int region)
    {
        Span<int> wood = stackalloc int[crates.Length];
        Span<int> iron = stackalloc int[crates.Length];
        int nw = 0, ni = 0;
        for (int i = 0; i < crates.Length; i++)
            if (l.Iron[i]) iron[ni++] = crates[i];
            else wood[nw++] = crates[i];
        wood[..nw].Sort();
        iron[..ni].Sort();
        long k = region & 127;
        int shift = 7;
        for (int i = 0; i < ni; i++, shift += 7) k |= (long)iron[i] << shift;
        for (int i = 0; i < nw; i++, shift += 7) k |= (long)wood[i] << shift;
        return k;
    }

    private readonly record struct Node(int[] Crates, int Player, long Key, int Depth);

    /// <summary>
    /// Breadth-first search backwards from the solved room
    /// (<see cref="Data.CrateStart"/> holds it on entry): every pull that is a
    /// legal push the other way. Fills the table and sets the start to one of
    /// the deepest states (no crate on a plate, the door's region).
    /// </summary>
    private static bool Backwards(RoomPlan room, Data l, Rng rng, int lo, int hi)
    {
        UInt128 open = GridBits.Open(room);
        int[] solved = l.CrateStart;
        // The player finishes on the star plate.
        UInt128 r0 = GridBits.Flood(l.Star, Free(open, solved));
        if (r0 == UInt128.Zero) return false;
        var table = new Dictionary<long, (long Next, Move Move)>();
        long k0 = Key(l, solved, GridBits.Lowest(r0));
        table[k0] = (-1, default);
        var queue = new Queue<Node>();
        queue.Enqueue(new Node(solved, l.Star, k0, 0));
        var deepest = new List<int[]>();
        int maxDepth = 0;
        while (queue.Count > 0 && table.Count < TableCap)
        {
            Node s = queue.Dequeue();
            if (s.Depth >= hi) break;   // deep enough: the table already holds every start's way home
            UInt128 reach = GridBits.Flood(s.Player, Free(open, s.Crates));
            for (int i = 0; i < s.Crates.Length; i++)
                for (int dir = 0; dir < 4; dir++)
                {
                    // The push dir that would have brought crate i here: it
                    // came from c, the player stood at p and ended on c.
                    int c = PuzzleSolver.Step(s.Crates[i], (dir + 2) % 4);
                    if (c < 0 || !GridBits.Has(reach, c)) continue;
                    if (room.CrateForbidden(c) || c == l.Star) continue;   // no push ever leaves a crate there
                    int p = PuzzleSolver.Step(c, (dir + 2) % 4);
                    if (p < 0 || PuzzleSolver.Solid(room, p) || Array.IndexOf(s.Crates, p) >= 0) continue;
                    var prev = (int[])s.Crates.Clone();
                    prev[i] = c;
                    // The push back must be legal: crate i not locked at c,
                    // free to enter its cell here, and no crate on a raised plate.
                    int locked = Locked(l, prev);
                    int from = Array.IndexOf(l.Plates, c);
                    if (from >= 0 && from < locked) continue;
                    if (OnClosedPlate(l, prev, locked)) continue;
                    prev[i] = -1;
                    bool legal = CrateCanEnter(room, l, s.Crates[i], prev, locked);
                    prev[i] = c;
                    if (!legal) continue;
                    UInt128 pr = GridBits.Flood(p, Free(open, prev));
                    long pk = Key(l, prev, GridBits.Lowest(pr));
                    if (table.ContainsKey(pk)) continue;
                    table[pk] = (s.Key, new Move(c, p, dir));
                    var node = new Node(prev, p, pk, s.Depth + 1);
                    queue.Enqueue(node);
                    // A start: the door's region, no crate on any plate.
                    if (node.Depth > hi || !GridBits.Has(pr, room.EntryCell) || OnAnyPlate(l, prev)) continue;
                    if (node.Depth > maxDepth)
                    {
                        maxDepth = node.Depth;
                        deepest.Clear();
                    }
                    if (node.Depth == maxDepth) deepest.Add(prev);
                }
        }
        if (deepest.Count == 0) return false;
        l.CrateStart = deepest[rng.RangeInt(0, deepest.Count)];
        l.Table = table;
        l.CacheKey = -1;
        l.CachePlan = null;
        UInt128 rs = GridBits.Flood(room.EntryCell, Free(open, l.CrateStart));
        l.Pushes = FromTable(room, l, l.CrateStart, Key(l, l.CrateStart, GridBits.Lowest(rs))).Count;
        return l.Pushes >= Math.Min(lo, 2);
    }

    private static bool OnClosedPlate(Data l, int[] crates, int locked)
    {
        for (int p = locked + 1; p < l.Plates.Length; p++) if (Array.IndexOf(crates, l.Plates[p]) >= 0) return true;
        return false;
    }

    private static bool OnAnyPlate(Data l, int[] crates)
    {
        foreach (int c in crates) if (Array.IndexOf(l.Plates, c) >= 0) return true;
        return false;
    }

    /// <summary>The table's pushes from <paramref name="key"/> (crates at
    /// <paramref name="crates"/>), up to the first moment every plate is
    /// locked with the star plate in reach (the table runs on to the exact
    /// room it was searched from, spare crates and all).</summary>
    private static List<Move> FromTable(RoomPlan room, Data l, int[] crates, long key)
    {
        var moves = new List<Move>();
        UInt128 open = GridBits.Open(room);
        int[] cur = (int[])crates.Clone();
        for (long k = key; l.Table.TryGetValue(k, out var e) && e.Next >= 0; k = e.Next)
        {
            moves.Add(e.Move);
            int i = Array.IndexOf(cur, e.Move.From);
            if (i < 0) break;
            cur[i] = PuzzleSolver.Step(e.Move.From, e.Move.Side);
            if (Locked(l, cur) == l.Plates.Length && GridBits.Has(GridBits.Flood(e.Move.From, Free(open, cur)), l.Star)) break;
        }
        return moves;
    }

    /// <summary>The pushes (crate cell, stand, side) from this state to every
    /// plate locked with the star plate reachable; null when there are none
    /// (or the search gave up: the reset post is always an answer).</summary>
    public static List<Move>? Solve(RoomPlan room, int player, int[] crates)
    {
        Data l = room.Layout<Data>();
        UInt128 open = GridBits.Open(room);
        UInt128 r0 = GridBits.Flood(player, Free(open, crates));
        if (r0 == UInt128.Zero) return null;
        long k0 = Key(l, crates, GridBits.Lowest(r0));
        if (l.Table.ContainsKey(k0)) return FromTable(room, l, crates, k0);
        if (Locked(l, crates) == l.Plates.Length && GridBits.Has(r0, l.Star)) return new List<Move>();
        var parent = new Dictionary<long, (long Prev, Move Move)> { [k0] = (-1, default) };
        var queue = new Queue<Node>();
        queue.Enqueue(new Node(crates, player, k0, 0));
        while (queue.Count > 0 && parent.Count < ForwardCap)
        {
            Node s = queue.Dequeue();
            UInt128 reach = GridBits.Flood(s.Player, Free(open, s.Crates));
            for (int i = 0; i < s.Crates.Length; i++)
                for (int side = 0; side < 4; side++)
                {
                    int stand = PuzzleSolver.Step(s.Crates[i], (side + 2) % 4);
                    if (stand < 0 || !GridBits.Has(reach, stand)) continue;
                    int[]? next = Push(room, l, s.Crates, i, side);
                    if (next is null) continue;
                    int moved = s.Crates[i];
                    UInt128 nr = GridBits.Flood(moved, Free(open, next));
                    long nk = Key(l, next, GridBits.Lowest(nr));
                    if (parent.ContainsKey(nk)) continue;
                    parent[nk] = (s.Key, new Move(moved, stand, side));
                    bool done = Locked(l, next) == l.Plates.Length && GridBits.Has(nr, l.Star);
                    if (done || l.Table.ContainsKey(nk))
                    {
                        var moves = new List<Move>();
                        for (long k = nk; parent[k].Prev >= 0; k = parent[k].Prev) moves.Add(parent[k].Move);
                        moves.Reverse();
                        if (!done) moves.AddRange(FromTable(room, l, next, nk));
                        return moves;
                    }
                    queue.Enqueue(new Node(next, moved, nk, s.Depth + 1));
                }
        }
        return null;
    }

    /// <summary>The plan from the room as it stands (cached: the bot asks every frame).</summary>
    private static List<Move>? Plan(PuzzleState st, RoomPlan room, int player)
    {
        Data l = room.Layout<Data>();
        int[] crates = Crates(st, room);
        UInt128 reach = GridBits.Flood(player, Free(GridBits.Open(room), crates));
        long key = Key(l, crates, GridBits.Lowest(reach));
        if (key == l.CacheKey) return l.CachePlan;
        l.CacheKey = key;
        l.CachePlan = Solve(room, player, crates);
        return l.CachePlan;
    }

    // ------------------------------------------------------------- host

    public override void HostStep(PuzzleState st, RoomPlan room, int r, IReadOnlyList<Vector3> players, float now)
    {
        Data l = room.Layout<Data>();
        int locked = Locked(l, Crates(st, room));
        int pressed = (1 << locked) - 1;
        bool star = false;
        foreach (Vector3 p in players) star |= PuzzleState.OnCell(room, p, l.Star);
        if (star && locked == l.Plates.Length) pressed |= 1 << l.Plates.Length;
        st.SetR(room, PuzzleState.RPressed, pressed);
        if (pressed == (1 << (l.Plates.Length + 1)) - 1) st.MarkSolved(r, now);
    }

    public override bool Act(PuzzleState st, RoomPlan room, int r, PuzzleAction action, int a, int b, float now)
    {
        Data l = room.Layout<Data>();
        switch (action)
        {
            case PuzzleAction.Push:
            {
                if (a < 0 || a >= l.CrateStart.Length || b is < 0 or > 3) return true;
                int[]? next = Push(room, l, Crates(st, room), a, b);
                if (next is not null) st.SetR(room, PuzzleState.RA + a, next[a]);
                return true;
            }
            case PuzzleAction.Reset:
                for (int i = 0; i < l.CrateStart.Length; i++) st.SetR(room, PuzzleState.RA + i, l.CrateStart[i]);
                st.SetR(room, PuzzleState.RResets, st.Get(room, PuzzleState.RResets) + 1);
                return true;
        }
        return false;
    }

    public override async Task<string?> Solve(IPuzzleDriver d, RoomPlan room, int r)
    {
        Data l = room.Layout<Data>();
        List<Move>? moves = Plan(d.State, room, room.EntryCell);
        if (moves is null)
        {
            await d.Ask(PuzzleAction.Reset, 0);
            moves = Plan(d.State, room, room.EntryCell);
        }
        if (moves is null) return "no crate solution";
        if (moves.Count < 1) return "no push needed";
        foreach (Move m in moves)
        {
            if (d.State.Solved(r)) return null;
            int crate = Array.IndexOf(Crates(d.State, room), m.From);
            if (crate < 0) return "the plan lost a crate";
            await d.Stand(room.World(m.Stand));
            await d.Ask(PuzzleAction.Push, crate, m.Side);
        }
        await d.Wait(0.2f);
        await d.Stand(room.World(l.Star));
        await d.Wait(0.2f);
        return null;
    }

    // ------------------------------------------------------------ scene

    public override object Build(PieceKit kit, RoomPlan room)
    {
        Data l = room.Layout<Data>();
        var v = new View();
        for (int i = 0; i < l.Plates.Length; i++)
        {
            Vector3 at = room.World(l.Plates[i]);
            v.Plates.Add(kit.Plate(at, l.PlateIron[i]));
            kit.Label((i + 1).ToString(), at + Vector3.Up * 2.6f, 96);
            // The grate that keeps a later plate raised until its turn.
            var grate = new Node3D { Position = at + Vector3.Up * 0.32f };
            kit.Put(grate);
            var bar = PieceKit.Flat(PieceKit.Iron);
            for (int k = -2; k <= 2; k++)
            {
                grate.AddChild(PieceKit.Mesh(new BoxMesh { Size = new Vector3(0.08f, 0.1f, 1.7f) }, new Vector3(k * 0.36f, 0f, 0f), bar, false));
                grate.AddChild(PieceKit.Mesh(new BoxMesh { Size = new Vector3(1.7f, 0.1f, 0.08f) }, new Vector3(0f, 0.02f, k * 0.36f), bar, false));
            }
            v.Grates.Add(grate);
            if (l.PlateIron[i])
                kit.Put(PieceKit.Mesh(new TorusMesh { InnerRadius = 1.02f, OuterRadius = 1.18f, Rings = 28, RingSegments = 6 }, at + Vector3.Up * 0.06f, PieceKit.Flat(PieceKit.Iron), false));
        }
        // The star plate: the player's own, last.
        v.Plates.Add(kit.Plate(room.World(l.Star), true));
        Node3D star = PieceKit.Symbol(RuneSymbol.Star, PieceKit.Glow(PieceKit.Warm, 1.6f));
        star.Position = room.World(l.Star) + Vector3.Up * 2.4f;
        kit.Put(star);
        for (int c = 0; c < l.CrateStart.Length; c++)
        {
            bool iron = l.Iron[c];
            var body = new StaticBody3D { Name = "Crate", Position = room.World(l.CrateStart[c]) + Vector3.Up * 0.85f, CollisionLayer = (1u << 6) | PuzzleMap.PieceLayer, CollisionMask = 0 };
            body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(1.7f, 1.7f, 1.7f) } });
            kit.Put(body);
            v.Bodies.Add(body);
            var mesh = new Node3D { Name = "CrateMesh", Position = body.Position };
            kit.Put(mesh);
            mesh.AddChild(PieceKit.Mesh(new BoxMesh { Size = new Vector3(1.66f, 1.66f, 1.66f) }, Vector3.Zero, PieceKit.Flat(iron ? IronCrate : Crate)));
            var band = PieceKit.Flat(iron ? IronBand : CrateBand);
            foreach (float y in new[] { -0.55f, 0.55f })
                mesh.AddChild(PieceKit.Mesh(new BoxMesh { Size = new Vector3(1.72f, 0.18f, 1.72f) }, Vector3.Up * y, band));
            if (iron)
            {
                // Iron: banded both ways and studded with rivets.
                mesh.AddChild(PieceKit.Mesh(new BoxMesh { Size = new Vector3(0.24f, 1.72f, 1.72f) }, Vector3.Zero, band));
                mesh.AddChild(PieceKit.Mesh(new BoxMesh { Size = new Vector3(1.72f, 1.72f, 0.24f) }, Vector3.Zero, band));
                foreach (float x in new[] { -0.6f, 0.6f })
                    foreach (float zz in new[] { -0.6f, 0.6f })
                        mesh.AddChild(PieceKit.Mesh(new SphereMesh { Radius = 0.12f, Height = 0.24f, RadialSegments = 8, Rings = 4 }, new Vector3(x, 0.86f, zz), band));
            }
            else
                mesh.AddChild(PieceKit.Mesh(new BoxMesh { Size = new Vector3(0.2f, 1.72f, 1.72f) }, Vector3.Zero, band));
            v.Meshes.Add(mesh);
            v.Bands.Add(band);
        }
        kit.ResetPost(room.World(l.ResetCell));
        return v;
    }

    public override void Sync(PieceKit kit, object view, RoomPlan room, PuzzleState st, float t, float dt, bool changed)
    {
        var v = (View)view;
        Data l = room.Layout<Data>();
        int[] crates = Crates(st, room);
        int locked = Locked(l, crates);
        for (int i = 0; i < crates.Length; i++)
        {
            int plate = Array.IndexOf(l.Plates, crates[i]);
            bool isLocked = plate >= 0 && plate < locked;
            // A locked crate sinks a hand's width into its plate and its bands glow.
            Vector3 target = room.World(crates[i]) + Vector3.Up * (isLocked ? 0.72f : 0.85f);
            if (changed) v.Bodies[i].Position = room.World(crates[i]) + Vector3.Up * 0.85f;
            v.Meshes[i].Position = v.Meshes[i].Position.MoveToward(target, dt * 7f);
            PieceKit.Lit(v.Bands[i], isLocked, PieceKit.Teal, l.Iron[i] ? IronBand : CrateBand, 1.1f);
        }
        int pressed = st.Get(room, PuzzleState.RPressed);
        bool solved = st.Solved(room);
        for (int i = 0; i < v.Plates.Count; i++) PieceKit.DrawPlate(v.Plates[i], (pressed >> i & 1) != 0 || solved, dt);
        for (int i = 0; i < v.Grates.Count; i++) v.Grates[i].Visible = i > locked && !solved;
    }

    public override void Sounds(object view, RoomPlan room, Func<int, int> was, Func<int, int> now)
    {
        for (int i = 0; i < room.Layout<Data>().CrateStart.Length; i++)
            if (was(PuzzleState.RA + i) != now(PuzzleState.RA + i)) { AudioDirector.PlayPuzzle(PuzzleSound.Thunk); break; }
        if ((now(PuzzleState.RPressed) & ~was(PuzzleState.RPressed)) != 0) AudioDirector.PlayPuzzle(PuzzleSound.Click);
    }

    public override void Offer(PuzzleMap map, RoomPlan room, FocusOffer offer)
        => offer(PuzzleAction.Reset, 0, room.World(room.Layout<Data>().ResetCell), VerbReset);

    /// <summary>Walking into a crate along a grid line for a moment pushes it one cell.</summary>
    public override void LocalStep(PuzzleMap map, object view, Player me, RoomPlan room, int r, float dt, bool control)
    {
        var v = (View)view;
        v.PushCooldown = Mathf.Max(0f, v.PushCooldown - dt);
        int side = control ? PushSide(me.MoveWish) : -1;
        int crate = -1;
        if (side >= 0)
        {
            int[] crates = Crates(map.State, room);
            Vector3 p = me.GlobalPosition;
            for (int i = 0; i < crates.Length; i++)
            {
                Vector3 d = room.World(crates[i]) - p;
                float along = d.X * RoomPlan.Dx[side] + d.Z * RoomPlan.Dz[side];
                float across = Mathf.Abs(d.X * RoomPlan.Dz[side] - d.Z * RoomPlan.Dx[side]);
                if (along > 0.6f && along < 1.9f && across < 0.75f) { crate = i; break; }
            }
        }
        if (crate < 0 || v.PushCooldown > 0f)
        {
            v.PushHeld = 0f;
            return;
        }
        v.PushHeld += dt;
        if (v.PushHeld < 0.16f) return;
        v.PushHeld = 0f;
        v.PushCooldown = 0.4f;
        map.Ask(PuzzleAction.Push, r, crate, side);
    }

    /// <summary>The grid side a move wish points along clearly, or -1.</summary>
    public static int PushSide(Vector3 wish)
    {
        if (wish.LengthSquared() <= 0.25f) return -1;
        if (Mathf.Abs(wish.X) > Mathf.Abs(wish.Z) * 1.4f) return wish.X > 0 ? 0 : 2;
        if (Mathf.Abs(wish.Z) > Mathf.Abs(wish.X) * 1.4f) return wish.Z > 0 ? 1 : 3;
        return -1;
    }

    private static int From(RoomPlan room, Vector3? me, int[] crates)
    {
        int from = me is { } at ? room.CellAt(at) : room.EntryCell;
        if (from < 0 || PuzzleSolver.Solid(room, from) || Array.IndexOf(crates, from) >= 0) from = room.EntryCell;
        return from;
    }

    public override PuzzleHint? HintAt(PuzzleState st, RoomPlan room, Vector3? me, float now)
    {
        Data l = room.Layout<Data>();
        int[] crates = Crates(st, room);
        List<Move>? moves = Plan(st, room, From(room, me, crates));
        if (moves is null) return new PuzzleHint(room.World(l.ResetCell), "!");
        if (moves.Count == 0) return new PuzzleHint(room.World(l.Star), "!");
        return new PuzzleHint(room.World(moves[0].From) + Vector3.Up * 0.6f, "!", moves[0].Side);
    }

    public override string Status(PuzzleMap map, RoomPlan room)
    {
        Data l = room.Layout<Data>();
        return Meta.Loc.F(PlatesLine, Locked(l, Crates(map.State, room)), l.Plates.Length);
    }

    // -------------------------------------------------------------- bot

    public override Vector3 Bot(PuzzleBot bot, RoomPlan room, Vector3 me)
    {
        Data l = room.Layout<Data>();
        int[] crates = Crates(bot.State, room);
        bool IsCrate(int c) => Array.IndexOf(crates, c) >= 0;
        int from = room.CellAt(me);
        if (from < 0 || PuzzleSolver.Solid(room, from) || IsCrate(from)) from = NearestFree(room, me, crates);
        List<Move>? moves = Plan(bot.State, room, from);
        if (moves is null)
            return bot.Use(room, me, room.World(l.ResetCell), PuzzleAction.Reset, 0);   // stuck: reset
        if (moves.Count == 0) return PuzzleBot.WalkGrid(room, me, l.Star, false, IsCrate);
        return PuzzleBot.PushFrom(room, me, moves[0].Stand, moves[0].Side, IsCrate);
    }

    private static int NearestFree(RoomPlan room, Vector3 me, int[] crates)
    {
        int best = room.EntryCell;
        float bestD = float.MaxValue;
        for (int c = 0; c < RoomPlan.N * RoomPlan.N; c++)
        {
            if (PuzzleSolver.Solid(room, c) || Array.IndexOf(crates, c) >= 0) continue;
            float d = room.World(c).DistanceSquaredTo(me);
            if (d < bestD) { bestD = d; best = c; }
        }
        return best;
    }

    public override void Glyph(CanvasItem ci, Vector2 at, float size, Color c)
    {
        float h = size * 0.55f;
        ci.DrawRect(new Rect2(at - new Vector2(h, h), new Vector2(h * 2f, h * 2f)), c, false, Mathf.Max(1f, size * 0.16f));
        ci.DrawLine(at - new Vector2(h, h), at + new Vector2(h, h), c, Mathf.Max(1f, size * 0.12f));
    }
}
