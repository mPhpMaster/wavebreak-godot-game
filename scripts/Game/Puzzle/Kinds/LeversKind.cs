using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Wavebreak.Audio;
using Wavebreak.Core;

namespace Wavebreak.Game.Puzzle.Kinds;

/// <summary>
/// Lamp Room: a row of levers facing a row of lamps. Each lever flips its own
/// lamp and one to three others (standing at a lever marks them). The goal is
/// a pattern: light exactly the lamps that wear a halo and leave the others
/// dark (at low difficulty every lamp wears one).
///
/// What makes it a puzzle rather than a guess:
/// <list type="bullet">
/// <item><b>Padlocked levers.</b> A lever with a shackle and a carved rune
/// only moves while the lamp carrying the same rune is lit. Order matters:
/// a key lamp often has to be lit first and put out again afterwards, so the
/// answer is a route through the lamp states, not a set of levers.</item>
/// <item><b>Counted pulls.</b> The room allows a few pulls more than the
/// fewest; the last one that does not solve it jams the mechanism and the
/// board scrambles back to its start. Trying levers at random runs out.</item>
/// </list>
/// Difficulty adds levers and lamps (5 to 8), padlocks (1 to 4), wider masks,
/// a longer shortest answer (3 to 8 pulls) and a tighter allowance (4 spare
/// pulls down to 1); from 2 a padlock must force a detour (the answer with
/// padlocks is longer than without), from 3 the halo pattern leaves lamps
/// dark.
///
/// State: RA lamps lit (bits); RB lever positions (bits, display only); RC
/// pulls used; RY refused pulls (a counter, for the rattle); RFails jams;
/// RResets resets. Solver: breadth-first search over the lamp states (at
/// most 2^8), padlocks applied, fewest pulls first.
/// </summary>
public sealed class LeversKind : PuzzleKind
{
    public sealed class Data
    {
        public Vector2[] Levers = Array.Empty<Vector2>();
        public Vector2[] Lamps = Array.Empty<Vector2>();
        public int[] Masks = Array.Empty<int>();
        /// <summary>Per lever: the lamp that must be lit for it to move, or -1.</summary>
        public int[] Key = Array.Empty<int>();
        /// <summary>Per lever: the rune on its padlock (and on its key lamp).</summary>
        public RuneSymbol[] Rune = Array.Empty<RuneSymbol>();
        public int LampStart, Target, Budget, Best;
        public Vector2 ResetSpot;
    }

    private sealed class View
    {
        public readonly List<Node3D> Handles = new();
        public readonly List<Node3D> Lamps = new();
        public readonly List<StandardMaterial3D> Mats = new();
        public readonly List<Node3D> Marks = new();
        public readonly List<Node3D?> Shackles = new();
        public readonly List<StandardMaterial3D?> ShackleMats = new();
        public readonly List<Node3D> Halos = new();
    }

    private const string Verb = "Pull the lever";
    private const string VerbLocked = "Locked: light the lamp with its rune";
    private const string VerbReset = "Reset the levers";
    private const string LampsLine = "Lamps right: {0}/{1}  ·  pulls left: {2}";

    public override string Id => "levers";
    public override string Title => "Lamp Room";
    public override string Objective => "Light exactly the lamps that wear a halo. A padlocked lever only moves while the lamp with its rune is lit. Pulls are counted: run out and the board resets.";
    public override string Hint => "Hint: pull the marked lever next. The post resets the board.";
    public override IEnumerable<string> Strings => new[] { Verb, VerbLocked, VerbReset, LampsLine };
    public override HallStyle Style => HallStyle.Foundry;
    public override float ParSeconds => 48f;

    private static readonly Color Unlit = new(0.35f, 0.38f, 0.42f);

    // ------------------------------------------------------------- data

    public override void Generate(RoomPlan room, Rng rng, int d)
    {
        var l = new Data();
        room.Data = l;
        d = Math.Clamp(d, 0, 5);
        int k = new[] { 5, 6, 6, 7, 8, 8 }[d];
        int locks = new[] { 1, 2, 2, 3, 3, 4 }[d];
        int minBest = new[] { 3, 4, 5, 6, 7, 8 }[d];
        int slack = new[] { 4, 3, 2, 2, 1, 1 }[d];
        bool pattern = d >= 3, detour = d >= 2;
        int turn = rng.RangeInt(0, 4);
        Vector2 Turn(Vector2 v) => turn switch
        {
            1 => new Vector2(-v.Y, v.X),
            2 => -v,
            3 => new Vector2(v.Y, -v.X),
            _ => v,
        };
        float gap = k <= 6 ? 3f : 2.4f;
        l.Levers = new Vector2[k];
        l.Lamps = new Vector2[k];
        for (int i = 0; i < k; i++)
        {
            float x = (i - (k - 1) * 0.5f) * gap;
            l.Levers[i] = Turn(new Vector2(x, 5.6f));
            l.Lamps[i] = Turn(new Vector2(x, -2.6f));
        }
        l.ResetSpot = Turn(new Vector2(-8f, -8f));   // a grid corner, away from the rows
        int full = (1 << k) - 1;

        for (int attempt = 0; attempt < 600; attempt++)
        {
            bool late = attempt >= 450;
            l.Masks = new int[k];
            for (int i = 0; i < k; i++)
            {
                int extra = 1 + (d >= 2 ? 1 : 0) + (rng.Chance(0.25f + 0.08f * d) ? 1 : 0);
                var others = Enumerable.Range(0, k).Where(j => j != i).ToList();
                int mask = 1 << i;
                for (int e = 0; e < extra && others.Count > 0; e++) mask |= 1 << PuzzleGen.Take(rng, others);
                l.Masks[i] = mask;
            }
            l.Key = Enumerable.Repeat(-1, k).ToArray();
            l.Rune = new RuneSymbol[k];
            var lockable = Enumerable.Range(0, k).ToList();
            var runes = Enumerable.Range(0, 6).ToList();
            for (int n = 0; n < locks && lockable.Count > 0; n++)
            {
                int lever = PuzzleGen.Take(rng, lockable);
                int key;
                do key = rng.RangeInt(0, k); while (key == lever);
                l.Key[lever] = key;
                l.Rune[lever] = (RuneSymbol)PuzzleGen.Take(rng, runes);
            }
            l.Target = full;
            if (pattern)
            {
                // A halo pattern with one to three lamps left dark.
                int dark = rng.RangeInt(1, Math.Min(3, k - 3) + 1);
                for (int n = 0; n < dark; n++) l.Target &= ~(1 << rng.RangeInt(0, k));
            }
            l.LampStart = rng.RangeInt(0, full + 1);
            if (l.LampStart == l.Target) continue;
            List<int>? path = Path(l, l.LampStart, l.Target, true);
            if (path is null) continue;
            int best = path.Count;
            if (best < (late ? Math.Max(3, minBest - 2) : minBest) || best > minBest + 4) continue;
            if (detour && !late && (Path(l, l.LampStart, l.Target, false)?.Count ?? 99) >= best) continue;
            // Every padlock must matter somewhere on the way: the answer pulls a locked lever.
            if (!late && !path.Any(i => l.Key[i] >= 0)) continue;
            l.Best = best;
            l.Budget = best + slack;
            break;
        }
        if (l.Best == 0)
        {
            // Fallback: a fixed board that still needs the padlock in order.
            l.Masks = new int[k];
            for (int i = 0; i < k; i++) l.Masks[i] = (1 << i) | (1 << ((i + 1) % k));
            l.Key = Enumerable.Repeat(-1, k).ToArray();
            l.Rune = new RuneSymbol[k];
            l.Key[0] = 2;
            l.Rune[0] = RuneSymbol.Star;
            l.Target = full;
            l.LampStart = full ^ l.Masks[0] ^ l.Masks[2] ^ l.Masks[3];
            List<int> path = Path(l, l.LampStart, l.Target, true) ?? new List<int> { 0 };
            l.Best = path.Count;
            l.Budget = l.Best + slack;
        }
        room.Spots.AddRange(l.Levers);
        room.Spots.AddRange(l.Lamps);
        room.Spots.Add(l.ResetSpot);
        if (d >= 2) PuzzleGen.AddSpikes(room, rng, c => PuzzleGen.NearAny(RoomPlan.Local(c), l.Levers, 2.2f) || PuzzleGen.NearAny(RoomPlan.Local(c), l.Lamps, 2.2f) || RoomPlan.Local(c).DistanceTo(l.ResetSpot) < 2.2f);
    }

    public override void InitState(PuzzleState st, RoomPlan room) => st.V[room.Slot + PuzzleState.RA] = room.Layout<Data>().LampStart;

    /// <summary>Can lever <paramref name="i"/> move with these lamps lit?</summary>
    public static bool Free(Data l, int i, int lamps) => l.Key[i] < 0 || (lamps >> l.Key[i] & 1) != 0;

    /// <summary>The fewest pulls (levers, in order) from <paramref name="lamps"/>
    /// to <paramref name="target"/>; null when there is none. Without
    /// <paramref name="padlocks"/> every lever moves (to measure the detour).</summary>
    public static List<int>? Path(Data l, int lamps, int target, bool padlocks)
    {
        if (lamps == target) return new List<int>();
        int k = l.Masks.Length;
        var prev = new int[1 << k];
        var by = new int[1 << k];
        Array.Fill(prev, -1);
        prev[lamps] = lamps;
        var queue = new Queue<int>();
        queue.Enqueue(lamps);
        while (queue.Count > 0)
        {
            int s = queue.Dequeue();
            for (int i = 0; i < k; i++)
            {
                if (padlocks && !Free(l, i, s)) continue;
                int n = s ^ l.Masks[i];
                if (prev[n] >= 0) continue;
                prev[n] = s;
                by[n] = i;
                if (n == target)
                {
                    var path = new List<int>();
                    for (int c = n; c != lamps; c = prev[c]) path.Add(by[c]);
                    path.Reverse();
                    return path;
                }
                queue.Enqueue(n);
            }
        }
        return null;
    }

    /// <summary>The answer from the room as it stands, within the pulls left;
    /// null when the board must be reset first.</summary>
    private static List<int>? Plan(PuzzleState st, RoomPlan room)
    {
        Data l = room.Layout<Data>();
        List<int>? path = Path(l, st.Get(room, PuzzleState.RA), l.Target, true);
        if (path is null || path.Count > l.Budget - st.Get(room, PuzzleState.RC)) return null;
        return path;
    }

    public override bool Act(PuzzleState st, RoomPlan room, int r, PuzzleAction action, int a, int b, float now)
    {
        Data l = room.Layout<Data>();
        switch (action)
        {
            case PuzzleAction.Lever:
            {
                if (a < 0 || a >= l.Masks.Length) return true;
                int lamps = st.Get(room, PuzzleState.RA);
                if (!Free(l, a, lamps))
                {
                    st.SetR(room, PuzzleState.RY, st.Get(room, PuzzleState.RY) + 1);
                    return true;
                }
                lamps ^= l.Masks[a];
                int pulls = st.Get(room, PuzzleState.RC) + 1;
                st.SetR(room, PuzzleState.RA, lamps);
                st.SetR(room, PuzzleState.RB, st.Get(room, PuzzleState.RB) ^ (1 << a));
                st.SetR(room, PuzzleState.RC, pulls);
                if (lamps == l.Target) st.MarkSolved(r, now);
                else if (pulls >= l.Budget)
                {
                    // Out of pulls: the mechanism jams and the board scrambles back.
                    Restart(st, room);
                    st.SetR(room, PuzzleState.RFails, st.Get(room, PuzzleState.RFails) + 1);
                }
                return true;
            }
            case PuzzleAction.Reset:
                Restart(st, room);
                st.SetR(room, PuzzleState.RResets, st.Get(room, PuzzleState.RResets) + 1);
                return true;
        }
        return false;
    }

    private static void Restart(PuzzleState st, RoomPlan room)
    {
        st.SetR(room, PuzzleState.RA, room.Layout<Data>().LampStart);
        st.SetR(room, PuzzleState.RB, 0);
        st.SetR(room, PuzzleState.RC, 0);
    }

    public override async Task<string?> Solve(IPuzzleDriver d, RoomPlan room, int r)
    {
        List<int>? path = Plan(d.State, room);
        if (path is null)
        {
            await d.Ask(PuzzleAction.Reset, 0);
            path = Plan(d.State, room);
        }
        if (path is null) return "no lever solution";
        if (path.Count < 2) return "one pull solves it";
        foreach (int i in path)
        {
            if (!Free(room.Layout<Data>(), i, d.State.Get(room, PuzzleState.RA))) return "the answer pulled a locked lever";
            await d.Ask(PuzzleAction.Lever, i);
        }
        return null;
    }

    // ------------------------------------------------------------ scene

    public override object Build(PieceKit kit, RoomPlan room)
    {
        Data l = room.Layout<Data>();
        var v = new View();
        for (int i = 0; i < l.Levers.Length; i++)
        {
            Vector3 at = room.World(l.Levers[i]);
            // The handle swings along the row's facing: towards the lamps when pulled.
            Vector2 toLamps = (l.Lamps[i] - l.Levers[i]).Normalized();
            float yaw = Mathf.Atan2(toLamps.X, toLamps.Y);
            kit.Put(PieceKit.Mesh(new BoxMesh { Size = new Vector3(0.9f, 0.5f, 0.6f) }, at + Vector3.Up * 0.25f, PieceKit.Flat(PieceKit.WallCap)));
            var pivot = new Node3D { Position = at + Vector3.Up * 0.5f, Rotation = new Vector3(0f, yaw, 0f) };
            kit.Put(pivot);
            var handle = new Node3D { Rotation = new Vector3(-0.6f, 0f, 0f) };
            pivot.AddChild(handle);
            handle.AddChild(PieceKit.Mesh(new CylinderMesh { TopRadius = 0.06f, BottomRadius = 0.08f, Height = 1.1f, RadialSegments = 8 }, Vector3.Up * 0.55f, PieceKit.Flat(PieceKit.BronzeDark)));
            handle.AddChild(PieceKit.Mesh(new SphereMesh { Radius = 0.17f, Height = 0.34f, RadialSegments = 10, Rings = 5 }, Vector3.Up * 1.12f, PieceKit.Flat(PieceKit.Bronze)));
            v.Handles.Add(handle);
            if (l.Key[i] >= 0)
            {
                // A padlock: an iron shackle on the base's front, its rune above.
                var lockNode = new Node3D { Position = at + Vector3.Up * 0.5f, Rotation = new Vector3(0f, yaw, 0f) };
                kit.Put(lockNode);
                var mat = PieceKit.Flat(PieceKit.Iron);
                var shackle = new Node3D { Position = new Vector3(0f, 0f, 0.34f) };
                lockNode.AddChild(shackle);
                shackle.AddChild(PieceKit.Mesh(new TorusMesh { InnerRadius = 0.13f, OuterRadius = 0.2f, Rings = 14, RingSegments = 6 }, Vector3.Up * 0.12f, mat));
                shackle.GetChild<Node3D>(0).Rotation = new Vector3(Mathf.Pi * 0.5f, 0f, 0f);
                lockNode.AddChild(PieceKit.Mesh(new BoxMesh { Size = new Vector3(0.36f, 0.26f, 0.12f) }, new Vector3(0f, -0.08f, 0.34f), PieceKit.Flat(PieceKit.Iron)));
                Node3D rune = PieceKit.Symbol(l.Rune[i], PieceKit.Glow(PieceKit.Warm, 1.6f));
                rune.Position = new Vector3(0f, 1.95f, 0f);
                rune.Scale = Vector3.One * 0.65f;
                lockNode.AddChild(rune);
                v.Shackles.Add(shackle);
                v.ShackleMats.Add(mat);
            }
            else
            {
                v.Shackles.Add(null);
                v.ShackleMats.Add(null);
            }
            kit.Post(at, 0.45f, 1.2f);
        }
        for (int i = 0; i < l.Lamps.Length; i++)
        {
            Vector3 at = room.World(l.Lamps[i]);
            kit.Put(PieceKit.Mesh(new CylinderMesh { TopRadius = 0.32f, BottomRadius = 0.42f, Height = 0.5f, RadialSegments = 10 }, at + Vector3.Up * 0.25f, PieceKit.Flat(PieceKit.WallCap)));
            var mat = PieceKit.Flat(Unlit);
            var lamp = new Node3D { Position = at + Vector3.Up * 0.5f };
            kit.Put(lamp);
            lamp.AddChild(PieceKit.Mesh(new CylinderMesh { TopRadius = 0.08f, BottomRadius = 0.3f, Height = 1.6f, RadialSegments = 6 }, Vector3.Up * 0.8f, mat));
            v.Lamps.Add(lamp);
            v.Mats.Add(mat);
            var mark = PieceKit.Mesh(new CylinderMesh { TopRadius = 0.22f, BottomRadius = 0f, Height = 0.4f, RadialSegments = 8 }, at + Vector3.Up * 3.4f, PieceKit.Glow(PieceKit.Warm, 1.8f), false);
            mark.Visible = false;
            kit.Put(mark);
            v.Marks.Add(mark);
            // The halo: the lamps the pattern wants lit wear a ring overhead.
            var halo = PieceKit.Mesh(new TorusMesh { InnerRadius = 0.36f, OuterRadius = 0.46f, Rings = 20, RingSegments = 6 }, at + Vector3.Up * 2.75f, PieceKit.Glow(new Color(0.95f, 0.92f, 0.7f), 1.2f), false);
            halo.Visible = (l.Target >> i & 1) != 0;
            kit.Put(halo);
            v.Halos.Add(halo);
            // A key lamp carries the rune of every padlock it opens, on its base.
            int runes = 0;
            for (int j = 0; j < l.Levers.Length; j++)
            {
                if (l.Key[j] != i) continue;
                Node3D rune = PieceKit.Symbol(l.Rune[j], PieceKit.Glow(PieceKit.Warm, 1.6f));
                Vector2 toLever = (l.Levers[j] - l.Lamps[i]).Normalized();
                Vector3 face = new(toLever.X, 0f, toLever.Y);
                Vector3 side = new(face.Z, 0f, -face.X);
                // Beside the lamp at head height, facing the levers.
                rune.Position = at + Vector3.Up * (1.5f + runes * 0.7f) + face * 0.75f + side * 0.55f;
                rune.Rotation = new Vector3(0f, Mathf.Atan2(face.X, face.Z), 0f);
                rune.Scale = Vector3.One * 0.6f;
                kit.Put(rune);
                runes++;
            }
            kit.Post(at, 0.42f, 1.6f);
        }
        kit.ResetPost(room.World(l.ResetSpot));
        return v;
    }

    public override void Sync(PieceKit kit, object view, RoomPlan room, PuzzleState st, float t, float dt, bool changed)
    {
        var v = (View)view;
        Data l = room.Layout<Data>();
        int pulled = st.Get(room, PuzzleState.RB);
        int lit = st.Get(room, PuzzleState.RA);
        for (int i = 0; i < v.Handles.Count; i++)
        {
            float target = (pulled >> i & 1) != 0 ? 0.6f : -0.6f;
            Vector3 rot = v.Handles[i].Rotation;
            v.Handles[i].Rotation = new Vector3(Mathf.MoveToward(rot.X, target, dt * 5f), 0f, 0f);
            if (v.Shackles[i] is { } sh)
            {
                // Open: the shackle lifts and swings aside.
                bool open = Free(l, i, lit);
                Vector3 p = sh.Position;
                sh.Position = new Vector3(p.X, Mathf.MoveToward(p.Y, open ? 0.22f : 0f, dt * 2f), p.Z);
                sh.Rotation = new Vector3(0f, Mathf.MoveToward(sh.Rotation.Y, open ? 1.2f : 0f, dt * 6f), 0f);
                PieceKit.Lit(v.ShackleMats[i]!, open, PieceKit.Warm, PieceKit.Iron, 1.2f);
            }
        }
        PuzzleFocus? focus = kit.Map.Focus;
        for (int i = 0; i < v.Lamps.Count; i++)
        {
            bool on = (lit >> i & 1) != 0;
            float sy = Mathf.MoveToward(v.Lamps[i].Scale.Y, on ? 1f : 0.35f, dt * 3f);
            v.Lamps[i].Scale = new Vector3(1f, sy, 1f);
            PieceKit.Lit(v.Mats[i], on, PieceKit.Warm, Unlit, 1.8f);
            // Standing at a lever marks the lamps it flips.
            bool mark = focus is { Action: PuzzleAction.Lever } f && f.Room == room.Index && f.Index < l.Masks.Length && (l.Masks[f.Index] >> i & 1) != 0;
            v.Marks[i].Visible = mark;
            if (mark) v.Marks[i].Position = room.World(l.Lamps[i]) + Vector3.Up * (3.4f + Mathf.Sin(t * 5f) * 0.12f);
            if (v.Halos[i].Visible) v.Halos[i].RotateY(dt * 0.6f);
        }
    }

    public override void Sounds(object view, RoomPlan room, Func<int, int> was, Func<int, int> now)
    {
        if (now(PuzzleState.RFails) != was(PuzzleState.RFails)) AudioDirector.PlayPuzzle(PuzzleSound.Wrong);
        else if (was(PuzzleState.RA) != now(PuzzleState.RA)) AudioDirector.PlayPuzzle(PuzzleSound.Click, 0.8f);
        if (was(PuzzleState.RY) != now(PuzzleState.RY)) AudioDirector.PlayPuzzle(PuzzleSound.Thunk, 1.3f);
    }

    public override void Offer(PuzzleMap map, RoomPlan room, FocusOffer offer)
    {
        Data l = room.Layout<Data>();
        int lit = map.State.Get(room, PuzzleState.RA);
        for (int i = 0; i < l.Levers.Length; i++) offer(PuzzleAction.Lever, i, room.World(l.Levers[i]), Free(l, i, lit) ? Verb : VerbLocked);
        offer(PuzzleAction.Reset, 0, room.World(l.ResetSpot), VerbReset);
    }

    public override PuzzleHint? HintAt(PuzzleState st, RoomPlan room, Vector3? me, float now)
    {
        Data l = room.Layout<Data>();
        List<int>? path = Plan(st, room);
        if (path is null) return new PuzzleHint(room.World(l.ResetSpot), "!");
        return path.Count == 0 ? null : new PuzzleHint(room.World(l.Levers[path[0]]), "!");
    }

    public override string Status(PuzzleMap map, RoomPlan room)
    {
        Data l = room.Layout<Data>();
        int lit = map.State.Get(room, PuzzleState.RA);
        int right = l.Masks.Length - PuzzleSolver.PopCount((lit ^ l.Target) & ((1 << l.Masks.Length) - 1));
        return Meta.Loc.F(LampsLine, right, l.Masks.Length, Math.Max(0, l.Budget - map.State.Get(room, PuzzleState.RC)));
    }

    public override Vector3 Bot(PuzzleBot bot, RoomPlan room, Vector3 me)
    {
        Data l = room.Layout<Data>();
        List<int>? path = Plan(bot.State, room);
        if (path is null) return Reach(bot, room, me, room.World(l.ResetSpot), PuzzleAction.Reset, 0);
        if (path.Count == 0) return Vector3.Zero;
        return Reach(bot, room, me, room.World(l.Levers[path[0]]), PuzzleAction.Lever, path[0]);
    }

    /// <summary>Walk the grid to a piece, then straight at it until it is the
    /// focus (levers stand between cells, so the cell beside one can be just
    /// out of reach).</summary>
    private static Vector3 Reach(PuzzleBot bot, RoomPlan room, Vector3 me, Vector3 at, PuzzleAction action, int index)
    {
        if (bot.PressIf(action, index, room.Index)) return Vector3.Zero;
        Vector3 d = at - me;
        d.Y = 0f;
        if (d.Length() < 4f) return d.Normalized() * 0.7f;
        return bot.Use(room, me, at, action, index);
    }

    public override void Glyph(CanvasItem ci, Vector2 at, float size, Color c)
    {
        float h = size * 0.6f, w = Mathf.Max(1f, size * 0.16f);
        for (int i = -1; i <= 1; i++)
        {
            Vector2 b = at + new Vector2(i * h * 0.7f, h * 0.6f);
            ci.DrawLine(b, b + new Vector2(h * 0.3f, -h * 1.1f), c, w);
        }
    }
}
