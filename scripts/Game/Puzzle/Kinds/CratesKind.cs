using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Audio;
using Wavebreak.Core;

namespace Wavebreak.Game.Puzzle.Kinds;

/// <summary>
/// Weighing Room: pressure plates held down by pushable crates, and the last
/// plate by a player (any player, so one is enough). Walking into a crate
/// along a grid line for a moment pushes it one cell; a crate never enters
/// the cell in front of a door; the reset bell puts the crates back.
///
/// State: RA, RB crate cells; RPressed plates held (bits); RResets resets.
/// Solver: breadth-first search over (player cell, crate cells).
/// </summary>
public sealed class CratesKind : PuzzleKind
{
    public sealed class Data
    {
        public int[] CrateStart = Array.Empty<int>();
        public int[] Plates = Array.Empty<int>();
        public int ResetCell = -1;
        public int Pushes;
    }

    private sealed class View
    {
        public readonly List<StaticBody3D> Bodies = new();
        public readonly List<Node3D> Meshes = new();
        public readonly List<PlateView> Plates = new();
        public float PushHeld, PushCooldown;
    }

    private const string VerbReset = "Reset the crates";
    private const string PlatesLine = "Plates held: {0}/{1}";

    public override string Id => "crates";
    public override string Title => "Weighing Room";
    public override string Objective => "Push crates onto the plates, then stand on the last one.";
    public override string Hint => "Hint: the marked crate moves next, in the arrow's direction. The bell resets the crates.";
    public override IEnumerable<string> Strings => new[] { VerbReset, PlatesLine };
    public override HallStyle Style => HallStyle.Workshop;

    private static readonly Color Crate = new(0.70f, 0.50f, 0.30f);
    private static readonly Color CrateBand = new(0.45f, 0.30f, 0.18f);

    // ------------------------------------------------------------- data

    public override void Generate(RoomPlan room, Rng rng, int d)
    {
        var l = new Data();
        room.Data = l;
        int crates = d == 0 ? 1 : d <= 2 ? (rng.Chance(0.5f) ? 2 : 1) : 2;
        int lo = 2 + d, hi = 8 + 3 * d;
        for (int attempt = 0; attempt < 160; attempt++)
        {
            if (attempt == 100) lo = 1;   // late attempts take any honest room
            PuzzleGen.Clear(room);
            l.ResetCell = PuzzleGen.Corner(rng.RangeInt(0, 4));
            room.Fixed[l.ResetCell] = true;
            PuzzleGen.Scatter(room, rng, rng.RangeInt(3, 6 + d));
            List<int> free = PuzzleGen.FreeInner(room);
            if (free.Count < crates * 2 + 2) continue;
            var plates = new int[crates + 1];
            for (int i = 0; i < plates.Length; i++) plates[i] = PuzzleGen.Take(rng, free);
            var start = new int[crates];
            bool dead = false;
            for (int i = 0; i < crates; i++)
            {
                start[i] = PuzzleGen.Take(rng, free);
                dead |= DeadCorner(room, start[i]);
            }
            if (dead) continue;
            l.Plates = plates;
            l.CrateStart = start;
            List<int>? moves = Solve(room, room.EntryCell, start, out int pushes, 60000);
            if (moves is null || pushes < lo || pushes > hi || !PuzzleSolver.DoorsConnected(room)) continue;
            l.Pushes = pushes;
            return;
        }
        // Fallback: one crate two cells short of a plate, in a clear row.
        PuzzleGen.Clear(room);
        l.ResetCell = PuzzleGen.Corner(0);
        room.Fixed[l.ResetCell] = true;
        int z = room.EntrySide == 1 || room.ExitSide == 1 ? 3 : 5;
        l.CrateStart = new[] { RoomPlan.CellOf(3, z) };
        l.Plates = new[] { RoomPlan.CellOf(5, z), RoomPlan.CellOf(6, z == 3 ? 2 : 6) };
        Solve(room, room.EntryCell, l.CrateStart, out l.Pushes);
    }

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

    public override void HostStep(PuzzleState st, RoomPlan room, int r, IReadOnlyList<Vector3> players, float now)
    {
        Data l = room.Layout<Data>();
        int[] crates = Crates(st, room);
        int pressed = 0;
        for (int i = 0; i < l.Plates.Length; i++)
        {
            int plate = l.Plates[i];
            bool on = Array.IndexOf(crates, plate) >= 0;
            foreach (Vector3 p in players) on |= PuzzleState.OnCell(room, p, plate);
            if (on) pressed |= 1 << i;
        }
        st.SetR(room, PuzzleState.RPressed, pressed);
        if (pressed == (1 << l.Plates.Length) - 1) st.MarkSolved(r, now);
    }

    public override bool Act(PuzzleState st, RoomPlan room, int r, PuzzleAction action, int a, int b, float now)
    {
        Data l = room.Layout<Data>();
        switch (action)
        {
            case PuzzleAction.Push:
            {
                if (a < 0 || a >= l.CrateStart.Length || b is < 0 or > 3) return true;
                int[] crates = Crates(st, room);
                int target = PuzzleSolver.Step(crates[a], b);
                if (CrateCanEnter(room, target, crates)) st.SetR(room, PuzzleState.RA + a, target);
                return true;
            }
            case PuzzleAction.Reset:
                for (int i = 0; i < l.CrateStart.Length; i++) st.SetR(room, PuzzleState.RA + i, l.CrateStart[i]);
                st.SetR(room, PuzzleState.RResets, st.Get(room, PuzzleState.RResets) + 1);
                return true;
        }
        return false;
    }

    // ----------------------------------------------------------- solver

    /// <summary>Every plate has a crate or the player on it.</summary>
    public static bool Goal(RoomPlan room, int player, IReadOnlyList<int> crates)
    {
        foreach (int p in room.Layout<Data>().Plates)
        {
            if (p == player) continue;
            bool held = false;
            for (int i = 0; i < crates.Count; i++) if (crates[i] == p) { held = true; break; }
            if (!held) return false;
        }
        return true;
    }

    public static bool CrateCanEnter(RoomPlan room, int cell, IReadOnlyList<int> crates)
    {
        if (cell < 0 || PuzzleSolver.Solid(room, cell) || room.CrateForbidden(cell)) return false;
        for (int i = 0; i < crates.Count; i++) if (crates[i] == cell) return false;
        return true;
    }

    /// <summary>The player's moves (sides) to solve the room from this state,
    /// shortest in steps; null when it cannot be solved from here.</summary>
    public static List<int>? Solve(RoomPlan room, int player, IReadOnlyList<int> crates, out int pushes, int maxNodes = 200000)
    {
        pushes = 0;
        if (player < 0) return null;
        int n = crates.Count;
        int Key(int p, int[] c)
        {
            int k = p;
            for (int i = 0; i < n; i++) k |= c[i] << (7 * (i + 1));
            return k;
        }
        int[] Unpack(int key, out int p)
        {
            p = key & 127;
            var c = new int[n];
            for (int i = 0; i < n; i++) c[i] = (key >> (7 * (i + 1))) & 127;
            return c;
        }

        int[] start = new int[n];
        for (int i = 0; i < n; i++) start[i] = crates[i];
        Array.Sort(start);
        int startKey = Key(player, start);
        if (Goal(room, player, start)) return new List<int>();

        var parent = new Dictionary<int, (int Prev, sbyte Side, bool Push)> { [startKey] = (-1, -1, false) };
        var queue = new Queue<int>();
        queue.Enqueue(startKey);
        int goal = -1;
        while (queue.Count > 0 && parent.Count < maxNodes)
        {
            int key = queue.Dequeue();
            int[] c = Unpack(key, out int p);
            for (int s = 0; s < 4; s++)
            {
                int next = PuzzleSolver.Step(p, s);
                if (next < 0 || PuzzleSolver.Solid(room, next)) continue;
                int hit = Array.IndexOf(c, next);
                int[] nc = c;
                bool push = false;
                if (hit >= 0)
                {
                    int beyond = PuzzleSolver.Step(next, s);
                    if (!CrateCanEnter(room, beyond, c)) continue;
                    nc = (int[])c.Clone();
                    nc[hit] = beyond;
                    Array.Sort(nc);
                    push = true;
                }
                int nk = Key(next, nc);
                if (parent.ContainsKey(nk)) continue;
                parent[nk] = (key, (sbyte)s, push);
                if (Goal(room, next, nc)) { goal = nk; break; }
                queue.Enqueue(nk);
            }
            if (goal >= 0) break;
        }
        if (goal < 0) return null;
        var moves = new List<int>();
        for (int k = goal; parent[k].Prev >= 0; k = parent[k].Prev)
        {
            moves.Add(parent[k].Side);
            if (parent[k].Push) pushes++;
        }
        moves.Reverse();
        return moves;
    }

    public override async Task<string?> Solve(IPuzzleDriver d, RoomPlan room, int r)
    {
        List<int>? moves = Solve(room, room.EntryCell, Crates(d.State, room), out int pushes);
        if (moves is null) return "no crate solution";
        if (pushes < 1) return "no push needed";
        int p = room.EntryCell;
        foreach (int side in moves)
        {
            int next = PuzzleSolver.Step(p, side);
            int crate = Array.IndexOf(Crates(d.State, room), next);
            if (crate >= 0) await d.Ask(PuzzleAction.Push, crate, side);
            p = next;
        }
        await d.Wait(0.2f);
        await d.Stand(room.World(p));
        return null;
    }

    // ------------------------------------------------------------ scene

    public override object Build(PieceKit kit, RoomPlan room)
    {
        Data l = room.Layout<Data>();
        var v = new View();
        foreach (int p in l.Plates) v.Plates.Add(kit.Plate(room.World(p)));
        foreach (int c in l.CrateStart)
        {
            var body = new StaticBody3D { Name = "Crate", Position = room.World(c) + Vector3.Up * 0.85f, CollisionLayer = (1u << 6) | PuzzleMap.PieceLayer, CollisionMask = 0 };
            body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(1.7f, 1.7f, 1.7f) } });
            kit.Put(body);
            v.Bodies.Add(body);
            var mesh = new Node3D { Name = "CrateMesh", Position = body.Position };
            kit.Put(mesh);
            mesh.AddChild(PieceKit.Mesh(new BoxMesh { Size = new Vector3(1.66f, 1.66f, 1.66f) }, Vector3.Zero, PieceKit.Flat(Crate)));
            foreach (float y in new[] { -0.55f, 0.55f })
                mesh.AddChild(PieceKit.Mesh(new BoxMesh { Size = new Vector3(1.72f, 0.18f, 1.72f) }, Vector3.Up * y, PieceKit.Flat(CrateBand)));
            mesh.AddChild(PieceKit.Mesh(new BoxMesh { Size = new Vector3(0.2f, 1.72f, 1.72f) }, Vector3.Zero, PieceKit.Flat(CrateBand)));
            v.Meshes.Add(mesh);
        }
        kit.ResetPost(room.World(l.ResetCell));
        return v;
    }

    public override void Sync(PieceKit kit, object view, RoomPlan room, PuzzleState st, float t, float dt, bool changed)
    {
        var v = (View)view;
        int[] crates = Crates(st, room);
        for (int i = 0; i < crates.Length; i++)
        {
            Vector3 target = room.World(crates[i]) + Vector3.Up * 0.85f;
            if (changed) v.Bodies[i].Position = target;
            v.Meshes[i].Position = v.Meshes[i].Position.MoveToward(target, dt * 7f);
        }
        int pressed = st.Get(room, PuzzleState.RPressed);
        for (int i = 0; i < v.Plates.Count; i++) PieceKit.DrawPlate(v.Plates[i], (pressed >> i & 1) != 0 || st.Solved(room), dt);
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

    public override PuzzleHint? HintAt(PuzzleState st, RoomPlan room, Vector3? me, float now)
    {
        Data l = room.Layout<Data>();
        int[] crates = Crates(st, room);
        int from = me is { } at ? room.CellAt(at) : room.EntryCell;
        if (from < 0 || PuzzleSolver.Solid(room, from) || Array.IndexOf(crates, from) >= 0) from = room.EntryCell;
        List<int>? moves = Solve(room, from, crates, out _, 60000);
        if (moves is null) return new PuzzleHint(room.World(l.ResetCell), "!");
        int p = from;
        foreach (int side in moves)
        {
            int next = PuzzleSolver.Step(p, side);
            if (Array.IndexOf(crates, next) >= 0) return new PuzzleHint(room.World(next) + Vector3.Up * 0.6f, "!", side);
            p = next;
        }
        return new PuzzleHint(room.World(p), "!");
    }

    public override string Status(PuzzleMap map, RoomPlan room)
        => Meta.Loc.F(PlatesLine, PuzzleSolver.PopCount(map.State.Get(room, PuzzleState.RPressed)), room.Layout<Data>().Plates.Length);

    // -------------------------------------------------------------- bot

    public override Vector3 Bot(PuzzleBot bot, RoomPlan room, Vector3 me)
    {
        int[] crates = Crates(bot.State, room);
        bool IsCrate(int c) => Array.IndexOf(crates, c) >= 0;
        int from = room.CellAt(me);
        if (from < 0 || PuzzleSolver.Solid(room, from) || IsCrate(from)) from = NearestFree(room, me, crates);
        List<int>? moves = Solve(room, from, crates, out _, 120000);
        if (moves is null)
            return bot.Use(room, me, room.World(room.Layout<Data>().ResetCell), PuzzleAction.Reset, 0);   // stuck: ring the bell
        int p = from;
        foreach (int side in moves)
        {
            int next = PuzzleSolver.Step(p, side);
            if (IsCrate(next)) return PuzzleBot.PushFrom(room, me, p, side, IsCrate);
            p = next;
        }
        // No push left: walk onto the last plate.
        return PuzzleBot.WalkGrid(room, me, p, false, IsCrate);
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
