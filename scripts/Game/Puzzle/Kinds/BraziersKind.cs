using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Wavebreak.Audio;
using Wavebreak.Core;

namespace Wavebreak.Game.Puzzle.Kinds;

/// <summary>
/// Ember Hall: braziers stand about the hall. Lighting one (interact) makes
/// it burn for a few seconds, its flame shrinking as it burns down; have them
/// all burning at once. The burn time is fitted to the shortest round of the
/// braziers with a margin, so the puzzle is the order: a careless round runs
/// out of fire, a planned one has time to spare. Anyone may light any of
/// them, so a team can split the round, but one player always can.
///
/// State: burning until (tenths) per brazier in RA, RB, RC, RD, RX, RY.
/// Solver: every order of the braziers (at most 6! = 720).
/// </summary>
public sealed class BraziersKind : PuzzleKind
{
    public sealed class Data
    {
        public Vector2[] Spots = Array.Empty<Vector2>();
        public float Burn = 10f;
        /// <summary>Seconds the best round takes from the entrance (walking, with the reach).</summary>
        public float Best;
    }

    private sealed class View
    {
        public readonly List<Node3D> Flames = new();
        public readonly List<StandardMaterial3D> FlameMats = new();
        public readonly List<Node3D> Rings = new();
        public readonly List<OmniLight3D> Lights = new();
    }

    private static readonly int[] Slots = { PuzzleState.RA, PuzzleState.RB, PuzzleState.RC, PuzzleState.RD, PuzzleState.RX, PuzzleState.RY };

    private const string Verb = "Light the brazier";
    private const string LitLine = "Braziers burning: {0}/{1}  ·  each burns {2}s";

    public override string Id => "braziers";
    public override string Title => "Ember Hall";
    public override string Objective => "Light every brazier so that all of them burn at the same time. Each one burns out after a while.";
    public override string Hint => "Hint: light the marked brazier next: it starts the shortest round.";
    public override IEnumerable<string> Strings => new[] { Verb, LitLine };
    public override HallStyle Style => HallStyle.Ember;
    public override float ParSeconds => 24f;

    /// <summary>Seconds spent at each brazier (stop, press).</summary>
    private const float AtBrazier = 0.5f;
    /// <summary>Metres saved at each end of a leg: interact reaches that far.</summary>
    private const float ReachSave = 1.6f;

    // ------------------------------------------------------------- data

    public override void Generate(RoomPlan room, Rng rng, int d)
    {
        var l = new Data();
        room.Data = l;
        int k = 4 + Math.Min(d, 2);
        var doors = new List<Vector2>();
        foreach (int side in new[] { room.EntrySide, room.ExitSide })
            if (side >= 0) doors.Add(RoomPlan.Local(RoomPlan.DoorCell(side)));
        for (int attempt = 0; attempt < 300; attempt++)
        {
            var spots = new List<Vector2>();
            for (int tries = 0; tries < 200 && spots.Count < k; tries++)
            {
                var p = new Vector2(rng.Range(-7.4f, 7.4f), rng.Range(-7.4f, 7.4f));
                if (PuzzleGen.NearAny(p, spots, 4.2f) || PuzzleGen.NearAny(p, doors, 3.4f) || p.Length() < 1.5f) continue;
                spots.Add(p);
            }
            if (spots.Count < k) continue;
            l.Spots = spots.ToArray();
            Vector2 entry = doors.Count > 0 ? doors[0] : Vector2.Zero;
            l.Best = RoundSeconds(l.Spots, entry, BestOrder(l.Spots, entry, null, out _));
            float worst = MedianSeconds(l.Spots, entry, rng);
            // Burn: the best round with a margin that shrinks with difficulty.
            l.Burn = Mathf.Ceil(l.Best * (1.75f - 0.07f * d) + 2.5f);
            // A puzzle, not a stroll: a typical round must not make it as well.
            if (worst <= l.Burn && attempt < 250) continue;
            break;
        }
        foreach (Vector2 p in l.Spots) room.Spots.Add(p);
    }

    /// <summary>Seconds to walk the round in <paramref name="order"/> from <paramref name="from"/> and light each.</summary>
    private static float RoundSeconds(Vector2[] spots, Vector2 from, int[] order)
    {
        float metres = 0f;
        Vector2 at = from;
        foreach (int i in order)
        {
            metres += Mathf.Max(0f, at.DistanceTo(spots[i]) - (at == from ? ReachSave : 2f * ReachSave));
            at = spots[i];
        }
        return metres / PuzzleSolver.WalkSpeed + order.Length * AtBrazier;
    }

    /// <summary>The order of <paramref name="which"/> (all when null) with the
    /// shortest round from <paramref name="from"/>.</summary>
    public static int[] BestOrder(Vector2[] spots, Vector2 from, List<int>? which, out float seconds)
    {
        var items = which ?? new List<int>();
        if (which is null) for (int i = 0; i < spots.Length; i++) items.Add(i);
        int[] best = items.ToArray();
        float bestS = float.MaxValue;
        var perm = items.ToArray();
        void Go(int n)
        {
            if (n == perm.Length)
            {
                float s = RoundSeconds(spots, from, perm);
                if (s < bestS) { bestS = s; best = (int[])perm.Clone(); }
                return;
            }
            for (int i = n; i < perm.Length; i++)
            {
                (perm[n], perm[i]) = (perm[i], perm[n]);
                Go(n + 1);
                (perm[n], perm[i]) = (perm[i], perm[n]);
            }
        }
        Go(0);
        seconds = items.Count == 0 ? 0f : bestS;
        return best;
    }

    private static float MedianSeconds(Vector2[] spots, Vector2 from, Rng rng)
    {
        var times = new List<float>();
        var order = new List<int>();
        for (int i = 0; i < spots.Length; i++) order.Add(i);
        for (int n = 0; n < 31; n++)
        {
            PuzzleGen.Shuffle(rng, order);
            times.Add(RoundSeconds(spots, from, order.ToArray()));
        }
        times.Sort();
        return times[times.Count / 2];
    }

    private static int Until(PuzzleState st, RoomPlan room, int i) => st.Get(room, Slots[i]);

    public static bool Burning(PuzzleState st, RoomPlan room, int i, float now) => st.Solved(room) || Until(st, room, i) > PuzzleState.Tenths(now);

    public override bool Act(PuzzleState st, RoomPlan room, int r, PuzzleAction action, int a, int b, float now)
    {
        if (action != PuzzleAction.Use) return false;
        Data l = room.Layout<Data>();
        if (a < 0 || a >= l.Spots.Length) return true;
        st.SetR(room, Slots[a], PuzzleState.Tenths(now + l.Burn));
        bool all = true;
        for (int i = 0; i < l.Spots.Length; i++) all &= Burning(st, room, i, now);
        if (all) st.MarkSolved(r, now);
        return true;
    }

    /// <summary>What to light next from <paramref name="from"/>: the best
    /// order over every brazier that is out or will be out before the round
    /// could end.</summary>
    public static int Next(PuzzleState st, RoomPlan room, Vector2 from, float now)
    {
        Data l = room.Layout<Data>();
        var todo = new List<int>();
        for (int i = 0; i < l.Spots.Length; i++) if (!Burning(st, room, i, now)) todo.Add(i);
        if (todo.Count == 0) return -1;
        BestOrder(l.Spots, from, todo, out float seconds);
        // Burning ones that will be out before the rest are lit go back on the list.
        for (int i = 0; i < l.Spots.Length; i++)
            if (!todo.Contains(i) && Until(st, room, i) / 10f < now + seconds + 0.5f) todo.Add(i);
        if (todo.Count == l.Spots.Length && todo.Count > 0)
        {
            // Starting over: light the round in its best order.
            return BestOrder(l.Spots, from, null, out _)[0];
        }
        return BestOrder(l.Spots, from, todo, out _)[0];
    }

    public override async Task<string?> Solve(IPuzzleDriver d, RoomPlan room, int r)
    {
        Data l = room.Layout<Data>();
        if (l.Spots.Length < 3) return "too few braziers";
        Vector2 entry = RoomPlan.Local(room.EntryCell);
        int[] order = BestOrder(l.Spots, entry, null, out float seconds);
        if (seconds * PuzzleSolver.TimingMargin > l.Burn) return $"burn {l.Burn:0.0}s leaves no margin over the best round {seconds:0.0}s";
        await d.Stand(room.World(room.EntryCell));
        Vector2 at = entry;
        foreach (int i in order)
        {
            // Walk there (the time a player needs), then light it.
            float leg = Mathf.Max(0f, at.DistanceTo(l.Spots[i]) - ReachSave) / PuzzleSolver.WalkSpeed + AtBrazier;
            await d.Wait(leg);
            await d.Ask(PuzzleAction.Use, i);
            at = l.Spots[i];
        }
        return null;
    }

    // ------------------------------------------------------------ scene

    public override object Build(PieceKit kit, RoomPlan room)
    {
        Data l = room.Layout<Data>();
        var v = new View();
        var iron = PieceKit.Flat(new Color(0.20f, 0.18f, 0.17f));
        for (int i = 0; i < l.Spots.Length; i++)
        {
            Vector3 at = room.World(l.Spots[i]);
            kit.Put(PieceKit.Mesh(new CylinderMesh { TopRadius = 0.14f, BottomRadius = 0.4f, Height = 1.1f, RadialSegments = 8 }, at + Vector3.Up * 0.55f, iron));
            kit.Put(PieceKit.Mesh(new CylinderMesh { TopRadius = 0.7f, BottomRadius = 0.3f, Height = 0.45f, RadialSegments = 14 }, at + Vector3.Up * 1.3f, iron));
            // Embers in the bowl: always there, so an unlit brazier still reads as one.
            kit.Put(PieceKit.Mesh(new SphereMesh { Radius = 0.45f, Height = 0.2f, RadialSegments = 10, Rings = 3 }, at + Vector3.Up * 1.5f, PieceKit.Flat(new Color(0.25f, 0.12f, 0.08f)), false));
            var mat = PieceKit.Glow(PieceKit.Flame, 3f);
            var flame = new Node3D { Position = at + Vector3.Up * 1.55f, Scale = new Vector3(1f, 0.01f, 1f) };
            kit.Put(flame);
            flame.AddChild(PieceKit.Mesh(new CylinderMesh { TopRadius = 0f, BottomRadius = 0.48f, Height = 1.5f, RadialSegments = 8 }, Vector3.Up * 0.75f, mat, false));
            flame.AddChild(PieceKit.Mesh(new SphereMesh { Radius = 0.42f, Height = 0.6f, RadialSegments = 8, Rings = 4 }, Vector3.Up * 0.15f, mat, false));
            flame.Visible = false;
            v.Flames.Add(flame);
            v.FlameMats.Add(mat);
            // A ring on the floor that shrinks as the fire burns down.
            var ring = PieceKit.Mesh(new TorusMesh { InnerRadius = 0.95f, OuterRadius = 1.12f, Rings = 28, RingSegments = 6 }, at + Vector3.Up * 0.04f, PieceKit.Glow(PieceKit.Warm, 1.4f), false);
            ring.Visible = false;
            kit.Put(ring);
            v.Rings.Add(ring);
            var light = new OmniLight3D { Position = at + Vector3.Up * 2.4f, LightColor = PieceKit.Flame, LightEnergy = 0f, OmniRange = 5f, ShadowEnabled = false, Visible = false };
            kit.Put(light);
            v.Lights.Add(light);
            kit.Post(at, 0.55f, 1.6f);
        }
        return v;
    }

    public override void Sync(PieceKit kit, object view, RoomPlan room, PuzzleState st, float t, float dt, bool changed)
    {
        var v = (View)view;
        Data l = room.Layout<Data>();
        bool solved = st.Solved(room);
        for (int i = 0; i < l.Spots.Length; i++)
        {
            float left = solved ? l.Burn : Until(st, room, i) / 10f - t;
            bool on = solved || left > 0f;
            float frac = on ? Mathf.Clamp(left / l.Burn, 0.15f, 1f) : 0f;
            Node3D flame = v.Flames[i];
            flame.Visible = on;
            float flicker = 1f + 0.08f * Mathf.Sin(t * 13f + i * 1.7f);
            flame.Scale = new Vector3(0.6f + 0.4f * frac, Mathf.Max(0.01f, frac * flicker), 0.6f + 0.4f * frac);
            v.Rings[i].Visible = on && !solved;
            if (on) v.Rings[i].Scale = new Vector3(frac, 1f, frac);
            // Only the room the player is in has its lights on (they are cheap, but there are six).
            v.Lights[i].Visible = on;
            v.Lights[i].LightEnergy = on ? 1.2f * frac : 0f;
        }
    }

    public override void Sounds(object view, RoomPlan room, Func<int, int> was, Func<int, int> now)
    {
        foreach (int s in Slots)
            if (now(s) > was(s)) { AudioDirector.PlayPuzzle(PuzzleSound.Gust, 1.5f); break; }
    }

    public override void Offer(PuzzleMap map, RoomPlan room, FocusOffer offer)
    {
        Data l = room.Layout<Data>();
        for (int i = 0; i < l.Spots.Length; i++) offer(PuzzleAction.Use, i, room.World(l.Spots[i]), Verb);
    }

    private static Vector2 LocalOf(RoomPlan room, Vector3? me)
        => me is { } at ? new Vector2(at.X - room.Centre.X, at.Z - room.Centre.Z) : RoomPlan.Local(room.EntryCell);

    public override PuzzleHint? HintAt(PuzzleState st, RoomPlan room, Vector3? me, float now)
    {
        int i = Next(st, room, LocalOf(room, me), now);
        return i < 0 ? null : new PuzzleHint(room.World(room.Layout<Data>().Spots[i]), "!");
    }

    public override string Status(PuzzleMap map, RoomPlan room)
    {
        Data l = room.Layout<Data>();
        int n = 0;
        for (int i = 0; i < l.Spots.Length; i++) if (Burning(map.State, room, i, map.Elapsed)) n++;
        return Meta.Loc.F(LitLine, n, l.Spots.Length, Mathf.RoundToInt(l.Burn));
    }

    public override Vector3 Bot(PuzzleBot bot, RoomPlan room, Vector3 me)
    {
        int i = Next(bot.State, room, LocalOf(room, me), bot.Map.Elapsed);
        if (i < 0) return Vector3.Zero;
        Vector3 at = room.World(room.Layout<Data>().Spots[i]);
        if (bot.PressIf(PuzzleAction.Use, i, room.Index)) return Vector3.Zero;
        // Braziers stand in the open: walk straight at it.
        return PuzzleBot.Toward(me, at, 1.2f);
    }

    public override void Glyph(CanvasItem ci, Vector2 at, float size, Color c)
    {
        float h = size * 0.65f;
        ci.DrawPolyline(new[] { at + new Vector2(0f, -h), at + new Vector2(h * 0.55f, h * 0.35f), at + new Vector2(0f, h * 0.7f), at + new Vector2(-h * 0.55f, h * 0.35f), at + new Vector2(0f, -h) }, c, Mathf.Max(1f, size * 0.16f));
    }
}
