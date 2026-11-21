using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Audio;
using Wavebreak.Core;

namespace Wavebreak.Game.Puzzle.Kinds;

/// <summary>
/// Gust Room: numbered plates, some on islands inside rings of gusting wind,
/// each ring on its own calm/gust cycle. Step on the plates in number order.
/// A plate out of order sends the count back to 1; from difficulty 2 so does
/// being caught in a ring while it gusts (the gust also shoves you out); from
/// difficulty 3 the whole sequence must be done within a time limit counted
/// from plate 1. The order is shuffled against the layout, so the walk zigzags
/// between rings whose calms do not line up: the puzzle is planning a route
/// and the waits so every crossing falls in a calm, without treading on a
/// plate that is not next. Difficulty adds plates (3 to 7) and rings (2 to
/// 4), shortens the calms, and tightens the limit.
///
/// State: RA the next plate (count done); RB the limit's end (tenths, 0 when
/// not running); RPressed plates stood on now (bits); RFails resets (sound).
/// Solver: the plates in order, waiting at each ring's edge for a calm long
/// enough to cross (an exact walk on the cycles); the limit is fitted to the
/// slowest such walk over every starting phase.
/// </summary>
public sealed class WindKind : PuzzleKind
{
    public sealed class Data
    {
        /// <summary>Plate centres, in the order they are pressed.</summary>
        public Vector2[] Plates = Array.Empty<Vector2>();
        /// <summary>Per plate: the ring it sits in, or -1 in the open.</summary>
        public int[] RingOf = Array.Empty<int>();
        public Vector2[] Centres = Array.Empty<Vector2>();
        public float[] Outer = Array.Empty<float>();
        public float[] Calm = Array.Empty<float>(), Gust = Array.Empty<float>(), Phase = Array.Empty<float>();
        public float Inner = 1.3f;
        /// <summary>Caught in a gusting ring: back to plate 1.</summary>
        public bool BandReset;
        /// <summary>Seconds from plate 1 to the last; 0: no limit.</summary>
        public float Window;
    }

    private sealed class View
    {
        public readonly List<PlateView> Plates = new();
        public readonly List<Label3D> Numbers = new();
        public readonly List<List<Node3D>> Arrows = new();
        public readonly List<StandardMaterial3D> RingMats = new();
        public float GustSoundIn;
    }

    private const string GustLine = "GUST  ·  calm in {0}s";
    private const string CalmLine = "CALM  ·  gust in {0}s";
    private const string NextLine = "next plate: {0} of {1}";
    private const string WindowLine = "{0}s left";
    private const string WindowIdleLine = "limit {0}s from plate 1";

    public override string Id => "wind";
    public override string Title => "Gust Room";
    public override string Objective => "Step on the numbered plates in order. A plate out of order sends you back to 1; in harder rooms so does a gust that catches you in its ring, or running out of time.";
    public override string Hint => "Hint: the marked plate is next. Cross a ring only while its arrows lie flat.";
    public override IEnumerable<string> Strings => new[] { GustLine, CalmLine, NextLine, WindowLine, WindowIdleLine };
    public override HallStyle Style => HallStyle.Gallery;
    public override float ParSeconds => 45f;

    private const float PlateR = 1.0f;

    private static readonly int[] PlateCount = { 3, 4, 5, 5, 6, 7 };
    private static readonly int[] RingCount = { 2, 2, 3, 3, 4, 4 };

    // ------------------------------------------------------------- data

    public override void Generate(RoomPlan room, Rng rng, int d)
    {
        d = Math.Clamp(d, 0, 5);
        var l = new Data();
        room.Data = l;
        l.BandReset = d >= 2;
        var doors = new List<Vector2>();
        foreach (int side in new[] { room.EntrySide, room.ExitSide })
            if (side >= 0) doors.Add(RoomPlan.Local(RoomPlan.DoorCell(side)));
        for (int attempt = 0; attempt < 600; attempt++)
        {
            int plates = attempt < 450 ? PlateCount[d] : Math.Max(3, PlateCount[d] - 1);
            int rings = Math.Min(plates - 1, attempt < 450 ? RingCount[d] : Math.Max(2, RingCount[d] - 1));
            if (TryLayout(l, rng, d, plates, rings, doors)) { Finish(l, d); return; }
        }
        // Fallback: two rings and an open plate, in a line across the room.
        l.Centres = new[] { new Vector2(-4.5f, -3.5f), new Vector2(4.5f, 3.5f) };
        l.Outer = new[] { 3f, 3f };
        l.Calm = new[] { 2.8f, 2.4f };
        l.Gust = new[] { 2f, 2.2f };
        l.Phase = new[] { 0f, 1.7f };
        l.Plates = new[] { l.Centres[1], l.Centres[0], new Vector2(4.5f, -4.5f) };
        l.RingOf = new[] { 1, 0, -1 };
        if (PuzzleGen.NearAny(l.Plates[2], doors, 2.6f)) l.Plates[2] = new Vector2(-4.5f, 4.5f);
        Finish(l, d);
    }

    private static bool TryLayout(Data l, Rng rng, int d, int plates, int rings, List<Vector2> doors)
    {
        var centres = new List<Vector2>();
        var outer = new List<float>();
        for (int tries = 0; tries < 200 && centres.Count < rings; tries++)
        {
            float o = rng.Range(2.7f, 3.3f);
            float lim = 8.6f - o;
            var c = new Vector2(rng.Range(-lim, lim), rng.Range(-lim, lim));
            bool bad = PuzzleGen.NearAny(c, doors, o + 2.4f);
            for (int i = 0; i < centres.Count && !bad; i++) bad = c.DistanceTo(centres[i]) < o + outer[i] + 1.6f;
            if (bad) continue;
            centres.Add(c);
            outer.Add(o);
        }
        if (centres.Count < rings) return false;
        var open = new List<Vector2>();
        for (int tries = 0; tries < 200 && open.Count < plates - rings; tries++)
        {
            var p = new Vector2(rng.Range(-7f, 7f), rng.Range(-7f, 7f));
            bool bad = PuzzleGen.NearAny(p, doors, 2.8f) || PuzzleGen.NearAny(p, open, 3.2f);
            for (int i = 0; i < centres.Count && !bad; i++) bad = p.DistanceTo(centres[i]) < outer[i] + 2.2f;
            if (bad) continue;
            open.Add(p);
        }
        if (open.Count < plates - rings) return false;
        // The order: a shuffle, kept only if no two plates in a row share a
        // ring-free straight walk that a good route would take anyway.
        var all = new List<(Vector2 P, int Ring)>();
        for (int i = 0; i < rings; i++) all.Add((centres[i], i));
        foreach (Vector2 p in open) all.Add((p, -1));
        for (int shuffle = 0; shuffle < 40; shuffle++)
        {
            PuzzleGen.Shuffle(rng, all);
            if (!Walkable(all, centres, outer)) continue;
            // Zigzag: the order must walk well over the shortest tour of the plates.
            float walk = 0f;
            for (int i = 1; i < all.Count; i++) walk += all[i - 1].P.DistanceTo(all[i].P);
            if (walk < Greedy(all) * (1.15f + 0.04f * d) && shuffle < 30) continue;
            l.Centres = centres.ToArray();
            l.Outer = outer.ToArray();
            l.Plates = new Vector2[all.Count];
            l.RingOf = new int[all.Count];
            for (int i = 0; i < all.Count; i++) { l.Plates[i] = all[i].P; l.RingOf[i] = all[i].Ring; }
            l.Calm = new float[rings];
            l.Gust = new float[rings];
            l.Phase = new float[rings];
            float calmLo = Mathf.Max(1.3f, 2.7f - 0.28f * d), calmHi = calmLo + 0.7f;
            for (int i = 0; i < rings; i++)
            {
                l.Calm[i] = Mathf.Snapped(rng.Range(calmLo, calmHi), 0.1f);
                l.Gust[i] = Mathf.Snapped(rng.Range(1.6f + 0.15f * d, 2.4f + 0.2f * d), 0.1f);
                l.Phase[i] = rng.Range(0f, l.Calm[i] + l.Gust[i]);
            }
            if (!Crossable(l)) return false;
            return true;
        }
        return false;
    }

    /// <summary>Each step from one plate to the next walks straight without
    /// touching another ring or another plate.</summary>
    private static bool Walkable(List<(Vector2 P, int Ring)> order, List<Vector2> centres, List<float> outer)
    {
        for (int i = 1; i < order.Count; i++)
        {
            Vector2 a = order[i - 1].P, b = order[i].P;
            for (int r = 0; r < centres.Count; r++)
            {
                if (r == order[i - 1].Ring || r == order[i].Ring) continue;
                if (SegmentDistance(centres[r], a, b) < outer[r] + 0.4f) return false;
            }
            for (int j = 0; j < order.Count; j++)
            {
                if (j == i || j == i - 1) continue;
                if (SegmentDistance(order[j].P, a, b) < PlateR + 0.9f) return false;
            }
        }
        return true;
    }

    private static float Greedy(List<(Vector2 P, int Ring)> plates)
    {
        // The shortest of the nearest-neighbour tours, a fair stand-in for the best one.
        float best = float.MaxValue;
        for (int s = 0; s < plates.Count; s++)
        {
            var left = new List<Vector2>();
            foreach (var p in plates) left.Add(p.P);
            Vector2 at = left[s];
            left.RemoveAt(s);
            float len = 0f;
            while (left.Count > 0)
            {
                int n = 0;
                for (int i = 1; i < left.Count; i++) if (at.DistanceTo(left[i]) < at.DistanceTo(left[n])) n = i;
                len += at.DistanceTo(left[n]);
                at = left[n];
                left.RemoveAt(n);
            }
            best = Mathf.Min(best, len);
        }
        return best;
    }

    public static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = ab.LengthSquared() < 1e-4f ? 0f : Mathf.Clamp((p - a).Dot(ab) / ab.LengthSquared(), 0f, 1f);
        return p.DistanceTo(a + ab * t);
    }

    private static void Finish(Data l, int d)
    {
        l.Window = 0f;
        if (d < 3) return;
        // The slowest exact walk over every starting phase, with a margin.
        float worst = 0f;
        for (float t0 = 0f; t0 < 40f; t0 += 0.37f) worst = Mathf.Max(worst, Walk(l, t0, null));
        float factor = d >= 5 ? 1.3f : d >= 4 ? 1.4f : 1.55f;
        l.Window = Mathf.Ceil(worst * factor + 2.5f);
    }

    /// <summary>Seconds into the calm/gust cycle of ring <paramref name="i"/> (calm first).</summary>
    public static float Cycle(Data l, int i, float t)
    {
        float period = l.Calm[i] + l.Gust[i];
        float p = (t + l.Phase[i]) % period;
        return p < 0f ? p + period : p;
    }

    public static bool Gusting(Data l, int i, float t) => Cycle(l, i, t) >= l.Calm[i];

    /// <summary>Seconds a body needs to cross ring <paramref name="i"/>'s band, with the margin.</summary>
    private static float Need(Data l, int i) => (l.Outer[i] - l.Inner + 0.6f) / PuzzleSolver.WalkSpeed * PuzzleSolver.TimingMargin;

    /// <summary>Every ring's calm leaves time to cross its band.</summary>
    public static bool Crossable(Data l)
    {
        for (int i = 0; i < l.Centres.Length; i++) if (Need(l, i) + 0.3f > l.Calm[i]) return false;
        return l.Plates.Length > 0;
    }

    /// <summary>The first moment from <paramref name="t"/> with enough calm left in ring <paramref name="i"/> to cross.</summary>
    public static float CalmFrom(Data l, int i, float t)
    {
        float c = Cycle(l, i, t);
        float need = Need(l, i);
        if (c < l.Calm[i] - need) return t;
        return t + (l.Calm[i] + l.Gust[i] - c) + 0.05f;
    }

    /// <summary>One step of the walk: from plate <paramref name="a"/> (-1: a point) to plate <paramref name="b"/>.</summary>
    private readonly record struct Step(Vector2 OutEdge, float OutWait, float OutCross, float Middle, Vector2 InEdge, float InWait, float InCross);

    /// <summary>Walk the plates in order from <paramref name="t0"/> (on plate 0
    /// already), exactly as <see cref="Solve"/> does; returns the seconds to
    /// the last plate. <paramref name="steps"/> gets each leg.</summary>
    private static float Walk(Data l, float t0, List<Step>? steps)
    {
        float t = t0;
        for (int i = 1; i < l.Plates.Length; i++) t = Leg(l, l.Plates[i - 1], l.RingOf[i - 1], i, t, steps);
        return t - t0;
    }

    private static float Leg(Data l, Vector2 from, int fromRing, int b, float t, List<Step>? steps)
    {
        Vector2 to = l.Plates[b];
        int toRing = l.RingOf[b];
        float speed = PuzzleSolver.WalkSpeed;
        Vector2 dir = (to - from).Length() > 0.01f ? (to - from).Normalized() : Vector2.Right;
        float total = from.DistanceTo(to);
        float outWait = 0f, outCross = 0f, inWait = 0f, inCross = 0f;
        Vector2 outEdge = from, inEdge = to;
        float used = 0f;
        if (fromRing >= 0)
        {
            float t1 = CalmFrom(l, fromRing, t);
            outWait = t1 - t;
            outCross = (l.Outer[fromRing] + 0.6f) / speed;
            t = t1 + outCross;
            outEdge = l.Centres[fromRing] + dir * (l.Outer[fromRing] + 0.6f);
            used += l.Outer[fromRing] + 0.6f;
        }
        float inside = toRing >= 0 ? l.Outer[toRing] + 0.6f : 0f;
        float middle = Mathf.Max(0f, total - used - inside) / speed;
        t += middle;
        if (toRing >= 0)
        {
            inEdge = l.Centres[toRing] - dir * (l.Outer[toRing] + 0.6f);
            float t2 = CalmFrom(l, toRing, t);
            inWait = t2 - t;
            inCross = inside / speed;
            t = t2 + inCross;
        }
        steps?.Add(new Step(outEdge, outWait, outCross, middle, inEdge, inWait, inCross));
        return t;
    }

    // ------------------------------------------------------------ rules

    private static int OnPlates(Data l, RoomPlan room, IReadOnlyList<Vector3> players)
    {
        int on = 0;
        for (int i = 0; i < l.Plates.Length; i++)
            foreach (Vector3 p in players)
            {
                Vector3 d = p - room.World(l.Plates[i]);
                if (new Vector2(d.X, d.Z).Length() <= PlateR && d.Y < 1.6f) on |= 1 << i;
            }
        return on;
    }

    /// <summary>Is this body in ring <paramref name="i"/>'s band (not its island)?</summary>
    private static bool InBand(Data l, RoomPlan room, int i, Vector3 p)
    {
        Vector3 d = p - room.World(l.Centres[i]);
        float dist = new Vector2(d.X, d.Z).Length();
        return dist > l.Inner + 0.25f && dist < l.Outer[i] - 0.25f && d.Y < 2.5f;
    }

    private static void Reset(PuzzleState st, RoomPlan room)
    {
        if (st.Get(room, PuzzleState.RA) == 0 && st.Get(room, PuzzleState.RB) == 0) return;
        st.SetR(room, PuzzleState.RA, 0);
        st.SetR(room, PuzzleState.RB, 0);
        st.SetR(room, PuzzleState.RFails, st.Get(room, PuzzleState.RFails) + 1);
    }

    public override void HostStep(PuzzleState st, RoomPlan room, int r, IReadOnlyList<Vector3> players, float now)
    {
        Data l = room.Layout<Data>();
        int on = OnPlates(l, room, players);
        int was = st.Get(room, PuzzleState.RPressed);
        st.SetR(room, PuzzleState.RPressed, on);
        int next = st.Get(room, PuzzleState.RA);
        if (l.BandReset && next > 0)
            for (int i = 0; i < l.Centres.Length; i++)
            {
                if (!Gusting(l, i, now)) continue;
                foreach (Vector3 p in players)
                    if (InBand(l, room, i, p)) { Reset(st, room); next = 0; break; }
            }
        int end = st.Get(room, PuzzleState.RB);
        if (end > 0 && PuzzleState.Tenths(now) > end) { Reset(st, room); next = 0; }
        int fresh = on & ~was;
        for (int i = 0; i < l.Plates.Length; i++)
        {
            if ((fresh >> i & 1) == 0 || i < next) continue;
            if (i == next)
            {
                next++;
                st.SetR(room, PuzzleState.RA, next);
                if (next == 1 && l.Window > 0f) st.SetR(room, PuzzleState.RB, PuzzleState.Tenths(now + l.Window));
            }
            else
            {
                Reset(st, room);
                next = 0;
                break;
            }
        }
        if (next >= l.Plates.Length)
        {
            st.SetR(room, PuzzleState.RB, 0);
            st.MarkSolved(r, now);
        }
    }

    public override async Task<string?> Solve(IPuzzleDriver d, RoomPlan room, int r)
    {
        Data l = room.Layout<Data>();
        if (!Crossable(l)) return "calm too short to cross a ring";
        // Plate 1, reached from the entrance (the limit starts there).
        Vector2 entry = RoomPlan.Local(room.EntryCell);
        await GoTo(d, room, l, entry, -1, 0);
        float began = d.Now;
        for (int i = 1; i < l.Plates.Length; i++) await GoTo(d, room, l, l.Plates[i - 1], l.RingOf[i - 1], i);
        if (l.Window > 0f && d.Now - began > l.Window) return $"walk {d.Now - began:0.0}s over the limit {l.Window:0}s";
        return null;
    }

    /// <summary>Walk to plate <paramref name="b"/> the way <see cref="Walk"/> times it.</summary>
    private static async Task GoTo(IPuzzleDriver d, RoomPlan room, Data l, Vector2 from, int fromRing, int b)
    {
        var steps = new List<Step>();
        Leg(l, from, fromRing, b, d.Now, steps);
        Step s = steps[0];
        if (fromRing >= 0)
        {
            await d.Wait(s.OutWait);
            await d.Wait(s.OutCross);
            await d.Stand(room.World(s.OutEdge));
        }
        await d.Wait(s.Middle);
        if (l.RingOf[b] >= 0)
        {
            await d.Stand(room.World(s.InEdge));
            // Recompute the calm from the real clock (a Stand takes a moment).
            float t2 = CalmFrom(l, l.RingOf[b], d.Now);
            await d.Wait(t2 - d.Now);
            await d.Wait(s.InCross);
        }
        await d.Stand(room.World(l.Plates[b]));
    }

    // ------------------------------------------------------------ scene

    public override object Build(PieceKit kit, RoomPlan room)
    {
        Data l = room.Layout<Data>();
        var v = new View();
        for (int i = 0; i < l.Centres.Length; i++)
        {
            Vector3 c = room.World(l.Centres[i]);
            var ringMat = PieceKit.Flat(new Color(0.40f, 0.68f, 0.76f));
            var ring = PieceKit.Mesh(new TorusMesh { InnerRadius = l.Inner, OuterRadius = l.Outer[i], Rings = 64, RingSegments = 8 }, c + Vector3.Up * 0.02f, ringMat, false);
            ring.Scale = new Vector3(1f, 0.03f, 1f);
            kit.Put(ring);
            v.RingMats.Add(ringMat);
            var arrows = new List<Node3D>();
            int count = Mathf.RoundToInt(l.Outer[i] * 2.2f);
            for (int k = 0; k < count; k++)
            {
                float a = Mathf.Tau * k / count;
                var arrow = new Node3D { Position = c, Rotation = new Vector3(0f, -a, 0f) };
                kit.Put(arrow);
                // A flat arrowhead pointing outward along +X.
                var head = PieceKit.Mesh(new CylinderMesh { TopRadius = 0f, BottomRadius = 0.28f, Height = 0.6f, RadialSegments = 3 }, Vector3.Zero, PieceKit.Glow(new Color(0.85f, 0.97f, 1f), 1.2f), false);
                head.Rotation = new Vector3(0f, 0f, -Mathf.Pi * 0.5f);
                arrow.AddChild(head);
                arrows.Add(arrow);
            }
            v.Arrows.Add(arrows);
        }
        for (int i = 0; i < l.Plates.Length; i++)
        {
            Vector3 at = room.World(l.Plates[i]);
            v.Plates.Add(kit.Plate(at, round: true));
            v.Numbers.Add(kit.Label((i + 1).ToString(), at + Vector3.Up * 1.5f, 96));
        }
        return v;
    }

    public override void Sync(PieceKit kit, object view, RoomPlan room, PuzzleState st, float t, float dt, bool changed)
    {
        var v = (View)view;
        Data l = room.Layout<Data>();
        bool solved = st.Solved(room);
        int next = solved ? l.Plates.Length : st.Get(room, PuzzleState.RA);
        for (int i = 0; i < l.Plates.Length; i++)
        {
            PieceKit.DrawPlate(v.Plates[i], i < next, dt);
            // Done plates' numbers fade; the next one is the brightest.
            v.Numbers[i].Modulate = i < next ? new Color(0.6f, 0.9f, 0.85f, 0.55f) : i == next ? new Color(1f, 0.97f, 0.7f) : new Color(1f, 0.97f, 0.88f, 0.85f);
            v.Numbers[i].Scale = Vector3.One * (i == next ? 1.25f : 1f);
        }
        for (int i = 0; i < l.Centres.Length; i++)
        {
            float cycle = Cycle(l, i, t);
            bool gust = cycle >= l.Calm[i] && !solved;
            bool warn = !gust && cycle >= l.Calm[i] - 0.6f && !solved;
            float span = l.Outer[i] - l.Inner;
            for (int k = 0; k < v.Arrows[i].Count; k++)
            {
                var head = v.Arrows[i][k].GetChild<Node3D>(0);
                float run = gust ? ((t * 2.4f + k * 0.37f) % 1f) : 0.5f;
                float height = gust ? 0.9f : warn ? 0.45f + 0.15f * Mathf.Sin(t * 30f) : 0.08f;
                head.Position = new Vector3(l.Inner + 0.4f + run * (span - 0.8f), height, 0f);
                head.Scale = Vector3.One * (gust ? 1.2f : 0.8f);
            }
            v.RingMats[i].AlbedoColor = gust ? new Color(0.62f, 0.88f, 0.98f) : new Color(0.40f, 0.68f, 0.76f);
        }
    }

    public override void Sounds(object view, RoomPlan room, Func<int, int> was, Func<int, int> now)
    {
        if (now(PuzzleState.RFails) > was(PuzzleState.RFails)) AudioDirector.PlayPuzzle(PuzzleSound.Click, 0.6f);
        else if (now(PuzzleState.RA) > was(PuzzleState.RA)) AudioDirector.PlayPuzzle(PuzzleSound.Click, 1f + 0.08f * now(PuzzleState.RA));
    }

    /// <summary>Gusts shove this player out of the ring while they blow.</summary>
    public override void LocalStep(PuzzleMap map, object view, Player me, RoomPlan room, int r, float dt, bool control)
    {
        var v = (View)view;
        Data l = room.Layout<Data>();
        Vector3 p = me.GlobalPosition;
        for (int i = 0; i < l.Centres.Length; i++)
        {
            Vector3 d = p - room.World(l.Centres[i]);
            d.Y = 0f;
            float dist = d.Length();
            if (dist <= l.Inner || dist >= l.Outer[i] + 0.3f || p.Y > 2.5f) continue;
            if (!Gusting(l, i, map.Elapsed)) continue;
            Vector3 outward = dist > 0.01f ? d / dist : Vector3.Right;
            me.ApplyKnockback(outward * 52f * dt);
            v.GustSoundIn -= dt;
            if (v.GustSoundIn <= 0f)
            {
                v.GustSoundIn = 1.0f;
                AudioDirector.PlayPuzzle(PuzzleSound.Gust);
            }
        }
    }

    public override PuzzleHint? HintAt(PuzzleState st, RoomPlan room, Vector3? me, float now)
    {
        Data l = room.Layout<Data>();
        int next = st.Get(room, PuzzleState.RA);
        if (next >= l.Plates.Length) return null;
        return new PuzzleHint(room.World(l.Plates[next]), (next + 1).ToString());
    }

    public override string Status(PuzzleMap map, RoomPlan room)
    {
        Data l = room.Layout<Data>();
        PuzzleState st = map.State;
        int next = Math.Min(st.Get(room, PuzzleState.RA), l.Plates.Length - 1);
        string line = Meta.Loc.F(NextLine, next + 1, l.Plates.Length);
        // The ring that matters now: the next plate's, or the nearest to the player.
        int ring = l.RingOf[next];
        if (ring < 0 && Player.Instance is { } me)
        {
            float best = float.MaxValue;
            for (int i = 0; i < l.Centres.Length; i++)
            {
                float dd = room.World(l.Centres[i]).DistanceTo(me.GlobalPosition);
                if (dd < best) { best = dd; ring = i; }
            }
        }
        if (ring >= 0)
        {
            float cycle = Cycle(l, ring, map.Elapsed);
            line += "  ·  " + (cycle >= l.Calm[ring]
                ? Meta.Loc.F(GustLine, (l.Calm[ring] + l.Gust[ring] - cycle).ToString("0.0"))
                : Meta.Loc.F(CalmLine, (l.Calm[ring] - cycle).ToString("0.0")));
        }
        if (l.Window > 0f)
        {
            int end = st.Get(room, PuzzleState.RB);
            line += "  ·  " + (end > 0 ? Meta.Loc.F(WindowLine, Mathf.Max(0f, end / 10f - map.Elapsed).ToString("0.0")) : Meta.Loc.F(WindowIdleLine, Mathf.RoundToInt(l.Window)));
        }
        return line;
    }

    // -------------------------------------------------------------- bot

    public override Vector3 Bot(PuzzleBot bot, RoomPlan room, Vector3 me)
    {
        Data l = room.Layout<Data>();
        int next = bot.State.Get(room, PuzzleState.RA);
        if (next >= l.Plates.Length) return Vector3.Zero;
        float now = bot.Map.Elapsed;
        var at = new Vector2(me.X - room.Centre.X, me.Z - room.Centre.Z);
        Vector2 goal = l.Plates[next];
        int goalRing = l.RingOf[next];
        // Which ring am I in, if any?
        int island = -1, band = -1;
        for (int i = 0; i < l.Centres.Length; i++)
        {
            float dist = at.DistanceTo(l.Centres[i]);
            if (dist <= l.Inner) island = i;
            else if (dist < l.Outer[i] + 0.3f) band = i;
        }
        Vector3 Go(Vector2 p, float stop) => Avoid(room, l, me, at, p, next, stop);
        // Standing on the plate that is next but not counted (the count went back while on it): step off and on again.
        if ((bot.State.Get(room, PuzzleState.RPressed) >> next & 1) != 0)
        {
            Vector2 off = goal + (at.DistanceTo(goal) > 0.05f ? (at - goal).Normalized() : Vector2.Right) * 1.25f;
            return PuzzleBot.Toward(me, room.World(off), 0.05f);
        }
        if (band >= 0)
            // In a band: get out (towards the goal if it is this ring's island, else straight out).
            return band == goalRing ? Go(goal, 0.1f) : Go(l.Centres[band] + (at - l.Centres[band]).Normalized() * (l.Outer[band] + 1f), 0.1f);
        if (island >= 0 && island != goalRing)
        {
            // On an island: leave only with a calm long enough to cross.
            if (CalmFrom(l, island, now) > now) return Vector3.Zero;
            return Go(goal, 0.1f);
        }
        if (goalRing >= 0 && island != goalRing)
        {
            Vector2 c = l.Centres[goalRing];
            Vector2 edge = c + (at.DistanceTo(c) > 0.01f ? (at - c).Normalized() : Vector2.Right) * (l.Outer[goalRing] + 0.8f);
            if (at.DistanceTo(edge) > 0.9f && at.DistanceTo(c) > l.Outer[goalRing] + 1.2f) return Go(edge, 0.3f);
            return CalmFrom(l, goalRing, now) > now ? Vector3.Zero : PuzzleBot.Toward(me, room.World(goal), 0.1f);
        }
        return Go(goal, 0.1f);
    }

    /// <summary>Walk at <paramref name="goal"/>, round any ring or later plate in the way.</summary>
    private static Vector3 Avoid(RoomPlan room, Data l, Vector3 me, Vector2 at, Vector2 goal, int next, float stop)
    {
        Vector2 aim = goal;
        float nearest = float.MaxValue;
        void Check(Vector2 c, float r)
        {
            if (c.DistanceTo(goal) < r + 0.2f || c.DistanceTo(at) < r) return;
            if (SegmentDistance(c, at, goal) >= r) return;
            float dd = at.DistanceTo(c);
            if (dd >= nearest) return;
            nearest = dd;
            Vector2 along = (goal - at).Normalized();
            var side = new Vector2(-along.Y, along.X);
            if ((at - c).Dot(side) < 0f) side = -side;
            aim = c + side * (r + 0.9f);
        }
        for (int i = 0; i < l.Centres.Length; i++)
            if (i != l.RingOf[Math.Min(next, l.Plates.Length - 1)]) Check(l.Centres[i], l.Outer[i] + 0.4f);
        for (int j = next + 1; j < l.Plates.Length; j++) if (l.RingOf[j] < 0) Check(l.Plates[j], PlateR + 0.7f);
        return PuzzleBot.Toward(me, room.World(aim), aim == goal ? stop : 0.2f);
    }

    public override void Glyph(CanvasItem ci, Vector2 at, float size, Color c)
    {
        float w = Mathf.Max(1f, size * 0.14f);
        ci.DrawArc(at, size * 0.3f, 0f, Mathf.Tau, 12, c, w, true);
        ci.DrawArc(at, size * 0.7f, 0.4f, Mathf.Tau - 0.4f, 16, c, w, true);
    }
}
