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
/// until something stops it: a pillar, the reset post, the grid's frosted rim
/// or the cell in front of a door. Rest it on the socket that carries its
/// rune. Sliding is what makes it a puzzle: the stone can only stop where
/// something stands in its way, so the way to the socket is a chain of
/// stops. The reset post puts the stone back.
///
/// State: RA stone cell; RC pushes made; RResets resets.
/// Solver: breadth-first search over (stone cell, the player's side of it),
/// fewest pushes first.
/// </summary>
public sealed class GlideKind : PuzzleKind
{
    public sealed class Data
    {
        public int Stone = -1, Socket = -1, ResetCell = -1, Pushes;
        public RuneSymbol Rune;
    }

    private sealed class View
    {
        public StaticBody3D Body = null!;
        public Node3D Mesh = null!;
        public StandardMaterial3D SocketMat = null!;
        public Node3D SocketRune = null!;
        public Node3D Top = null!;
        public float PushHeld, PushCooldown;
    }

    private const string VerbReset = "Reset the stone";
    private const string PushesLine = "Pushes: {0}  ·  fewest: {1}";

    public override string Id => "glide";
    public override string Title => "Ice Hall";
    public override string Objective => "Push the rune stone across the ice: it slides until it hits something. Stop it on its socket.";
    public override string Hint => "Hint: push the marked stone the way the arrow points. The post resets the stone.";
    public override IEnumerable<string> Strings => new[] { VerbReset, PushesLine };
    public override HallStyle Style => HallStyle.Frost;
    public override float ParSeconds => 26f;

    private static readonly Color Ice = new(0.78f, 0.90f, 0.98f, 0.55f);
    private static readonly Color StoneTint = new(0.36f, 0.50f, 0.78f);
    private static readonly Color RuneGlow = new(0.55f, 0.85f, 1f);

    // ------------------------------------------------------------- data

    public override void Generate(RoomPlan room, Rng rng, int d)
    {
        var l = new Data();
        room.Data = l;
        int lo = 2 + d / 2, hi = 4 + d;
        for (int attempt = 0; attempt < 300; attempt++)
        {
            if (attempt == 200) lo = 2;
            PuzzleGen.Clear(room);
            l.ResetCell = PuzzleGen.Corner(rng.RangeInt(0, 4));
            room.Fixed[l.ResetCell] = true;
            PuzzleGen.Scatter(room, rng, rng.RangeInt(5, 8 + d));
            if (!PuzzleSolver.DoorsConnected(room)) continue;
            List<int> free = PuzzleGen.FreeInner(room);
            if (free.Count < 6) continue;
            l.Stone = PuzzleGen.Take(rng, free);
            // Every place the stone can be pushed to, with the fewest pushes.
            Dictionary<int, int> depth = Reach(room, l.Stone, room.EntryCell);
            int best = -1;
            var picks = new List<int>();
            foreach ((int cell, int pushes) in depth)
            {
                if (pushes < lo || pushes > hi || room.NearDoor(cell) || cell == l.Stone) continue;
                if (pushes > best) { best = pushes; picks.Clear(); }
                if (pushes == best) picks.Add(cell);
            }
            if (picks.Count == 0) continue;
            l.Socket = picks[rng.RangeInt(0, picks.Count)];
            l.Pushes = best;
            l.Rune = (RuneSymbol)rng.RangeInt(0, 6);
            return;
        }
        // Fallback: a clear floor; the stone two cells from a pillar it stops against.
        PuzzleGen.Clear(room);
        l.ResetCell = PuzzleGen.Corner(0);
        room.Fixed[l.ResetCell] = true;
        room.Pillar[RoomPlan.CellOf(6, 2)] = true;
        room.Pillar[RoomPlan.CellOf(2, 5)] = true;
        l.Stone = RoomPlan.CellOf(2, 2);
        l.Socket = RoomPlan.CellOf(5, 2);
        l.Pushes = Solve(room, l.Stone, room.EntryCell)?.Count ?? 1;
    }

    public override void InitState(PuzzleState st, RoomPlan room) => st.V[room.Slot + PuzzleState.RA] = room.Layout<Data>().Stone;

    /// <summary>Where the stone stops when pushed from <paramref name="from"/> towards <paramref name="side"/>.</summary>
    public static int Slide(RoomPlan room, int from, int side)
    {
        int cur = from;
        for (int guard = 0; guard < RoomPlan.N; guard++)
        {
            int next = PuzzleSolver.Step(cur, side);
            if (next < 0 || PuzzleSolver.Solid(room, next) || room.CrateForbidden(next)) break;
            cur = next;
        }
        return cur;
    }

    /// <summary>Cells the player can walk to from <paramref name="player"/> with the stone at <paramref name="stone"/>.</summary>
    private static bool[] Walkable(RoomPlan room, int stone, int player)
    {
        var seen = new bool[RoomPlan.N * RoomPlan.N];
        if (player < 0 || PuzzleSolver.Solid(room, player) || player == stone) return seen;
        var queue = new Queue<int>();
        queue.Enqueue(player);
        seen[player] = true;
        bool outside = false;
        while (queue.Count > 0)
        {
            int c = queue.Dequeue();
            for (int s = 0; s < 4; s++)
            {
                int n = PuzzleSolver.Step(c, s);
                if (n < 0 || seen[n] || n == stone || PuzzleSolver.Solid(room, n)) continue;
                seen[n] = true;
                queue.Enqueue(n);
            }
            // The open strip round the grid joins every edge cell to every other.
            if (!outside && PuzzleGen.Edge(c))
            {
                outside = true;
                for (int n = 0; n < seen.Length; n++)
                {
                    if (seen[n] || !PuzzleGen.Edge(n) || n == stone || PuzzleSolver.Solid(room, n)) continue;
                    seen[n] = true;
                    queue.Enqueue(n);
                }
            }
        }
        return seen;
    }

    private static int Region(bool[] seen)
    {
        for (int c = 0; c < seen.Length; c++) if (seen[c]) return c;
        return -1;
    }

    /// <summary>Pushes from (stone, player) to every stone cell it can reach.</summary>
    private static Dictionary<int, int> Reach(RoomPlan room, int stone, int player)
    {
        var depth = new Dictionary<int, int>();
        var seenState = new HashSet<int>();
        var queue = new Queue<(int Stone, int Player, int Depth)>();
        queue.Enqueue((stone, player, 0));
        while (queue.Count > 0)
        {
            (int s, int p, int k) = queue.Dequeue();
            bool[] walk = Walkable(room, s, p);
            int key = s * 128 + Region(walk);
            if (!seenState.Add(key)) continue;
            if (!depth.ContainsKey(s)) depth[s] = k;
            for (int side = 0; side < 4; side++)
            {
                int stand = PuzzleSolver.Step(s, (side + 2) % 4);
                if (stand < 0 || !walk[stand]) continue;
                int to = Slide(room, s, side);
                if (to != s) queue.Enqueue((to, stand, k + 1));
            }
        }
        return depth;
    }

    /// <summary>The fewest pushes (where to stand, which way) from this state
    /// to the socket; null when it cannot be done from here.</summary>
    public static List<(int Stand, int Side)>? Solve(RoomPlan room, int stone, int player)
    {
        int socket = room.Layout<Data>().Socket;
        if (stone == socket) return new List<(int, int)>();
        var parent = new Dictionary<int, (int Prev, int Stand, int Side)>();
        var queue = new Queue<(int Stone, int Player, int Key)>();
        bool[] w0 = Walkable(room, stone, player);
        int k0 = stone * 128 + Region(w0);
        parent[k0] = (-1, -1, -1);
        queue.Enqueue((stone, player, k0));
        while (queue.Count > 0)
        {
            (int s, int p, int key) = queue.Dequeue();
            bool[] walk = Walkable(room, s, p);
            for (int side = 0; side < 4; side++)
            {
                int stand = PuzzleSolver.Step(s, (side + 2) % 4);
                if (stand < 0 || !walk[stand]) continue;
                int to = Slide(room, s, side);
                if (to == s) continue;
                int nk = to * 128 + Region(Walkable(room, to, stand));
                if (parent.ContainsKey(nk)) continue;
                parent[nk] = (key, stand, side);
                if (to == socket)
                {
                    var moves = new List<(int, int)>();
                    for (int c = nk; parent[c].Prev >= 0; c = parent[c].Prev) moves.Add((parent[c].Stand, parent[c].Side));
                    moves.Reverse();
                    return moves;
                }
                queue.Enqueue((to, stand, nk));
            }
        }
        return null;
    }

    public override bool Act(PuzzleState st, RoomPlan room, int r, PuzzleAction action, int a, int b, float now)
    {
        Data l = room.Layout<Data>();
        switch (action)
        {
            case PuzzleAction.Push:
            {
                if (b is < 0 or > 3) return true;
                int stone = st.Get(room, PuzzleState.RA);
                int to = Slide(room, stone, b);
                if (to == stone) return true;
                st.SetR(room, PuzzleState.RA, to);
                st.SetR(room, PuzzleState.RC, st.Get(room, PuzzleState.RC) + 1);
                if (to == l.Socket) st.MarkSolved(r, now);
                return true;
            }
            case PuzzleAction.Reset:
                st.SetR(room, PuzzleState.RA, l.Stone);
                st.SetR(room, PuzzleState.RResets, st.Get(room, PuzzleState.RResets) + 1);
                return true;
        }
        return false;
    }

    public override async Task<string?> Solve(IPuzzleDriver d, RoomPlan room, int r)
    {
        List<(int Stand, int Side)>? moves = Solve(room, d.State.Get(room, PuzzleState.RA), room.EntryCell);
        if (moves is null) return "no way to the socket";
        if (moves.Count < 2) return "one push solves it";
        foreach ((int stand, int side) in moves)
        {
            await d.Stand(room.World(stand));
            await d.Ask(PuzzleAction.Push, 0, side);
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
        // (the stone stops at the rim; players step over it).
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

        // The socket: a sunken ring with the stone's rune floating over it.
        Vector3 so = room.World(l.Socket);
        v.SocketMat = PieceKit.Flat(PieceKit.BronzeDark);
        var ring = PieceKit.Mesh(new TorusMesh { InnerRadius = 0.6f, OuterRadius = 0.85f, Rings = 28, RingSegments = 8 }, so + Vector3.Up * 0.08f, v.SocketMat, false);
        kit.Put(ring);
        v.SocketRune = PieceKit.Symbol(l.Rune, PieceKit.Glow(RuneGlow, 2.2f));
        v.SocketRune.Position = so + Vector3.Up * 2.2f;
        v.SocketRune.Scale = Vector3.One * 1.3f;
        // A pale disc in the ring, so the socket reads on the ice from afar.
        kit.Put(PieceKit.Mesh(new CylinderMesh { TopRadius = 0.62f, BottomRadius = 0.62f, Height = 0.04f, RadialSegments = 20 }, so + Vector3.Up * 0.07f, PieceKit.Glow(RuneGlow.Darkened(0.35f), 0.8f), false));
        kit.Put(v.SocketRune);

        // The stone: a heavy block with its rune on every side.
        v.Body = new StaticBody3D { Name = "RuneStone", Position = room.World(l.Stone) + Vector3.Up * 0.8f, CollisionLayer = (1u << 6) | PuzzleMap.PieceLayer, CollisionMask = 0 };
        v.Body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(1.6f, 1.6f, 1.6f) } });
        kit.Put(v.Body);
        v.Mesh = new Node3D { Name = "RuneStoneMesh", Position = v.Body.Position };
        kit.Put(v.Mesh);
        v.Mesh.AddChild(PieceKit.Mesh(new BoxMesh { Size = new Vector3(1.56f, 1.56f, 1.56f) }, Vector3.Zero, PieceKit.Flat(StoneTint)));
        v.Mesh.AddChild(PieceKit.Mesh(new BoxMesh { Size = new Vector3(1.64f, 0.16f, 1.64f) }, Vector3.Up * 0.62f, PieceKit.Flat(StoneTint.Darkened(0.25f))));
        // Its rune on top too, standing up, so it reads from behind the pillars.
        Node3D top = PieceKit.Symbol(l.Rune, PieceKit.Glow(RuneGlow, 2.6f));
        top.Position = Vector3.Up * 1.35f;
        top.Scale = Vector3.One * 0.9f;
        v.Mesh.AddChild(top);
        v.Top = top;
        for (int side = 0; side < 4; side++)
        {
            Node3D sym = PieceKit.Symbol(l.Rune, PieceKit.Glow(RuneGlow, 2.6f));
            var dir = new Vector3(RoomPlan.Dx[side], 0f, RoomPlan.Dz[side]);
            sym.Position = dir * 0.8f;
            sym.Rotation = new Vector3(0f, Mathf.Atan2(dir.X, dir.Z), 0f);
            sym.Scale = Vector3.One * 0.7f;
            v.Mesh.AddChild(sym);
        }
        kit.ResetPost(room.World(l.ResetCell));
        return v;
    }

    public override void Sync(PieceKit kit, object view, RoomPlan room, PuzzleState st, float t, float dt, bool changed)
    {
        var v = (View)view;
        Vector3 target = room.World(st.Get(room, PuzzleState.RA)) + Vector3.Up * 0.8f;
        if (changed) v.Body.Position = target;
        // Slides: quick, then eases into the stop.
        float dist = v.Mesh.Position.DistanceTo(target);
        v.Mesh.Position = v.Mesh.Position.MoveToward(target, dt * Mathf.Clamp(dist * 6f, 3f, 14f));
        bool solved = st.Solved(room);
        PieceKit.Lit(v.SocketMat, solved, RuneGlow, PieceKit.BronzeDark, 2f);
        v.SocketRune.RotateY(dt * (solved ? 3f : 0.8f));
        v.Top.RotateY(dt * 0.8f);
    }

    public override void Sounds(object view, RoomPlan room, Func<int, int> was, Func<int, int> now)
    {
        if (was(PuzzleState.RA) != now(PuzzleState.RA)) AudioDirector.PlayPuzzle(PuzzleSound.Thunk, 0.8f);
    }

    public override void Offer(PuzzleMap map, RoomPlan room, FocusOffer offer)
        => offer(PuzzleAction.Reset, 0, room.World(room.Layout<Data>().ResetCell), VerbReset);

    /// <summary>Standing in the cell next to the stone and walking into it for
    /// a moment pushes it (and it slides).</summary>
    public override void LocalStep(PuzzleMap map, object view, Player me, RoomPlan room, int r, float dt, bool control)
    {
        var v = (View)view;
        v.PushCooldown = Mathf.Max(0f, v.PushCooldown - dt);
        int side = control ? CratesKind.PushSide(me.MoveWish) : -1;
        int stone = map.State.Get(room, PuzzleState.RA);
        bool lined = false;
        if (side >= 0 && room.OnGrid(me.GlobalPosition))
        {
            Vector3 d = room.World(stone) - me.GlobalPosition;
            float along = d.X * RoomPlan.Dx[side] + d.Z * RoomPlan.Dz[side];
            float across = Mathf.Abs(d.X * RoomPlan.Dz[side] - d.Z * RoomPlan.Dx[side]);
            lined = along > 0.6f && along < 1.9f && across < 0.75f;
        }
        if (!lined || v.PushCooldown > 0f)
        {
            v.PushHeld = 0f;
            return;
        }
        v.PushHeld += dt;
        if (v.PushHeld < 0.16f) return;
        v.PushHeld = 0f;
        v.PushCooldown = 0.6f;
        map.Ask(PuzzleAction.Push, r, 0, side);
    }

    private static int From(RoomPlan room, Vector3? me, int stone)
    {
        int from = me is { } at ? room.CellAt(at) : room.EntryCell;
        if (from < 0 || PuzzleSolver.Solid(room, from) || from == stone) from = room.EntryCell;
        return from;
    }

    public override PuzzleHint? HintAt(PuzzleState st, RoomPlan room, Vector3? me, float now)
    {
        int stone = st.Get(room, PuzzleState.RA);
        List<(int Stand, int Side)>? moves = Solve(room, stone, From(room, me, stone));
        if (moves is null || moves.Count == 0) return new PuzzleHint(room.World(room.Layout<Data>().ResetCell), "!");
        return new PuzzleHint(room.World(stone) + Vector3.Up * 0.6f, "!", moves[0].Side);
    }

    public override string Status(PuzzleMap map, RoomPlan room)
        => Meta.Loc.F(PushesLine, map.State.Get(room, PuzzleState.RC), room.Layout<Data>().Pushes);

    public override Vector3 Bot(PuzzleBot bot, RoomPlan room, Vector3 me)
    {
        int stone = bot.State.Get(room, PuzzleState.RA);
        bool IsStone(int c) => c == stone;
        List<(int Stand, int Side)>? moves = Solve(room, stone, From(room, me, stone));
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
