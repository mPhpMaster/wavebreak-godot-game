using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Wavebreak.Audio;
using Wavebreak.Core;

namespace Wavebreak.Game.Puzzle.Kinds;

/// <summary>
/// Counting Room: a great balance with a fixed counterweight on its left pan,
/// and numbered weights on pedestals. Using a pedestal moves its weight from
/// the rack to the left pan, to the right pan, and back. Level the beam. The
/// beam tilts towards the heavier side and the needle says how far, so it
/// reads without numbers too; from difficulty 2 the answer needs weights on
/// both pans (a weight beside the counterweight takes away).
///
/// State: RA each weight's place, two bits each (0 rack, 1 left, 2 right).
/// Solver: every placement (3^6 = 729).
/// </summary>
public sealed class ScalesKind : PuzzleKind
{
    public sealed class Data
    {
        public int[] Values = Array.Empty<int>();
        public int Counter;
        public Vector2[] Pedestals = Array.Empty<Vector2>();
        /// <summary>Unit vector along the beam (left pan at -Axis * Arm).</summary>
        public Vector2 Axis = Vector2.Right;
        public int[] Answer = Array.Empty<int>();
    }

    private sealed class View
    {
        public Node3D Beam = null!;
        public Node3D LeftPan = null!, RightPan = null!;
        public readonly List<Node3D> Weights = new();
        public Node3D Needle = null!;
        public StandardMaterial3D Dial = null!;
        public float Tilt;
    }

    private const float Arm = 3.2f;
    private const string Verb = "Move the weight";
    private const string PansLine = "Left pan {0}  ·  right pan {1}";

    public override string Id => "scales";
    public override string Title => "Counting Room";
    public override string Objective => "Level the great balance: move numbered weights from their pedestals onto the pans.";
    public override string Hint => "Hint: use the marked weight's pedestal as many times as the number says.";
    public override IEnumerable<string> Strings => new[] { Verb, PansLine };
    public override HallStyle Style => HallStyle.Counting;

    // ------------------------------------------------------------- data

    public override void Generate(RoomPlan room, Rng rng, int d)
    {
        var l = new Data();
        room.Data = l;
        int k = d == 0 ? 3 : d == 1 ? 4 : d <= 3 ? 5 : 6;
        bool bothPans = d >= 2;
        // The beam lies across the way in, so the room is entered facing it.
        int side = room.EntrySide < 0 ? 3 : room.EntrySide;
        l.Axis = side % 2 == 0 ? new Vector2(0f, 1f) : new Vector2(1f, 0f);
        Vector2 toEntry = new(RoomPlan.Dx[side], RoomPlan.Dz[side]);
        for (int attempt = 0; attempt < 400; attempt++)
        {
            var pool = new List<int> { 1, 2, 3, 4, 5, 6, 7, 8, 9 };
            PuzzleGen.Shuffle(rng, pool);
            l.Values = pool.GetRange(0, k).ToArray();
            Array.Sort(l.Values);
            l.Answer = new int[k];
            int left = 0, right = 0;
            for (int i = 0; i < k; i++)
            {
                l.Answer[i] = rng.RangeInt(0, 3);
                if (l.Answer[i] == 1) left += l.Values[i];
                if (l.Answer[i] == 2) right += l.Values[i];
            }
            l.Counter = right - left;
            if (l.Counter < 2 || l.Counter > 17) continue;
            if (bothPans && left == 0) continue;
            int moves = 0;
            foreach (int a in l.Answer) moves += a;
            if (moves < 2) continue;
            // Few answers, so it is reasoned out rather than stumbled on.
            int answers = CountAnswers(l);
            if (answers > (attempt < 300 ? 2 : 4)) continue;
            break;
        }
        // Pedestals in an arc on the entry side of the balance.
        l.Pedestals = new Vector2[k];
        Vector2 across = l.Axis;
        for (int i = 0; i < k; i++)
        {
            float x = (i - (k - 1) * 0.5f) * 2.4f;
            l.Pedestals[i] = toEntry * (4.6f + 0.25f * Mathf.Abs(x)) + across * x;
        }
        room.Spots.AddRange(l.Pedestals);
        room.Spots.Add(Vector2.Zero);
        room.Spots.Add(-l.Axis * Arm);
        room.Spots.Add(l.Axis * Arm);
    }

    private static int CountAnswers(Data l)
    {
        int n = 0, total = 1;
        for (int i = 0; i < l.Values.Length; i++) total *= 3;
        for (int code = 0; code < total; code++)
        {
            int c = code, left = l.Counter, right = 0;
            for (int i = 0; i < l.Values.Length; i++, c /= 3)
            {
                if (c % 3 == 1) left += l.Values[i];
                if (c % 3 == 2) right += l.Values[i];
            }
            if (left == right) n++;
        }
        return n;
    }

    public static int Place(PuzzleState st, RoomPlan room, int i) => st.Get(room, PuzzleState.RA) >> (2 * i) & 3;

    public static (int Left, int Right) Pans(Data l, Func<int, int> place)
    {
        int left = l.Counter, right = 0;
        for (int i = 0; i < l.Values.Length; i++)
        {
            if (place(i) == 1) left += l.Values[i];
            if (place(i) == 2) right += l.Values[i];
        }
        return (left, right);
    }

    public override bool Act(PuzzleState st, RoomPlan room, int r, PuzzleAction action, int a, int b, float now)
    {
        if (action != PuzzleAction.Use) return false;
        Data l = room.Layout<Data>();
        if (a < 0 || a >= l.Values.Length) return true;
        int word = st.Get(room, PuzzleState.RA);
        int next = ((word >> (2 * a) & 3) + 1) % 3;
        word = word & ~(3 << (2 * a)) | next << (2 * a);
        st.SetR(room, PuzzleState.RA, word);
        (int left, int right) = Pans(l, i => word >> (2 * i) & 3);
        if (left == right) st.MarkSolved(r, now);
        return true;
    }

    /// <summary>For each weight, presses needed to reach the balanced placement
    /// fewest presses away from the current one.</summary>
    public static int[]? Presses(PuzzleState st, RoomPlan room)
    {
        Data l = room.Layout<Data>();
        int k = l.Values.Length, total = 1;
        for (int i = 0; i < k; i++) total *= 3;
        int[]? best = null;
        int bestSum = int.MaxValue;
        for (int code = 0; code < total; code++)
        {
            var target = new int[k];
            int c = code;
            for (int i = 0; i < k; i++, c /= 3) target[i] = c % 3;
            (int left, int right) = Pans(l, i => target[i]);
            if (left != right) continue;
            var presses = new int[k];
            int sum = 0;
            for (int i = 0; i < k; i++)
            {
                presses[i] = (target[i] - Place(st, room, i) + 3) % 3;
                sum += presses[i];
            }
            if (sum > 0 && sum < bestSum) { bestSum = sum; best = presses; }
        }
        return best;
    }

    public override async Task<string?> Solve(IPuzzleDriver d, RoomPlan room, int r)
    {
        int[]? presses = Presses(d.State, room);
        if (presses is null) return "no balance";
        for (int i = 0; i < presses.Length; i++)
            for (int n = 0; n < presses[i]; n++) await d.Ask(PuzzleAction.Use, i);
        return null;
    }

    // ------------------------------------------------------------ scene

    private static Vector3 Dir3(Vector2 v) => new(v.X, 0f, v.Y);

    public override object Build(PieceKit kit, RoomPlan room)
    {
        Data l = room.Layout<Data>();
        var v = new View();
        Vector3 c = room.Centre;
        var brass = PieceKit.Flat(new Color(0.86f, 0.70f, 0.36f));
        var wood = PieceKit.Flat(new Color(0.42f, 0.28f, 0.16f));
        // The stand and the beam's pivot.
        kit.Put(PieceKit.Mesh(new CylinderMesh { TopRadius = 0.8f, BottomRadius = 1.0f, Height = 0.3f, RadialSegments = 16 }, c + Vector3.Up * 0.15f, wood));
        kit.Put(PieceKit.Mesh(new CylinderMesh { TopRadius = 0.16f, BottomRadius = 0.24f, Height = 3.2f, RadialSegments = 10 }, c + Vector3.Up * 1.75f, brass));
        kit.Post(c, 0.6f, 3.4f);
        float yaw = Mathf.Atan2(l.Axis.X, l.Axis.Y);
        v.Beam = new Node3D { Position = c + Vector3.Up * 3.3f, Rotation = new Vector3(0f, yaw, 0f) };
        kit.Put(v.Beam);
        // Along the beam node's local Z.
        v.Beam.AddChild(PieceKit.Mesh(new BoxMesh { Size = new Vector3(0.22f, 0.22f, Arm * 2f + 0.4f) }, Vector3.Zero, brass));
        // The needle points up from the pivot: upright when level.
        v.Needle = new Node3D();
        v.Beam.AddChild(v.Needle);
        v.Dial = PieceKit.Flat(PieceKit.WallCap);
        v.Needle.AddChild(PieceKit.Mesh(new CylinderMesh { TopRadius = 0f, BottomRadius = 0.12f, Height = 1.2f, RadialSegments = 6 }, Vector3.Up * 0.7f, v.Dial));
        // A fixed scale behind the needle: the level mark is a notch at the top.
        var scale = new Node3D { Position = c + Vector3.Up * 3.3f, Rotation = new Vector3(0f, yaw, 0f) };
        kit.Put(scale);
        scale.AddChild(PieceKit.Mesh(new TorusMesh { InnerRadius = 1.25f, OuterRadius = 1.35f, Rings = 24, RingSegments = 4 }, new Vector3(-0.15f, 0f, 0f), PieceKit.Flat(PieceKit.BronzeDark), false));
        scale.GetChild<Node3D>(0).Rotation = new Vector3(0f, 0f, Mathf.Pi * 0.5f);
        scale.AddChild(PieceKit.Mesh(new BoxMesh { Size = new Vector3(0.1f, 0.35f, 0.12f) }, new Vector3(-0.15f, 1.45f, 0f), PieceKit.Glow(PieceKit.Teal, 1.5f), false));

        v.LeftPan = Pan(kit, brass);
        v.RightPan = Pan(kit, brass);
        // The counterweight: a big block on the left pan with its number.
        var counter = new Node3D();
        v.LeftPan.AddChild(counter);
        counter.AddChild(PieceKit.Mesh(new BoxMesh { Size = new Vector3(0.9f, 0.7f, 0.9f) }, Vector3.Up * 0.4f, PieceKit.Flat(PieceKit.Iron)));
        var counterLabel = new Label3D
        {
            Text = l.Counter.ToString(), FontSize = 96, PixelSize = 0.011f, OutlineSize = 18,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Position = Vector3.Up * 1.25f,
            Modulate = new Color(1f, 0.95f, 0.8f), OutlineModulate = new Color(0.1f, 0.08f, 0.05f),
        };
        counter.AddChild(counterLabel);

        for (int i = 0; i < l.Values.Length; i++)
        {
            Vector3 at = room.World(l.Pedestals[i]);
            kit.Put(PieceKit.Mesh(new CylinderMesh { TopRadius = 0.45f, BottomRadius = 0.55f, Height = 0.9f, RadialSegments = 12 }, at + Vector3.Up * 0.45f, PieceKit.Flat(PieceKit.WallCap)));
            kit.Post(at, 0.5f, 1.0f);
            var w = new Node3D { Position = at + Vector3.Up * 1.1f };
            kit.Put(w);
            float s = 0.32f + 0.035f * l.Values[i];
            w.AddChild(PieceKit.Mesh(new CylinderMesh { TopRadius = s * 0.8f, BottomRadius = s, Height = s * 1.1f, RadialSegments = 10 }, Vector3.Up * (s * 0.55f), PieceKit.Flat(PieceKit.Bronze)));
            w.AddChild(new Label3D
            {
                Text = l.Values[i].ToString(), FontSize = 80, PixelSize = 0.011f, OutlineSize = 16,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Position = Vector3.Up * (s * 1.1f + 0.45f),
                Modulate = new Color(1f, 0.95f, 0.8f), OutlineModulate = new Color(0.1f, 0.08f, 0.05f),
            });
            v.Weights.Add(w);
        }
        return v;
    }

    private static Node3D Pan(PieceKit kit, Material brass)
    {
        var pan = new Node3D();
        kit.Put(pan);
        pan.AddChild(PieceKit.Mesh(new CylinderMesh { TopRadius = 1.0f, BottomRadius = 0.85f, Height = 0.14f, RadialSegments = 18 }, Vector3.Zero, brass));
        // Three chains up to the beam.
        for (int k = 0; k < 3; k++)
        {
            float a = Mathf.Tau * k / 3f;
            var chain = PieceKit.Mesh(new CylinderMesh { TopRadius = 0.03f, BottomRadius = 0.03f, Height = 1.6f, RadialSegments = 4 }, new Vector3(Mathf.Cos(a) * 0.45f, 0.8f, Mathf.Sin(a) * 0.45f), brass, false);
            chain.Rotation = new Vector3(Mathf.Sin(a) * 0.27f, 0f, -Mathf.Cos(a) * 0.27f);
            pan.AddChild(chain);
        }
        return pan;
    }

    public override void Sync(PieceKit kit, object view, RoomPlan room, PuzzleState st, float t, float dt, bool changed)
    {
        var v = (View)view;
        Data l = room.Layout<Data>();
        (int left, int right) = Pans(l, i => Place(st, room, i));
        float target = Mathf.Clamp((left - right) * 0.045f, -0.32f, 0.32f);
        v.Tilt = Mathf.MoveToward(v.Tilt, target, dt * 0.5f);
        // Positive tilt: the left end (-Z on the beam) goes down.
        v.Beam.Rotation = new Vector3(-v.Tilt, v.Beam.Rotation.Y, 0f);
        Vector3 pivot = v.Beam.Position;
        Vector3 axis = Dir3(l.Axis);
        Vector3 leftEnd = pivot - axis * (Arm * Mathf.Cos(v.Tilt)) + Vector3.Down * (Arm * Mathf.Sin(v.Tilt));
        Vector3 rightEnd = pivot + axis * (Arm * Mathf.Cos(v.Tilt)) + Vector3.Up * (Arm * Mathf.Sin(v.Tilt));
        v.LeftPan.Position = leftEnd + Vector3.Down * 1.65f;
        v.RightPan.Position = rightEnd + Vector3.Down * 1.65f;
        bool level = left == right;
        PieceKit.Lit(v.Dial, level, PieceKit.Teal, PieceKit.WallCap, 2f);
        // Weights: on the rack, or stacked on their pan.
        int onLeft = 0, onRight = 0;
        for (int i = 0; i < v.Weights.Count; i++)
        {
            int place = Place(st, room, i);
            Vector3 home;
            if (place == 0) home = room.World(l.Pedestals[i]) + Vector3.Up * 1.1f;
            else
            {
                int slot = place == 1 ? onLeft++ : onRight++;
                Vector3 pan = place == 1 ? v.LeftPan.Position : v.RightPan.Position;
                float ring = place == 1 ? 1 + slot : slot;   // the counterweight sits in the left pan's middle
                float a = ring * 1.3f;
                home = pan + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (ring == 0 ? 0f : 0.6f) + Vector3.Up * 0.08f;
            }
            v.Weights[i].Position = v.Weights[i].Position.MoveToward(home, dt * 9f);
        }
    }

    public override void Sounds(object view, RoomPlan room, Func<int, int> was, Func<int, int> now)
    {
        if (was(PuzzleState.RA) != now(PuzzleState.RA)) AudioDirector.PlayPuzzle(PuzzleSound.Thunk, 1.3f);
    }

    public override void Offer(PuzzleMap map, RoomPlan room, FocusOffer offer)
    {
        Data l = room.Layout<Data>();
        for (int i = 0; i < l.Pedestals.Length; i++) offer(PuzzleAction.Use, i, room.World(l.Pedestals[i]), Verb);
    }

    private static int NextWeight(PuzzleState st, RoomPlan room, Vector3? me, out int presses)
    {
        presses = 0;
        int[]? need = Presses(st, room);
        if (need is null) return -1;
        Data l = room.Layout<Data>();
        int best = -1;
        float bestD = float.MaxValue;
        for (int i = 0; i < need.Length; i++)
        {
            if (need[i] == 0) continue;
            float d = me is { } at ? room.World(l.Pedestals[i]).DistanceSquaredTo(at) : i;
            if (d < bestD) { bestD = d; best = i; presses = need[i]; }
        }
        return best;
    }

    public override PuzzleHint? HintAt(PuzzleState st, RoomPlan room, Vector3? me, float now)
    {
        int i = NextWeight(st, room, me, out int presses);
        return i < 0 ? null : new PuzzleHint(room.World(room.Layout<Data>().Pedestals[i]), presses.ToString());
    }

    public override string Status(PuzzleMap map, RoomPlan room)
    {
        (int left, int right) = Pans(room.Layout<Data>(), i => Place(map.State, room, i));
        return Meta.Loc.F(PansLine, left, right);
    }

    public override Vector3 Bot(PuzzleBot bot, RoomPlan room, Vector3 me)
    {
        int i = NextWeight(bot.State, room, me, out _);
        if (i < 0) return Vector3.Zero;
        Vector3 at = room.World(room.Layout<Data>().Pedestals[i]);
        if (bot.PressIf(PuzzleAction.Use, i, room.Index)) return Vector3.Zero;
        return PuzzleBot.Toward(me, at, 1.3f);
    }

    public override void Glyph(CanvasItem ci, Vector2 at, float size, Color c)
    {
        float h = size * 0.7f, w = Mathf.Max(1f, size * 0.14f);
        ci.DrawLine(at + new Vector2(0f, h), at + new Vector2(0f, -h * 0.6f), c, w);
        ci.DrawLine(at + new Vector2(-h, -h * 0.6f), at + new Vector2(h, -h * 0.6f), c, w);
        ci.DrawArc(at + new Vector2(-h, -h * 0.1f), h * 0.35f, 0f, Mathf.Pi, 6, c, w);
        ci.DrawArc(at + new Vector2(h, -h * 0.1f), h * 0.35f, 0f, Mathf.Pi, 6, c, w);
    }
}
