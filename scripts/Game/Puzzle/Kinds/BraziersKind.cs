using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Wavebreak.Audio;
using Wavebreak.Core;

namespace Wavebreak.Game.Puzzle.Kinds;

/// <summary>
/// Ember Hall: braziers joined by iron chains on the floor. Using a brazier
/// flips it (lit to dark, dark to lit) and every brazier its chains lead to:
/// a chain with a plain end works both ways, one with an arrowhead only from
/// its tail. A plaque before each brazier says how it must end: a star burns,
/// a cross stays dark. Make every brazier match its plaque at once.
///
/// It is "lights out" on a small graph: pressing a brazier twice undoes it,
/// so the answer is a SET of braziers, each used once, and the order does not
/// change where you end up. From difficulty 2 a fuse adds a clock: the first
/// brazier used starts it, and when it burns out every brazier returns to how
/// the hall began (so a player can experiment freely, but must then play the
/// planned set in one go). Difficulty adds braziers (5 to 10), more and
/// one-way chains, crosses (from 3), a longer fewest-press answer (2 to 7)
/// and a tighter fuse.
///
/// State: RA lit bits; RB fuse end (tenths, 0 when not burning); RFails
/// fuses burnt out (for the sound).
/// Solver: every subset of the braziers (at most 2^10), the fewest presses;
/// the fuse is fitted to the shortest walk through that set (Held-Karp).
/// </summary>
public sealed class BraziersKind : PuzzleKind
{
    public sealed class Data
    {
        public Vector2[] Spots = Array.Empty<Vector2>();
        /// <summary>Per brazier: the braziers using it flips (itself included), as bits.</summary>
        public int[] Effect = Array.Empty<int>();
        /// <summary>Braziers that must burn (bits); the rest must be dark.</summary>
        public int Target;
        public int Start;
        /// <summary>Seconds the fuse burns from the first press; 0: no fuse.</summary>
        public float Fuse;
        /// <summary>Fewest presses from the start (for the HUD and checks).</summary>
        public int Fewest;
        /// <summary>Cache: the fewest presses per pattern to flip (built on first use).</summary>
        public int[]? FlipTable;
        public readonly Dictionary<long, int[]> OrderCache = new();
    }

    private sealed class View
    {
        public readonly List<Node3D> Flames = new();
        public readonly List<Node3D> Rings = new();
        public readonly List<OmniLight3D> Lights = new();
        public readonly List<StandardMaterial3D> Plaques = new();
    }

    private const string Verb = "Light or douse the brazier";
    private const string RightLine = "Braziers as their plaques: {0}/{1}";
    private const string FuseIdleLine = "fuse {0}s, lit by the first brazier you use";
    private const string FuseLine = "fuse burning: {0}s left";

    public override string Id => "braziers";
    public override string Title => "Ember Hall";
    public override string Objective => "Using a brazier flips it and every brazier its chains lead to. Make each one match its plaque: a star burns, a cross stays dark.";
    public override string Hint => "Hint: the marked brazier belongs to the answer. Use each brazier at most once.";
    public override IEnumerable<string> Strings => new[] { Verb, RightLine, FuseIdleLine, FuseLine };
    public override HallStyle Style => HallStyle.Ember;
    public override float ParSeconds => 48f;

    /// <summary>Seconds spent at each brazier (stop, press).</summary>
    private const float AtBrazier = 0.6f;
    /// <summary>Metres saved at each end of a leg: interact reaches that far.</summary>
    private const float ReachSave = 1.6f;

    private static readonly int[] Count = { 5, 6, 7, 8, 9, 10 };
    private static readonly int[] MinPresses = { 2, 3, 4, 5, 6, 7 };

    // ------------------------------------------------------------- data

    public override void Generate(RoomPlan room, Rng rng, int d)
    {
        d = Math.Clamp(d, 0, 5);
        var l = new Data();
        room.Data = l;
        int k = Count[d];
        var doors = new List<Vector2>();
        foreach (int side in new[] { room.EntrySide, room.ExitSide })
            if (side >= 0) doors.Add(RoomPlan.Local(RoomPlan.DoorCell(side)));
        float gap = k >= 8 ? 3.5f : 4.0f;
        bool made = false;
        for (int attempt = 0; attempt < 400; attempt++)
        {
            int need = attempt < 300 ? MinPresses[d] : Math.Max(2, MinPresses[d] - 2);
            var spots = new List<Vector2>();
            for (int tries = 0; tries < 300 && spots.Count < k; tries++)
            {
                var p = new Vector2(rng.Range(-7.4f, 7.4f), rng.Range(-7.4f, 7.4f));
                if (PuzzleGen.NearAny(p, spots, gap) || PuzzleGen.NearAny(p, doors, 3.4f) || p.Length() < 1.2f) continue;
                spots.Add(p);
            }
            if (spots.Count < k) continue;
            l.Spots = spots.ToArray();
            l.Effect = Links(l.Spots, rng, d);
            l.FlipTable = null;
            l.OrderCache.Clear();
            // Plaques: every brazier burns, but from difficulty 3 some must stay dark.
            int all = (1 << k) - 1;
            l.Target = all;
            int dark = d >= 4 ? 2 : d >= 3 ? 1 : 0;
            for (int i = 0; i < dark; i++) l.Target &= ~(1 << rng.RangeInt(0, k));
            // The start: a random set of fires (never already right).
            bool ok = false;
            for (int s = 0; s < 40 && !ok; s++)
            {
                l.Start = rng.RangeInt(0, all + 1);
                int ans = Answer(l, l.Start);
                ok = ans >= 0 && l.Start != l.Target && PuzzleSolver.PopCount(ans) >= need;
            }
            if (!ok) continue;
            l.Fewest = PuzzleSolver.PopCount(Answer(l, l.Start));
            l.Fuse = 0f;
            if (d >= 2)
            {
                BestOrder(l, Answer(l, l.Start), null, out float seconds);
                l.Fuse = Mathf.Ceil(seconds * (1.75f - 0.07f * d) + 3f);
            }
            made = true;
            break;
        }
        if (!made) Fallback(l);
        foreach (Vector2 p in l.Spots) room.Spots.Add(p);
    }

    /// <summary>A ring of five with chains to both neighbours, two of them dark.</summary>
    private static void Fallback(Data l)
    {
        l.Spots = new Vector2[5];
        for (int i = 0; i < 5; i++) l.Spots[i] = new Vector2(Mathf.Cos(Mathf.Tau * i / 5f + 0.3f), Mathf.Sin(Mathf.Tau * i / 5f + 0.3f)) * 5.5f;
        l.Effect = new int[5];
        for (int i = 0; i < 5; i++) l.Effect[i] = 1 << i | 1 << (i + 1) % 5 | 1 << (i + 4) % 5;
        l.Target = 0b11111;
        l.Start = 0b00101;
        l.FlipTable = null;
        l.OrderCache.Clear();
        l.Fewest = PuzzleSolver.PopCount(Answer(l, l.Start));
        l.Fuse = 0f;
    }

    /// <summary>Chains between near braziers: each to one or two of its three
    /// nearest; some one-way from difficulty 2.</summary>
    private static int[] Links(Vector2[] spots, Rng rng, int d)
    {
        int k = spots.Length;
        var eff = new int[k];
        for (int i = 0; i < k; i++) eff[i] = 1 << i;
        float oneWay = d >= 4 ? 0.5f : d >= 2 ? 0.3f : 0f;
        var order = new List<int>();
        for (int i = 0; i < k; i++) order.Add(i);
        PuzzleGen.Shuffle(rng, order);
        foreach (int i in order)
        {
            var near = new List<int>();
            for (int j = 0; j < k; j++) if (j != i) near.Add(j);
            near.Sort((a, b) => spots[i].DistanceSquaredTo(spots[a]).CompareTo(spots[i].DistanceSquaredTo(spots[b])));
            int want = d >= 2 ? rng.RangeInt(1, 3) : 1;
            int made = 0;
            for (int n = 0; n < Math.Min(3, near.Count) && made < want; n++)
            {
                int j = near[n];
                if ((eff[i] >> j & 1) != 0 || (eff[j] >> i & 1) != 0) { made++; continue; }
                if (rng.Chance(0.35f) && n < 2) continue;
                if (rng.Chance(oneWay)) { if (rng.Chance(0.5f)) eff[i] |= 1 << j; else eff[j] |= 1 << i; }
                else { eff[i] |= 1 << j; eff[j] |= 1 << i; }
                made++;
            }
        }
        return eff;
    }

    /// <summary>The fewest braziers to use (bits) to bring <paramref name="lit"/>
    /// to the plaques; -1 when none (never, from a reachable state).</summary>
    public static int Answer(Data l, int lit)
    {
        int k = l.Spots.Length;
        if (l.FlipTable is null || l.FlipTable.Length != 1 << k)
        {
            // Every set once: what it flips, keeping the smallest set per result.
            var best = new int[1 << k];
            Array.Fill(best, -1);
            var flips = new int[1 << k];
            for (int set = 0; set < 1 << k; set++)
            {
                if (set > 0)
                {
                    int low = set & -set;
                    flips[set] = flips[set & (set - 1)] ^ l.Effect[System.Numerics.BitOperations.TrailingZeroCount(low)];
                }
                int x = flips[set];
                if (best[x] < 0 || PuzzleSolver.PopCount(set) < PuzzleSolver.PopCount(best[x])) best[x] = set;
            }
            l.FlipTable = best;
        }
        int want = (lit ^ l.Target) & ((1 << k) - 1);
        return l.FlipTable[want];
    }

    private static float Leg(Vector2 a, Vector2 b) => Mathf.Max(0f, a.DistanceTo(b) - 2f * ReachSave) / PuzzleSolver.WalkSpeed + AtBrazier;

    /// <summary>The order through the braziers in <paramref name="set"/> with
    /// the shortest walk, starting anywhere (<paramref name="from"/> null: the
    /// fuse starts at the first press) or from a point. Held-Karp.</summary>
    public static int[] BestOrder(Data l, int set, Vector2? from, out float seconds)
    {
        var items = new List<int>();
        for (int i = 0; i < l.Spots.Length; i++) if ((set >> i & 1) != 0) items.Add(i);
        int n = items.Count;
        seconds = 0f;
        if (n == 0) return Array.Empty<int>();
        var cost = new float[1 << n, n];
        var prev = new int[1 << n, n];
        for (int m = 0; m < 1 << n; m++)
            for (int j = 0; j < n; j++) cost[m, j] = float.MaxValue;
        for (int j = 0; j < n; j++)
        {
            float first = from is { } f ? Mathf.Max(0f, f.DistanceTo(l.Spots[items[j]]) - ReachSave) / PuzzleSolver.WalkSpeed : 0f;
            cost[1 << j, j] = first + AtBrazier;
            prev[1 << j, j] = -1;
        }
        for (int m = 1; m < 1 << n; m++)
            for (int j = 0; j < n; j++)
            {
                if ((m >> j & 1) == 0 || cost[m, j] == float.MaxValue) continue;
                for (int q = 0; q < n; q++)
                {
                    if ((m >> q & 1) != 0) continue;
                    float c = cost[m, j] + Leg(l.Spots[items[j]], l.Spots[items[q]]);
                    if (c < cost[m | 1 << q, q]) { cost[m | 1 << q, q] = c; prev[m | 1 << q, q] = j; }
                }
            }
        int full = (1 << n) - 1, end = 0;
        for (int j = 1; j < n; j++) if (cost[full, j] < cost[full, end]) end = j;
        seconds = cost[full, end];
        var order = new int[n];
        for (int m = full, j = end, at = n - 1; j >= 0; at--)
        {
            order[at] = items[j];
            int p = prev[m, j];
            m &= ~(1 << j);
            j = p;
        }
        return order;
    }

    private static int Lit(PuzzleState st, RoomPlan room) => st.Get(room, PuzzleState.RA);

    public static bool Burning(PuzzleState st, RoomPlan room, int i)
        => st.Solved(room) ? (room.Layout<Data>().Target >> i & 1) != 0 : (Lit(st, room) >> i & 1) != 0;

    public override void InitState(PuzzleState st, RoomPlan room) => st.V[room.Slot + PuzzleState.RA] = room.Layout<Data>().Start;

    public override void HostStep(PuzzleState st, RoomPlan room, int r, IReadOnlyList<Vector3> players, float now)
    {
        int end = st.Get(room, PuzzleState.RB);
        if (end <= 0 || PuzzleState.Tenths(now) < end) return;
        // The fuse is out: every brazier goes back to how the hall began.
        st.SetR(room, PuzzleState.RA, room.Layout<Data>().Start);
        st.SetR(room, PuzzleState.RB, 0);
        st.SetR(room, PuzzleState.RFails, st.Get(room, PuzzleState.RFails) + 1);
    }

    public override bool Act(PuzzleState st, RoomPlan room, int r, PuzzleAction action, int a, int b, float now)
    {
        if (action != PuzzleAction.Use) return false;
        Data l = room.Layout<Data>();
        if (a < 0 || a >= l.Spots.Length) return true;
        if (l.Fuse > 0f && st.Get(room, PuzzleState.RB) == 0) st.SetR(room, PuzzleState.RB, PuzzleState.Tenths(now + l.Fuse));
        int lit = Lit(st, room) ^ l.Effect[a];
        st.SetR(room, PuzzleState.RA, lit);
        if (lit == l.Target)
        {
            st.SetR(room, PuzzleState.RB, 0);
            st.MarkSolved(r, now);
        }
        return true;
    }

    /// <summary>The brazier to use next from where the room stands: the
    /// fewest-press answer, walked in its shortest order (from
    /// <paramref name="from"/> while the fuse burns).</summary>
    public static int Next(PuzzleState st, RoomPlan room, Vector2 from)
    {
        Data l = room.Layout<Data>();
        int lit = Lit(st, room);
        int ans = Answer(l, lit);
        if (ans <= 0) return -1;
        bool burning = st.Get(room, PuzzleState.RB) > 0;
        int cell = room.CellAt(room.World(from));
        long key = ((long)lit << 20) | ((long)ans << 8) | (burning ? (uint)(cell + 1) : 0u);
        if (!l.OrderCache.TryGetValue(key, out int[]? order))
        {
            order = BestOrder(l, ans, burning ? from : null, out _);
            if (l.OrderCache.Count > 2048) l.OrderCache.Clear();
            l.OrderCache[key] = order;
        }
        return order.Length > 0 ? order[0] : -1;
    }

    public override async Task<string?> Solve(IPuzzleDriver d, RoomPlan room, int r)
    {
        Data l = room.Layout<Data>();
        if (l.Spots.Length < 3) return "too few braziers";
        int lit = Lit(d.State, room);
        int ans = Answer(l, lit);
        if (ans < 0) return "no set of braziers reaches the plaques";
        if (ans == 0) return "already as the plaques";
        int[] order = BestOrder(l, ans, null, out float seconds);
        if (l.Fuse > 0f && seconds * PuzzleSolver.TimingMargin > l.Fuse) return $"fuse {l.Fuse:0.0}s leaves no margin over the best round {seconds:0.0}s";
        // Walk to the first (the fuse is not lit yet), then go round.
        await d.Stand(room.World(l.Spots[order[0]] + (l.Spots[order[0]].Length() > 0.1f ? -l.Spots[order[0]].Normalized() * 1.2f : Vector2.Right)));
        for (int n = 0; n < order.Length; n++)
        {
            if (n > 0) await d.Wait(Leg(l.Spots[order[n - 1]], l.Spots[order[n]]));
            await d.Ask(PuzzleAction.Use, order[n]);
        }
        return null;
    }

    // ------------------------------------------------------------ scene

    public override object Build(PieceKit kit, RoomPlan room)
    {
        Data l = room.Layout<Data>();
        var v = new View();
        var iron = PieceKit.Flat(new Color(0.20f, 0.18f, 0.17f));
        var chain = PieceKit.Flat(new Color(0.34f, 0.30f, 0.27f));
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
            // The fuse: a ring on the floor that shrinks as it burns down.
            var ring = PieceKit.Mesh(new TorusMesh { InnerRadius = 0.95f, OuterRadius = 1.12f, Rings = 28, RingSegments = 6 }, at + Vector3.Up * 0.04f, PieceKit.Glow(PieceKit.Warm, 1.4f), false);
            ring.Visible = false;
            kit.Put(ring);
            v.Rings.Add(ring);
            var light = new OmniLight3D { Position = at + Vector3.Up * 2.4f, LightColor = PieceKit.Flame, LightEnergy = 0f, OmniRange = 5f, ShadowEnabled = false, Visible = false };
            kit.Put(light);
            v.Lights.Add(light);
            kit.Post(at, 0.55f, 1.6f);

            // The plaque, lying on the floor towards the hall's middle: a star
            // (must burn) or a cross (must stay dark).
            bool burn = (l.Target >> i & 1) != 0;
            Vector2 inward = l.Spots[i].Length() > 0.1f ? -l.Spots[i].Normalized() : Vector2.Right;
            var plaqueMat = burn ? PieceKit.Glow(new Color(1f, 0.78f, 0.3f), 0.8f) : PieceKit.Flat(new Color(0.12f, 0.15f, 0.2f));
            var plaque = PieceKit.Symbol(burn ? RuneSymbol.Star : RuneSymbol.Cross, plaqueMat);
            plaque.Position = room.World(l.Spots[i] + inward * 1.15f) + Vector3.Up * 0.08f;
            plaque.Rotation = new Vector3(-Mathf.Pi * 0.5f, 0f, 0f);
            plaque.Scale = Vector3.One * 1.05f;
            kit.Put(plaque);
            v.Plaques.Add(plaqueMat);
        }
        // Chains: a bar between the bases; a one-way chain ends in an arrowhead.
        for (int i = 0; i < l.Spots.Length; i++)
            for (int j = 0; j < l.Spots.Length; j++)
            {
                if (i == j || (l.Effect[i] >> j & 1) == 0) continue;
                bool both = (l.Effect[j] >> i & 1) != 0;
                if (both && j < i) continue;
                Vector2 a = l.Spots[i], b = l.Spots[j];
                Vector2 dir = (b - a).Normalized();
                Vector3 pa = room.World(a + dir * 0.7f) + Vector3.Up * 0.07f, pb = room.World(b - dir * (both ? 0.7f : 1.25f)) + Vector3.Up * 0.07f;
                var holder = new Node3D();
                kit.Put(holder);
                PieceKit.Segment(holder, pa, pb, chain, 0.14f);
                // Links: small blocks along the bar so it reads as a chain.
                float len = pa.DistanceTo(pb);
                for (float s = 0.4f; s < len - 0.2f; s += 0.6f)
                {
                    var link = PieceKit.Mesh(new BoxMesh { Size = new Vector3(0.26f, 0.12f, 0.26f) }, pa.Lerp(pb, s / len), chain, false);
                    link.Rotation = new Vector3(0f, Mathf.Atan2(dir.X, dir.Y) + Mathf.Pi * 0.25f, 0f);
                    kit.Put(link);
                }
                if (!both)
                {
                    var head = PieceKit.Mesh(new CylinderMesh { TopRadius = 0f, BottomRadius = 0.32f, Height = 0.6f, RadialSegments = 3 }, pb + new Vector3(dir.X, 0f, dir.Y) * 0.25f, PieceKit.Glow(PieceKit.Bronze, 0.5f), false);
                    // Cone tip along +Y, laid down to point along the chain.
                    head.Rotation = new Vector3(Mathf.Pi * 0.5f, Mathf.Atan2(dir.X, dir.Y), 0f);
                    kit.Put(head);
                }
            }
        return v;
    }

    public override void Sync(PieceKit kit, object view, RoomPlan room, PuzzleState st, float t, float dt, bool changed)
    {
        var v = (View)view;
        Data l = room.Layout<Data>();
        bool solved = st.Solved(room);
        int end = st.Get(room, PuzzleState.RB);
        float left = end > 0 ? end / 10f - t : 0f;
        float frac = solved || end <= 0 || l.Fuse <= 0f ? 1f : Mathf.Clamp(left / l.Fuse, 0.15f, 1f);
        for (int i = 0; i < l.Spots.Length; i++)
        {
            bool on = Burning(st, room, i);
            Node3D flame = v.Flames[i];
            flame.Visible = on;
            float flicker = 1f + 0.08f * Mathf.Sin(t * 13f + i * 1.7f);
            float size = 0.55f + 0.45f * frac;
            flame.Scale = new Vector3(size, Mathf.Max(0.01f, size * flicker), size);
            v.Rings[i].Visible = on && !solved && end > 0;
            if (v.Rings[i].Visible) v.Rings[i].Scale = new Vector3(frac, 1f, frac);
            v.Lights[i].Visible = on;
            v.Lights[i].LightEnergy = on ? 1.2f * size : 0f;
            // A plaque glows when its brazier is as it asks.
            bool right = on == ((l.Target >> i & 1) != 0);
            v.Plaques[i].EmissionEnabled = right;
            v.Plaques[i].Emission = (l.Target >> i & 1) != 0 ? PieceKit.Warm : PieceKit.Teal;
            v.Plaques[i].EmissionEnergyMultiplier = right ? 1.2f : 0f;
        }
    }

    public override void Sounds(object view, RoomPlan room, Func<int, int> was, Func<int, int> now)
    {
        if (now(PuzzleState.RFails) > was(PuzzleState.RFails)) AudioDirector.PlayPuzzle(PuzzleSound.Click, 0.6f);
        else if (now(PuzzleState.RA) != was(PuzzleState.RA)) AudioDirector.PlayPuzzle(PuzzleSound.Gust, 1.5f);
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
        int i = Next(st, room, LocalOf(room, me));
        return i < 0 ? null : new PuzzleHint(room.World(room.Layout<Data>().Spots[i]), "!");
    }

    public override string Status(PuzzleMap map, RoomPlan room)
    {
        Data l = room.Layout<Data>();
        PuzzleState st = map.State;
        int right = 0;
        for (int i = 0; i < l.Spots.Length; i++) if (Burning(st, room, i) == ((l.Target >> i & 1) != 0)) right++;
        string line = Meta.Loc.F(RightLine, right, l.Spots.Length);
        if (l.Fuse <= 0f || st.Solved(room)) return line;
        int end = st.Get(room, PuzzleState.RB);
        return line + "  ·  " + (end > 0
            ? Meta.Loc.F(FuseLine, Mathf.Max(0f, end / 10f - map.Elapsed).ToString("0.0"))
            : Meta.Loc.F(FuseIdleLine, Mathf.RoundToInt(l.Fuse)));
    }

    public override Vector3 Bot(PuzzleBot bot, RoomPlan room, Vector3 me)
    {
        int i = Next(bot.State, room, LocalOf(room, me));
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
