using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Wavebreak.Audio;
using Wavebreak.Core;

namespace Wavebreak.Game.Puzzle.Kinds;

/// <summary>
/// Counting Room: a great balance with a counterweight on its left pan, and
/// numbered weights on pedestals. Using a pedestal moves its weight from the
/// rack to the left pan, to the right pan, and back to the rack (always in
/// that order). Level the beam. From difficulty 2 the counterweight changes
/// each time the beam levels (two levellings, three from difficulty 4), and
/// the weights stay where they are, so a good first answer is one that is
/// cheap to turn into the next. Every press spends one of a limited number of
/// moves (the fewest that can do it, plus a little slack that shrinks to
/// none); running out, or the reset post, puts every weight back and starts
/// over. A weight beside the counterweight takes away, and from difficulty 2
/// some levelling needs both pans. From difficulty 2 the pans' sums are no
/// longer shown: count.
///
/// The beam tilts towards the heavier side and the needle says how far, so
/// it reads without numbers too.
///
/// State: RA each weight's place, two bits each (0 rack, 1 left, 2 right);
/// RB levellings done; RC moves spent; RResets resets; RFails times out of
/// moves. Solver: breadth-first search over (placement, levellings), at
/// most 3^8 x 4 states, presses as moves; the budget is its distance.
/// </summary>
public sealed class ScalesKind : PuzzleKind
{
    public sealed class Data
    {
        public int[] Values = Array.Empty<int>();
        /// <summary>The counterweight for each levelling, in order.</summary>
        public int[] Counters = Array.Empty<int>();
        public int Budget;
        public Vector2[] Pedestals = Array.Empty<Vector2>();
        /// <summary>Unit vector along the beam (left pan at -Axis * Arm).</summary>
        public Vector2 Axis = Vector2.Right;
        public int ResetCell = -1;
        /// <summary>Show the pans' sums on the HUD (the easy rooms).</summary>
        public bool ShowSums;
    }

    private sealed class View
    {
        public Node3D Beam = null!;
        public Node3D LeftPan = null!, RightPan = null!;
        public readonly List<Node3D> Weights = new();
        public Node3D Needle = null!;
        public StandardMaterial3D Dial = null!;
        public Label3D CounterLabel = null!;
        public Label3D MovesLabel = null!;
        public readonly List<StandardMaterial3D> Pips = new();
        public float Tilt;
    }

    private const float Arm = 3.2f;
    private const string Verb = "Move the weight";
    private const string VerbReset = "Put the weights back";
    private const string PansLine = "Left pan {0}  ·  right pan {1}";
    private const string MovesLine = "Moves left: {0}";
    private const string StageLine = "Counterweight {0}  ·  moves left {1}  ·  levelled {2}/{3}";

    public override string Id => "scales";
    public override string Title => "Counting Room";
    public override string Objective => "Level the great balance with numbered weights. Each use of a pedestal moves its weight rack, left pan, right pan, rack, and costs a move; the counterweight may change when the beam levels.";
    public override string Hint => "Hint: use the marked weight's pedestal as many times as the number says. Out of moves? The post puts the weights back.";
    public override IEnumerable<string> Strings => new[] { Verb, VerbReset, PansLine, MovesLine, StageLine };
    public override HallStyle Style => HallStyle.Counting;
    public override float ParSeconds => 26f;

    // ------------------------------------------------------------- data

    /// <summary>Weights, levellings, slack moves, highest value, and the
    /// band the fewest moves must fall in.</summary>
    private static (int K, int Stages, int Slack, int MaxValue, int MinMoves, int MaxMoves) Knobs(int d) => Math.Clamp(d, 0, 5) switch
    {
        0 => (4, 1, 3, 9, 3, 5),
        1 => (5, 1, 2, 9, 4, 7),
        2 => (6, 2, 2, 9, 6, 11),
        3 => (6, 2, 1, 12, 8, 13),
        4 => (7, 3, 1, 12, 9, 16),
        _ => (8, 3, 0, 12, 10, 18),
    };

    public override void Generate(RoomPlan room, Rng rng, int d)
    {
        var l = new Data();
        room.Data = l;
        (int k, int stages, int slack, int maxValue, int minMoves, int maxMoves) = Knobs(d);
        l.ShowSums = d <= 1;
        // The beam lies across the way in, so the room is entered facing it.
        int side = room.EntrySide < 0 ? 3 : room.EntrySide;
        l.Axis = side % 2 == 0 ? new Vector2(0f, 1f) : new Vector2(1f, 0f);
        Vector2 toEntry = new(RoomPlan.Dx[side], RoomPlan.Dz[side]);
        int best = -1;
        for (int attempt = 0; attempt < 300; attempt++)
        {
            var pool = new List<int>();
            for (int v = 1; v <= maxValue; v++) pool.Add(v);
            PuzzleGen.Shuffle(rng, pool);
            int[] values = pool.GetRange(0, k).ToArray();
            Array.Sort(values);
            var counters = new int[stages];
            bool ok = true;
            for (int s = 0; s < stages && ok; s++)
            {
                int left = 0, right = 0;
                for (int i = 0; i < k; i++)
                {
                    int p = rng.RangeInt(0, 3);
                    if (p == 1) left += values[i];
                    if (p == 2) right += values[i];
                }
                counters[s] = right - left;
                ok = counters[s] >= 2 && counters[s] <= 30 && (s == 0 || counters[s] != counters[s - 1]);
            }
            if (!ok) continue;
            var trial = new Data { Values = values, Counters = counters };
            List<int>? path = Path(trial, 0, 0);
            if (path is null) continue;
            int moves = path.Count;
            // From difficulty 2 the best answer levels with a weight beside
            // the counterweight at least once (both pans needed).
            if (d >= 2 && !UsesLeft(trial, path)) continue;
            bool inBand = moves >= minMoves && moves <= maxMoves;
            // Out of the band, keep the longest that is not too long.
            if (!inBand && (moves > maxMoves || moves <= best)) continue;
            l.Values = values;
            l.Counters = counters;
            best = moves;
            if (inBand) break;
        }
        if (best < 0)
        {
            // Fallback: the 5 on the right pan against the counterweight 5.
            l.Values = new[] { 1, 2, 3, 5 };
            l.Counters = new[] { 5 };
            best = Distance(l, 0, 0);
        }
        l.Budget = best + slack;
        k = l.Values.Length;
        // Pedestals in an arc on the entry side of the balance.
        l.Pedestals = new Vector2[k];
        Vector2 across = l.Axis;
        float gap = k >= 7 ? 2.15f : 2.4f;
        for (int i = 0; i < k; i++)
        {
            float x = (i - (k - 1) * 0.5f) * gap;
            l.Pedestals[i] = toEntry * (4.6f + 0.25f * Mathf.Abs(x)) + across * x;
        }
        // The reset post on the far side, off the beam's line.
        l.ResetCell = RoomPlan.CellOf(RoomPlan.Mid - RoomPlan.Dx[side] * 3 + (int)across.X * 3, RoomPlan.Mid - RoomPlan.Dz[side] * 3 + (int)across.Y * 3);
        room.Fixed[l.ResetCell] = true;
        room.Spots.AddRange(l.Pedestals);
        room.Spots.Add(Vector2.Zero);
        room.Spots.Add(-l.Axis * Arm);
        room.Spots.Add(l.Axis * Arm);
        room.ParSeconds = 14f + 4.5f * best + 10f * l.Counters.Length;
    }

    /// <summary>Played from the start, does <paramref name="path"/> level the
    /// beam with a weight on the left pan at least once?</summary>
    private static bool UsesLeft(Data l, List<int> path)
    {
        var place = new int[l.Values.Length];
        int stage = 0;
        foreach (int i in path)
        {
            place[i] = (place[i] + 1) % 3;
            (int left, int right) = Pans(l, stage, k => place[k]);
            if (left != right) continue;
            if (Array.IndexOf(place, 1) >= 0) return true;
            stage++;
        }
        return false;
    }

    private static int Pow3(int k)
    {
        int n = 1;
        for (int i = 0; i < k; i++) n *= 3;
        return n;
    }

    private static int CodeOf(int word, int k)
    {
        int code = 0;
        for (int i = k - 1; i >= 0; i--) code = code * 3 + (word >> (2 * i) & 3) % 3;
        return code;
    }

    /// <summary>Right pan minus left pan's weights, per placement code.</summary>
    private static int[] Diffs(Data l)
    {
        int k = l.Values.Length, n = Pow3(k);
        var diff = new int[n];
        for (int code = 0; code < n; code++)
        {
            int c = code, d = 0;
            for (int i = 0; i < k; i++, c /= 3)
            {
                if (c % 3 == 1) d -= l.Values[i];
                if (c % 3 == 2) d += l.Values[i];
            }
            diff[code] = d;
        }
        return diff;
    }

    /// <summary>Breadth-first from (placement, levellings) to every levelling
    /// done: the presses in order, or null when it cannot be done.</summary>
    private static List<int>? Path(Data l, int code, int stage)
    {
        int k = l.Values.Length, n = Pow3(k), stages = l.Counters.Length;
        if (stage >= stages) return new List<int>();
        int total = n * (stages + 1);
        var from = new int[total];
        var by = new sbyte[total];
        Array.Fill(from, -2);
        int start = stage * n + code;
        from[start] = -1;
        var queue = new Queue<int>();
        queue.Enqueue(start);
        int goal = -1;
        int[] diff = Diffs(l);
        var pow = new int[k];
        for (int i = 0, p = 1; i < k; i++, p *= 3) pow[i] = p;
        while (queue.Count > 0 && goal < 0)
        {
            int s = queue.Dequeue();
            int st = s / n, c = s % n;
            for (int i = 0; i < k; i++)
            {
                int digit = c / pow[i] % 3;
                int nc = c + (digit == 2 ? -2 * pow[i] : pow[i]);
                int ns = st;
                if (diff[nc] == l.Counters[ns]) ns++;
                int next = ns * n + nc;
                if (from[next] != -2) continue;
                from[next] = s;
                by[next] = (sbyte)i;
                if (ns >= stages) { goal = next; break; }
                queue.Enqueue(next);
            }
        }
        if (goal < 0) return null;
        var path = new List<int>();
        for (int s = goal; from[s] >= 0; s = from[s]) path.Add(by[s]);
        path.Reverse();
        return path;
    }

    private static int Distance(Data l, int code, int stage) => Path(l, code, stage)?.Count ?? -1;

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Data, Dictionary<long, List<int>?>> Cache = new();

    /// <summary>The fewest presses from the room as it stands (cached: the
    /// hint and the bot ask every frame).</summary>
    public static List<int>? PathFrom(Data l, int word, int stage)
    {
        Dictionary<long, List<int>?> memo = Cache.GetValue(l, _ => new Dictionary<long, List<int>?>());
        long key = (long)word << 4 | (uint)stage;
        if (memo.TryGetValue(key, out List<int>? known)) return known;
        List<int>? path = Path(l, CodeOf(word, l.Values.Length), stage);
        if (memo.Count > 2048) memo.Clear();
        memo[key] = path;
        return path;
    }

    public static int Place(PuzzleState st, RoomPlan room, int i) => st.Get(room, PuzzleState.RA) >> (2 * i) & 3;

    /// <summary>The pans as they stand, against the current counterweight.</summary>
    public static (int Left, int Right) Pans(Data l, int stage, Func<int, int> place)
    {
        int left = l.Counters[Math.Clamp(stage, 0, l.Counters.Length - 1)], right = 0;
        for (int i = 0; i < l.Values.Length; i++)
        {
            if (place(i) == 1) left += l.Values[i];
            if (place(i) == 2) right += l.Values[i];
        }
        return (left, right);
    }

    public static int MovesLeft(PuzzleState st, RoomPlan room) => room.Layout<Data>().Budget - st.Get(room, PuzzleState.RC);

    private static void PutBack(PuzzleState st, RoomPlan room)
    {
        st.SetR(room, PuzzleState.RA, 0);
        st.SetR(room, PuzzleState.RB, 0);
        st.SetR(room, PuzzleState.RC, 0);
    }

    public override bool Act(PuzzleState st, RoomPlan room, int r, PuzzleAction action, int a, int b, float now)
    {
        Data l = room.Layout<Data>();
        if (action == PuzzleAction.Reset)
        {
            PutBack(st, room);
            st.SetR(room, PuzzleState.RResets, st.Get(room, PuzzleState.RResets) + 1);
            return true;
        }
        if (action != PuzzleAction.Use) return false;
        if (a < 0 || a >= l.Values.Length) return true;
        int word = st.Get(room, PuzzleState.RA);
        int next = ((word >> (2 * a) & 3) + 1) % 3;
        word = word & ~(3 << (2 * a)) | next << (2 * a);
        st.SetR(room, PuzzleState.RA, word);
        int used = st.Get(room, PuzzleState.RC) + 1;
        st.SetR(room, PuzzleState.RC, used);
        int stage = st.Get(room, PuzzleState.RB);
        (int left, int right) = Pans(l, stage, i => word >> (2 * i) & 3);
        if (left == right)
        {
            stage++;
            st.SetR(room, PuzzleState.RB, stage);
            if (stage >= l.Counters.Length) { st.MarkSolved(r, now); return true; }
        }
        // Out of moves: every weight goes back and it starts over.
        if (used >= l.Budget)
        {
            PutBack(st, room);
            st.SetR(room, PuzzleState.RFails, st.Get(room, PuzzleState.RFails) + 1);
        }
        return true;
    }

    /// <summary>For each weight, presses on the way to the next levelling
    /// (or all of them, for the bot and the script); null when the moves left
    /// cannot do it and the weights must go back first.</summary>
    private static List<int>? Plan(PuzzleState st, RoomPlan room)
    {
        Data l = room.Layout<Data>();
        List<int>? path = PathFrom(l, st.Get(room, PuzzleState.RA), st.Get(room, PuzzleState.RB));
        if (path is null || path.Count > MovesLeft(st, room)) return null;
        return path;
    }

    public override async Task<string?> Solve(IPuzzleDriver d, RoomPlan room, int r)
    {
        Data l = room.Layout<Data>();
        List<int>? path = Plan(d.State, room);
        if (path is null)
        {
            await d.Ask(PuzzleAction.Reset, 0);
            path = Plan(d.State, room);
        }
        if (path is null) return "no levelling within the moves";
        if (path.Count > l.Budget) return "the budget is short of the answer";
        foreach (int i in path) await d.Ask(PuzzleAction.Use, i);
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
        // One pip per levelling under the pivot: lit as each is done.
        int stages = l.Counters.Length;
        for (int s = 0; s < stages; s++)
        {
            var pip = PieceKit.Flat(PieceKit.WallCap);
            v.Pips.Add(pip);
            float x = (s - (stages - 1) * 0.5f) * 0.42f;
            kit.Put(PieceKit.Mesh(new SphereMesh { Radius = 0.15f, Height = 0.3f }, c + Vector3.Up * 2.35f + Dir3(l.Axis) * x, pip, false));
        }
        // Moves left, on the stand, readable from the pedestals.
        v.MovesLabel = new Label3D
        {
            Text = "", FontSize = 44, PixelSize = 0.01f, OutlineSize = 12,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Position = c + Vector3.Up * 5.0f,
            Modulate = new Color(1f, 0.95f, 0.8f), OutlineModulate = new Color(0.1f, 0.08f, 0.05f),
        };
        kit.Put(v.MovesLabel);

        v.LeftPan = Pan(kit, brass);
        v.RightPan = Pan(kit, brass);
        // The counterweight: a big block on the left pan with its number.
        var counter = new Node3D();
        v.LeftPan.AddChild(counter);
        counter.AddChild(PieceKit.Mesh(new BoxMesh { Size = new Vector3(0.9f, 0.7f, 0.9f) }, Vector3.Up * 0.4f, PieceKit.Flat(PieceKit.Iron)));
        v.CounterLabel = new Label3D
        {
            Text = l.Counters[0].ToString(), FontSize = 96, PixelSize = 0.011f, OutlineSize = 18,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Position = Vector3.Up * 1.25f,
            Modulate = new Color(1f, 0.95f, 0.8f), OutlineModulate = new Color(0.1f, 0.08f, 0.05f),
        };
        counter.AddChild(v.CounterLabel);

        for (int i = 0; i < l.Values.Length; i++)
        {
            Vector3 at = room.World(l.Pedestals[i]);
            kit.Put(PieceKit.Mesh(new CylinderMesh { TopRadius = 0.45f, BottomRadius = 0.55f, Height = 0.9f, RadialSegments = 12 }, at + Vector3.Up * 0.45f, PieceKit.Flat(PieceKit.WallCap)));
            kit.Post(at, 0.5f, 1.0f);
            var w = new Node3D { Position = at + Vector3.Up * 1.1f };
            kit.Put(w);
            float s = 0.3f + 0.028f * l.Values[i];
            w.AddChild(PieceKit.Mesh(new CylinderMesh { TopRadius = s * 0.8f, BottomRadius = s, Height = s * 1.1f, RadialSegments = 10 }, Vector3.Up * (s * 0.55f), PieceKit.Flat(PieceKit.Bronze)));
            w.AddChild(new Label3D
            {
                Text = l.Values[i].ToString(), FontSize = 80, PixelSize = 0.011f, OutlineSize = 16,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Position = Vector3.Up * (s * 1.1f + 0.45f),
                Modulate = new Color(1f, 0.95f, 0.8f), OutlineModulate = new Color(0.1f, 0.08f, 0.05f),
            });
            v.Weights.Add(w);
        }
        kit.ResetPost(room.World(l.ResetCell));
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
        int stage = st.Get(room, PuzzleState.RB);
        bool solved = st.Solved(room);
        (int left, int right) = Pans(l, stage, i => Place(st, room, i));
        bool level = solved || left == right;
        float target = level ? 0f : Mathf.Clamp((left - right) * 0.04f, -0.32f, 0.32f);
        v.Tilt = Mathf.MoveToward(v.Tilt, target, dt * 0.5f);
        // Positive tilt: the left end (-Z on the beam) goes down.
        v.Beam.Rotation = new Vector3(-v.Tilt, v.Beam.Rotation.Y, 0f);
        Vector3 pivot = v.Beam.Position;
        Vector3 axis = Dir3(l.Axis);
        Vector3 leftEnd = pivot - axis * (Arm * Mathf.Cos(v.Tilt)) + Vector3.Down * (Arm * Mathf.Sin(v.Tilt));
        Vector3 rightEnd = pivot + axis * (Arm * Mathf.Cos(v.Tilt)) + Vector3.Up * (Arm * Mathf.Sin(v.Tilt));
        v.LeftPan.Position = leftEnd + Vector3.Down * 1.65f;
        v.RightPan.Position = rightEnd + Vector3.Down * 1.65f;
        PieceKit.Lit(v.Dial, level, PieceKit.Teal, PieceKit.WallCap, 2f);
        v.CounterLabel.Text = l.Counters[Math.Clamp(stage, 0, l.Counters.Length - 1)].ToString();
        for (int s = 0; s < v.Pips.Count; s++) PieceKit.Lit(v.Pips[s], solved || s < stage, PieceKit.Teal, PieceKit.WallCap, 2f);
        v.MovesLabel.Text = solved ? "" : Meta.Loc.F(MovesLine, MovesLeft(st, room));
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
                float a = ring * 1.1f;
                home = pan + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (ring == 0 ? 0f : 0.62f) + Vector3.Up * (0.08f + (ring > 5 ? 0.35f : 0f));
            }
            v.Weights[i].Position = v.Weights[i].Position.MoveToward(home, dt * 9f);
        }
    }

    public override void Sounds(object view, RoomPlan room, Func<int, int> was, Func<int, int> now)
    {
        if (now(PuzzleState.RFails) > was(PuzzleState.RFails)) AudioDirector.PlayPuzzle(PuzzleSound.Wrong);
        else if (now(PuzzleState.RB) > was(PuzzleState.RB)) AudioDirector.PlayPuzzle(PuzzleSound.Bell, 1.5f);
        else if (was(PuzzleState.RA) != now(PuzzleState.RA)) AudioDirector.PlayPuzzle(PuzzleSound.Thunk, 1.3f);
    }

    public override void Offer(PuzzleMap map, RoomPlan room, FocusOffer offer)
    {
        Data l = room.Layout<Data>();
        for (int i = 0; i < l.Pedestals.Length; i++) offer(PuzzleAction.Use, i, room.World(l.Pedestals[i]), Verb);
        offer(PuzzleAction.Reset, 0, room.World(l.ResetCell), VerbReset);
    }

    /// <summary>The weight the fewest-moves plan presses next, and how many
    /// times in a row; -1 for the reset post. The plan's own order: another
    /// could level the beam early at a placement that costs more from there.</summary>
    private static int NextWeight(PuzzleState st, RoomPlan room, out int presses)
    {
        presses = 0;
        List<int>? path = Plan(st, room);
        if (path is null || path.Count == 0) return -1;
        while (presses < path.Count && path[presses] == path[0]) presses++;
        return path[0];
    }

    public override PuzzleHint? HintAt(PuzzleState st, RoomPlan room, Vector3? me, float now)
    {
        Data l = room.Layout<Data>();
        int i = NextWeight(st, room, out int presses);
        return i < 0 ? new PuzzleHint(room.World(l.ResetCell), "!") : new PuzzleHint(room.World(l.Pedestals[i]), presses.ToString());
    }

    public override string Status(PuzzleMap map, RoomPlan room)
    {
        Data l = room.Layout<Data>();
        int stage = map.State.Get(room, PuzzleState.RB);
        int moves = MovesLeft(map.State, room);
        if (l.ShowSums)
        {
            (int left, int right) = Pans(l, stage, i => Place(map.State, room, i));
            return Meta.Loc.F(PansLine, left, right) + "  ·  " + Meta.Loc.F(MovesLine, moves);
        }
        return Meta.Loc.F(StageLine, l.Counters[Math.Clamp(stage, 0, l.Counters.Length - 1)], moves, stage, l.Counters.Length);
    }

    public override Vector3 Bot(PuzzleBot bot, RoomPlan room, Vector3 me)
    {
        Data l = room.Layout<Data>();
        int i = NextWeight(bot.State, room, out _);
        if (i < 0) return bot.Use(room, me, room.World(l.ResetCell), PuzzleAction.Reset, 0);
        if (bot.PressIf(PuzzleAction.Use, i, room.Index)) return Vector3.Zero;
        // Stand in front of the pedestal, on the side away from the balance,
        // and go round the row from out there: straight across it, the
        // other pedestals' posts are in the way (eight of them at d5).
        int side = room.EntrySide < 0 ? 3 : room.EntrySide;
        var outward = new Vector3(RoomPlan.Dx[side], 0f, RoomPlan.Dz[side]);
        Vector3 stand = room.World(l.Pedestals[i]) + outward * 1.4f;
        Vector3 rel = me - room.Centre, target = stand - room.Centre;
        float meOut = rel.Dot(outward), standOut = target.Dot(outward);
        Vector3 across = rel - outward * meOut, targetAcross = target - outward * standOut;
        if (meOut < standOut - 0.4f && (across - targetAcross).Length() > 0.8f)
            return PuzzleBot.Toward(me, room.Centre + across + outward * (standOut + 0.3f), 0.1f);
        return PuzzleBot.Toward(me, stand, 0.2f);
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
