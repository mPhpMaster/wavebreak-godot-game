using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Wavebreak.Audio;
using Wavebreak.Core;

namespace Wavebreak.Game.Puzzle.Kinds;

/// <summary>
/// Lamp Room: a row of levers facing a row of lamps; each lever flips its own
/// lamp and one or two others. The board starts scrambled; light every lamp.
/// Standing at a lever marks the lamps it flips.
///
/// State: RA levers pulled (bits). Solver: every on/off combination (2^5).
/// </summary>
public sealed class LeversKind : PuzzleKind
{
    public sealed class Data
    {
        public Vector2[] Levers = Array.Empty<Vector2>();
        public Vector2[] Lamps = Array.Empty<Vector2>();
        public int[] Masks = Array.Empty<int>();
        public int LampStart;
    }

    private sealed class View
    {
        public readonly List<Node3D> Handles = new();
        public readonly List<Node3D> Lamps = new();
        public readonly List<StandardMaterial3D> Mats = new();
        public readonly List<Node3D> Marks = new();
    }

    private const string Verb = "Pull the lever";
    private const string LampsLine = "Lamps lit: {0}/{1}";

    public override string Id => "levers";
    public override string Title => "Lamp Room";
    public override string Objective => "Pull levers until every lamp stands tall and lit.";
    public override string Hint => "Hint: the marked lever is part of the answer.";
    public override IEnumerable<string> Strings => new[] { Verb, LampsLine };
    public override HallStyle Style => HallStyle.Foundry;

    private static readonly Color Unlit = new(0.35f, 0.38f, 0.42f);

    // ------------------------------------------------------------- data

    public override void Generate(RoomPlan room, Rng rng, int d)
    {
        var l = new Data();
        room.Data = l;
        int k = 3 + Math.Min(d, 2);
        int turn = rng.RangeInt(0, 4);
        Vector2 Turn(Vector2 v) => turn switch
        {
            1 => new Vector2(-v.Y, v.X),
            2 => -v,
            3 => new Vector2(v.Y, -v.X),
            _ => v,
        };
        l.Levers = new Vector2[k];
        l.Lamps = new Vector2[k];
        for (int i = 0; i < k; i++)
        {
            float x = (i - (k - 1) * 0.5f) * 3f;
            l.Levers[i] = Turn(new Vector2(x, 5.6f));
            l.Lamps[i] = Turn(new Vector2(x, -2.6f));
        }
        int full = (1 << k) - 1;
        for (int attempt = 0; attempt < 200; attempt++)
        {
            l.Masks = new int[k];
            for (int i = 0; i < k; i++)
            {
                int mask = 1 << i, extra = 0;
                var others = Enumerable.Range(0, k).Where(j => j != i).ToList();
                while (others.Count > 0 && extra < 2)
                {
                    int j = PuzzleGen.Take(rng, others);
                    if (!rng.Chance(0.35f + 0.05f * d) && !(d >= 1 && extra == 0)) continue;
                    mask |= 1 << j;
                    extra++;
                }
                l.Masks[i] = mask;
            }
            int scramble = 0;
            while (PuzzleSolver.PopCount(scramble) < Math.Min(k - 1, 2 + d / 2)) scramble |= 1 << rng.RangeInt(0, k);
            l.LampStart = full;
            for (int i = 0; i < k; i++) if ((scramble >> i & 1) != 0) l.LampStart ^= l.Masks[i];
            if (l.LampStart == full) continue;
            int answer = SolveBits(room, 0);
            if (answer < 0 || PuzzleSolver.PopCount(answer) < Math.Min(2, k)) continue;
            break;
        }
        room.Spots.AddRange(l.Levers);
        room.Spots.AddRange(l.Lamps);
        if (d >= 2) PuzzleGen.AddSpikes(room, rng, c => PuzzleGen.NearAny(RoomPlan.Local(c), l.Levers, 2.2f) || PuzzleGen.NearAny(RoomPlan.Local(c), l.Lamps, 2.2f));
    }

    public static int Full(RoomPlan room) => (1 << room.Layout<Data>().Lamps.Length) - 1;

    /// <summary>Which lamps are lit with these levers pulled (one bit each).</summary>
    public static int Lit(RoomPlan room, int pulled)
    {
        Data l = room.Layout<Data>();
        int lit = l.LampStart;
        for (int i = 0; i < l.Masks.Length; i++)
            if (((pulled >> i) & 1) != 0) lit ^= l.Masks[i];
        return lit;
    }

    /// <summary>The fewest extra levers to pull (one bit each) to light every
    /// lamp from <paramref name="pulled"/>; -1 when nothing does. Pulling a
    /// lever twice undoes it, so each one is pulled at most once.</summary>
    public static int SolveBits(RoomPlan room, int pulled)
    {
        int k = room.Layout<Data>().Masks.Length, best = -1, bestCount = int.MaxValue;
        for (int bits = 0; bits < 1 << k; bits++)
        {
            if (Lit(room, pulled ^ bits) != Full(room)) continue;
            int count = PuzzleSolver.PopCount(bits);
            if (count < bestCount) { best = bits; bestCount = count; }
        }
        return best;
    }

    private static int NextLever(PuzzleState st, RoomPlan room)
    {
        int answer = SolveBits(room, st.Get(room, PuzzleState.RA));
        for (int i = 0; i < room.Layout<Data>().Levers.Length; i++)
            if (answer > 0 && (answer >> i & 1) != 0) return i;
        return -1;
    }

    public override bool Act(PuzzleState st, RoomPlan room, int r, PuzzleAction action, int a, int b, float now)
    {
        if (action != PuzzleAction.Lever) return false;
        if (a < 0 || a >= room.Layout<Data>().Masks.Length) return true;
        int pulled = st.Get(room, PuzzleState.RA) ^ (1 << a);
        st.SetR(room, PuzzleState.RA, pulled);
        if (Lit(room, pulled) == Full(room)) st.MarkSolved(r, now);
        return true;
    }

    public override async Task<string?> Solve(IPuzzleDriver d, RoomPlan room, int r)
    {
        int answer = SolveBits(room, d.State.Get(room, PuzzleState.RA));
        if (answer <= 0) return "no lever solution";
        for (int i = 0; i < room.Layout<Data>().Levers.Length; i++)
            if ((answer >> i & 1) != 0) await d.Ask(PuzzleAction.Lever, i);
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
            var mark = PieceKit.Mesh(new CylinderMesh { TopRadius = 0.22f, BottomRadius = 0f, Height = 0.4f, RadialSegments = 8 }, at + Vector3.Up * 2.8f, PieceKit.Glow(PieceKit.Warm, 1.8f), false);
            mark.Visible = false;
            kit.Put(mark);
            v.Marks.Add(mark);
            kit.Post(at, 0.42f, 1.6f);
        }
        return v;
    }

    public override void Sync(PieceKit kit, object view, RoomPlan room, PuzzleState st, float t, float dt, bool changed)
    {
        var v = (View)view;
        Data l = room.Layout<Data>();
        int pulled = st.Get(room, PuzzleState.RA);
        int lit = Lit(room, pulled);
        for (int i = 0; i < v.Handles.Count; i++)
        {
            float target = (pulled >> i & 1) != 0 ? 0.6f : -0.6f;
            Vector3 rot = v.Handles[i].Rotation;
            v.Handles[i].Rotation = new Vector3(Mathf.MoveToward(rot.X, target, dt * 5f), 0f, 0f);
        }
        PuzzleFocus? focus = kit.Map.Focus;
        for (int i = 0; i < v.Lamps.Count; i++)
        {
            bool on = (lit >> i & 1) != 0;
            float sy = Mathf.MoveToward(v.Lamps[i].Scale.Y, on ? 1f : 0.35f, dt * 3f);
            v.Lamps[i].Scale = new Vector3(1f, sy, 1f);
            PieceKit.Lit(v.Mats[i], on, PieceKit.Warm, Unlit, 1.8f);
            // Standing at a lever marks the lamps it flips.
            bool mark = focus is { Action: PuzzleAction.Lever } f && f.Room == room.Index && (l.Masks[f.Index] >> i & 1) != 0;
            v.Marks[i].Visible = mark;
            if (mark) v.Marks[i].Position = room.World(l.Lamps[i]) + Vector3.Up * (2.8f + Mathf.Sin(t * 5f) * 0.12f);
        }
    }

    public override void Sounds(object view, RoomPlan room, Func<int, int> was, Func<int, int> now)
    {
        if (was(PuzzleState.RA) != now(PuzzleState.RA)) AudioDirector.PlayPuzzle(PuzzleSound.Click, 0.8f);
    }

    public override void Offer(PuzzleMap map, RoomPlan room, FocusOffer offer)
    {
        Vector2[] levers = room.Layout<Data>().Levers;
        for (int i = 0; i < levers.Length; i++) offer(PuzzleAction.Lever, i, room.World(levers[i]), Verb);
    }

    public override PuzzleHint? HintAt(PuzzleState st, RoomPlan room, Vector3? me, float now)
    {
        int i = NextLever(st, room);
        return i < 0 ? null : new PuzzleHint(room.World(room.Layout<Data>().Levers[i]), "?");
    }

    public override string Status(PuzzleMap map, RoomPlan room)
        => Meta.Loc.F(LampsLine, PuzzleSolver.PopCount(Lit(room, map.State.Get(room, PuzzleState.RA))), room.Layout<Data>().Lamps.Length);

    public override Vector3 Bot(PuzzleBot bot, RoomPlan room, Vector3 me)
    {
        int i = NextLever(bot.State, room);
        return i < 0 ? Vector3.Zero : bot.Use(room, me, room.World(room.Layout<Data>().Levers[i]), PuzzleAction.Lever, i);
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
