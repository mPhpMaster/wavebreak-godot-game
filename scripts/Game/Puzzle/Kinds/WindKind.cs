using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Audio;
using Wavebreak.Core;

namespace Wavebreak.Game.Puzzle.Kinds;

/// <summary>
/// Gust Room: plates on islands inside rings of gusting wind. A gust shoves
/// whoever stands in its ring outward (each machine its own body); touch
/// every plate by crossing in the calm. The calm always leaves a margin over
/// a straight walk across the ring.
///
/// State: RA plates touched (bits); RPressed plates stood on now.
/// </summary>
public sealed class WindKind : PuzzleKind
{
    public sealed class Data
    {
        public Vector2[] Centres = Array.Empty<Vector2>();
        public float Inner = 1.2f, Outer = 4f;
        public float Calm = 3f, Gust = 2f;
        public float[] Phase = Array.Empty<float>();
    }

    private sealed class View
    {
        public readonly List<PlateView> Plates = new();
        public readonly List<List<Node3D>> Arrows = new();
        public readonly List<StandardMaterial3D> RingMats = new();
        public float GustSoundIn;
    }

    private const string GustLine = "GUST  ·  calm in {0}s";
    private const string CalmLine = "CALM  ·  gust in {0}s";
    private const string PlatesLine = "plates {0}/{1}";

    public override string Id => "wind";
    public override string Title => "Gust Room";
    public override string Objective => "Cross the gust rings while the wind is calm and touch every plate.";
    public override string Hint => "Hint: wait at the ring's edge until the arrows drop, then run straight in.";
    public override IEnumerable<string> Strings => new[] { GustLine, CalmLine, PlatesLine };
    public override HallStyle Style => HallStyle.Gallery;
    public override bool BotLeaderOnly => false;

    // ------------------------------------------------------------- data

    public override void Generate(RoomPlan room, Rng rng, int d)
    {
        var l = new Data();
        room.Data = l;
        int plates = d >= 2 ? 2 : 1;
        l.Calm = Mathf.Max(1.6f, 3.2f - 0.3f * d);
        l.Gust = Mathf.Min(2.8f, 1.8f + 0.2f * d);
        l.Inner = 1.2f;
        if (plates == 1)
        {
            l.Outer = 5.2f;
            l.Centres = new[] { Vector2.Zero };
        }
        else
        {
            l.Outer = 3.4f;
            // The diagonal whose corners sit farther from the doors.
            Vector2 a = new(4.2f, 4.2f), b = new(-4.2f, 4.2f);
            float Clear(Vector2 c)
            {
                float best = float.MaxValue;
                foreach (int side in new[] { room.EntrySide, room.ExitSide })
                    if (side >= 0)
                    {
                        Vector2 door = RoomPlan.Local(RoomPlan.DoorCell(side));
                        best = Mathf.Min(best, Mathf.Min(door.DistanceTo(c), door.DistanceTo(-c)));
                    }
                return best;
            }
            Vector2 pick = Clear(a) >= Clear(b) ? a : b;
            if (rng.Chance(0.5f)) pick = -pick;
            l.Centres = new[] { pick, -pick };
        }
        l.Phase = new float[plates];
        for (int i = 0; i < plates; i++) l.Phase[i] = rng.Range(0f, l.Calm + l.Gust);
    }

    /// <summary>Seconds into the calm/gust cycle of ring <paramref name="i"/> (calm first).</summary>
    public static float Cycle(Data l, int i, float t)
    {
        float period = l.Calm + l.Gust;
        float p = (t + l.Phase[Math.Min(i, l.Phase.Length - 1)]) % period;
        return p < 0f ? p + period : p;
    }

    public static bool Gusting(Data l, int i, float t) => Cycle(l, i, t) >= l.Calm;

    /// <summary>A ring can be crossed in one calm, walking, with margin.</summary>
    public static bool Crossable(Data l)
        => l.Centres.Length > 0 && (l.Outer - l.Inner) / PuzzleSolver.WalkSpeed * PuzzleSolver.TimingMargin <= l.Calm;

    public override void HostStep(PuzzleState st, RoomPlan room, int r, IReadOnlyList<Vector3> players, float now)
    {
        Data l = room.Layout<Data>();
        int touched = st.Get(room, PuzzleState.RA), on = 0;
        for (int i = 0; i < l.Centres.Length; i++)
            foreach (Vector3 p in players)
            {
                Vector3 d = p - room.World(l.Centres[i]);
                if (new Vector2(d.X, d.Z).Length() <= l.Inner && d.Y < 1.6f) on |= 1 << i;
            }
        st.SetR(room, PuzzleState.RPressed, on);
        st.SetR(room, PuzzleState.RA, touched | on);
        if ((touched | on) == (1 << l.Centres.Length) - 1) st.MarkSolved(r, now);
    }

    public override async Task<string?> Solve(IPuzzleDriver d, RoomPlan room, int r)
    {
        Data l = room.Layout<Data>();
        if (!Crossable(l)) return "calm too short";
        foreach (Vector2 c in l.Centres)
        {
            await d.Stand(room.World(c));
            await d.Wait(0.5f);
        }
        return null;
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
            var ring = PieceKit.Mesh(new TorusMesh { InnerRadius = l.Inner, OuterRadius = l.Outer, Rings = 64, RingSegments = 8 }, c + Vector3.Up * 0.02f, ringMat, false);
            ring.Scale = new Vector3(1f, 0.03f, 1f);
            kit.Put(ring);
            v.RingMats.Add(ringMat);
            v.Plates.Add(kit.Plate(c, round: true));
            var arrows = new List<Node3D>();
            int count = Mathf.RoundToInt(l.Outer * 2.2f);
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
        return v;
    }

    public override void Sync(PieceKit kit, object view, RoomPlan room, PuzzleState st, float t, float dt, bool changed)
    {
        var v = (View)view;
        Data l = room.Layout<Data>();
        int touched = st.Get(room, PuzzleState.RA);
        for (int i = 0; i < l.Centres.Length; i++)
        {
            PieceKit.DrawPlate(v.Plates[i], (touched >> i & 1) != 0 || st.Solved(room), dt);
            float cycle = Cycle(l, i, t);
            bool gust = cycle >= l.Calm && !st.Solved(room);
            bool warn = !gust && cycle >= l.Calm - 0.6f && !st.Solved(room);
            float span = l.Outer - l.Inner;
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
        if ((now(PuzzleState.RPressed) & ~was(PuzzleState.RPressed)) != 0) AudioDirector.PlayPuzzle(PuzzleSound.Click);
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
            if (dist <= l.Inner || dist >= l.Outer + 0.3f || p.Y > 2.5f) continue;
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
        int touched = st.Get(room, PuzzleState.RA);
        for (int i = 0; i < l.Centres.Length; i++)
            if ((touched >> i & 1) == 0) return new PuzzleHint(room.World(l.Centres[i]), "!");
        return null;
    }

    public override string Status(PuzzleMap map, RoomPlan room)
    {
        Data l = room.Layout<Data>();
        float cycle = Cycle(l, 0, map.Elapsed);
        string touched = Meta.Loc.F(PlatesLine, PuzzleSolver.PopCount(map.State.Get(room, PuzzleState.RA)), l.Centres.Length);
        return cycle >= l.Calm
            ? Meta.Loc.F(GustLine, (l.Calm + l.Gust - cycle).ToString("0.0")) + "  ·  " + touched
            : Meta.Loc.F(CalmLine, (l.Calm - cycle).ToString("0.0")) + "  ·  " + touched;
    }

    public override Vector3 Bot(PuzzleBot bot, RoomPlan room, Vector3 me)
    {
        Data l = room.Layout<Data>();
        int touched = bot.State.Get(room, PuzzleState.RA);
        for (int i = 0; i < l.Centres.Length; i++)
        {
            if ((touched >> i & 1) != 0) continue;
            Vector3 c = room.World(l.Centres[i]);
            Vector3 d = me - c;
            d.Y = 0f;
            float dist = d.Length();
            if (dist <= l.Outer + 0.2f) return PuzzleBot.Toward(me, c, 0.1f);
            // Wait at the edge for a fresh calm, then run straight in.
            Vector3 edge = c + (dist > 0.01f ? d / dist : Vector3.Right) * (l.Outer + 0.9f);
            float cycle = Cycle(l, i, bot.Map.Elapsed);
            float need = (l.Outer - l.Inner + 1f) / PuzzleSolver.WalkSpeed * 1.3f;
            bool go = cycle < l.Calm - need;
            if (me.DistanceTo(edge) > 0.8f && !go) return PuzzleBot.Toward(me, edge, 0.3f);
            return go ? PuzzleBot.Toward(me, c, 0.1f) : Vector3.Zero;
        }
        return Vector3.Zero;
    }

    public override void Glyph(CanvasItem ci, Vector2 at, float size, Color c)
    {
        float w = Mathf.Max(1f, size * 0.14f);
        ci.DrawArc(at, size * 0.3f, 0f, Mathf.Tau, 12, c, w, true);
        ci.DrawArc(at, size * 0.7f, 0.4f, Mathf.Tau - 0.4f, 16, c, w, true);
    }
}
