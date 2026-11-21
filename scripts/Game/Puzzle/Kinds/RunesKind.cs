using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Wavebreak.Audio;
using Wavebreak.Core;

namespace Wavebreak.Game.Puzzle.Kinds;

/// <summary>
/// Bell Room: rune bells stand in a ring, and stone tablets round the walls
/// carry clues about the order they must be struck in ("Star rings before
/// Circle", "Strike number 2 is not Square", "Cross and Diamond are not
/// struck back to back", "Triangle stays silent"...). Nothing rings the
/// order: it has to be worked out. The clues are a minimal set (every one is
/// needed) that leaves exactly one order, checked against every possible
/// order of the bells. A wrong bell, or a decoy that never rings, starts the
/// order over. From difficulty 3 the bells hum after each strike and the next
/// must be struck before the hum fades, so the order has to be known before
/// the first strike.
///
/// Difficulty: d0 four bells, all struck, place clues allowed (about three
/// clues); d1 five bells with a decoy; d2 five struck; d3 six bells with a
/// decoy, no place clues, a 9 s hum; d4 six struck, 8 s; d5 six struck, only
/// the weakest clues (before, not-at, not back to back), 6.5 s. Struck bells
/// stay lit, so progress reads at a glance.
///
/// State: RA bells struck right in a row; RB when the last was struck
/// (tenths, -1 none); RFails wrong bells and faded hums.
/// </summary>
public sealed class RunesKind : PuzzleKind
{
    public enum ClueType { Before, Next, At, NotAt, Apart, Silent }

    /// <summary>One tablet: <see cref="A"/> and <see cref="B"/> are bells, or
    /// for <see cref="ClueType.At"/>/<see cref="ClueType.NotAt"/> B is the
    /// place in the order (0-based).</summary>
    public readonly record struct Clue(ClueType Type, int A, int B);

    public sealed class Data
    {
        public Vector2[] Bells = Array.Empty<Vector2>();
        public RuneSymbol[] Symbols = Array.Empty<RuneSymbol>();
        public int[] Sequence = Array.Empty<int>();
        public Clue[] Clues = Array.Empty<Clue>();
        /// <summary>Room-local spots of the clue tablets, one per clue.</summary>
        public Vector2[] Tablets = Array.Empty<Vector2>();
        /// <summary>Seconds the hum lasts after a strike (0: no hum).</summary>
        public float Hum;
    }

    private sealed class View
    {
        public readonly List<Node3D> Bells = new();
        public readonly List<Node3D> Symbols = new();
        public readonly List<StandardMaterial3D> Mats = new();
        public int Struck = -1;
        public float StruckFor;
    }

    private const string VerbBell = "Strike the bell";
    private const string Progress = "Bells struck in order: {0}/{1}";
    private const string ProgressHum = "Bells struck in order: {0}/{1}  ·  the hum fades in {2} s";
    private const string ClueBefore = "{0} rings before {1}";
    private const string ClueNext = "{0} rings right after {1}";
    private const string ClueAt = "Strike number {1} is {0}";
    private const string ClueNotAt = "Strike number {1} is not {0}";
    private const string ClueApart = "{0} and {1} are not struck back to back";
    private const string ClueSilent = "{0} stays silent";
    private const string Tablet = "Clue";

    public override string Id => "runes";
    public override string Title => "Bell Room";
    public override string Objective => "Read the clue tablets by the walls, work out the order and strike the bells in it. A wrong bell starts over.";
    public override string Hint => "Hint: the next bell is marked.";
    public override IEnumerable<string> Strings => new[] { VerbBell, Progress, ProgressHum, ClueBefore, ClueNext, ClueAt, ClueNotAt, ClueApart, ClueSilent, Tablet };
    public override HallStyle Style => HallStyle.Temple;
    public override float ParSeconds => 30f;

    private static readonly float[] Pitch = { 1f, 1.122f, 1.26f, 1.335f, 1.498f, 1.682f };

    // ------------------------------------------------------------- data

    /// <summary>Bells, bells struck, hum seconds, fewest clues for a difficulty.</summary>
    private static (int Bells, int Struck, float Hum, int MinClues) Knobs(int d) => Math.Clamp(d, 0, 5) switch
    {
        0 => (4, 4, 0f, 2),
        1 => (5, 4, 0f, 3),
        2 => (5, 5, 0f, 4),
        3 => (6, 5, 9f, 4),
        4 => (6, 6, 8f, 5),
        _ => (6, 6, 6.5f, 5),
    };

    /// <summary>Clue kinds a difficulty may use, weakest first (the generator
    /// tries them in that order, so a place clue only appears when needed).</summary>
    private static ClueType[] Types(int d, bool relaxed) => Math.Clamp(d, 0, 5) switch
    {
        0 => new[] { ClueType.Before, ClueType.Next, ClueType.At },
        1 => new[] { ClueType.Before, ClueType.Silent, ClueType.NotAt, ClueType.Next, ClueType.At },
        2 => new[] { ClueType.Before, ClueType.NotAt, ClueType.Next, ClueType.At },
        3 => relaxed ? new[] { ClueType.Before, ClueType.Apart, ClueType.NotAt, ClueType.Silent, ClueType.Next, ClueType.At }
                     : new[] { ClueType.Before, ClueType.Apart, ClueType.NotAt, ClueType.Silent, ClueType.Next },
        4 => relaxed ? new[] { ClueType.Before, ClueType.Apart, ClueType.NotAt, ClueType.Next, ClueType.At }
                     : new[] { ClueType.Before, ClueType.Apart, ClueType.NotAt, ClueType.Next },
        _ => relaxed ? new[] { ClueType.Before, ClueType.Apart, ClueType.NotAt, ClueType.Next }
                     : new[] { ClueType.Before, ClueType.Apart, ClueType.NotAt },
    };

    public const int MaxClues = 8;

    public override void Generate(RoomPlan room, Rng rng, int d)
    {
        var l = new Data();
        room.Data = l;
        (int bells, int struck, float hum, int minClues) = Knobs(d);
        l.Hum = hum;
        float offset = rng.Range(0f, Mathf.Tau);
        l.Bells = new Vector2[bells];
        for (int i = 0; i < bells; i++)
        {
            float a = offset + Mathf.Tau * i / bells;
            l.Bells[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 5f;
        }
        var symbols = Enum.GetValues<RuneSymbol>().ToList();
        PuzzleGen.Shuffle(rng, symbols);
        l.Symbols = symbols.Take(bells).ToArray();
        List<int[]> all = Orders(bells, struck);
        // From difficulty 2, of a few good draws the one with the most clues
        // wins: more, weaker clues are a longer chain of reasoning.
        int wanted = d >= 2 ? 5 : 1, good = 0;
        bool done = false;
        for (int attempt = 0; attempt < 80 && good < wanted; attempt++)
        {
            var pick = Enumerable.Range(0, bells).ToList();
            PuzzleGen.Shuffle(rng, pick);
            int[] seq = pick.Take(struck).ToArray();
            Clue[]? clues = MakeClues(seq, bells, all, Types(d, attempt >= 40), rng);
            if (clues is null || clues.Length > MaxClues || clues.Length < Math.Min(minClues, attempt >= 60 ? 1 : minClues)) continue;
            good++;
            if (done && clues.Length <= l.Clues.Length) continue;
            l.Sequence = seq;
            l.Clues = clues;
            done = true;
        }
        if (!done)
        {
            // Fallback: every place spelled out (always exactly one order).
            l.Sequence = Enumerable.Range(0, struck).ToArray();
            var list = new List<Clue>();
            for (int p = 0; p < struck; p++) list.Add(new Clue(ClueType.At, l.Sequence[p], p));
            for (int b = struck; b < bells; b++) list.Add(new Clue(ClueType.Silent, b, 0));
            l.Clues = list.Take(MaxClues).ToArray();
        }
        l.Tablets = TabletSpots(room, rng, l.Clues.Length);
        room.Spots.AddRange(l.Bells);
        if (d >= 1) PuzzleGen.AddSpikes(room, rng, c => RoomPlan.Local(c).Length() < 2.5f || PuzzleGen.NearAny(RoomPlan.Local(c), l.Bells, 2.2f));
        // Reading and reasoning take most of it; striking is quick.
        room.ParSeconds = 18f + 9f * l.Clues.Length + 3f * struck + (hum > 0f ? 6f : 0f);
    }

    /// <summary>Every order of <paramref name="struck"/> different bells out of <paramref name="bells"/>.</summary>
    private static List<int[]> Orders(int bells, int struck)
    {
        var list = new List<int[]>();
        var cur = new int[struck];
        void Go(int depth, int used)
        {
            if (depth == struck) { list.Add((int[])cur.Clone()); return; }
            for (int b = 0; b < bells; b++)
            {
                if ((used >> b & 1) != 0) continue;
                cur[depth] = b;
                Go(depth + 1, used | 1 << b);
            }
        }
        Go(0, 0);
        return list;
    }

    private static int PosOf(int[] seq, int bell)
    {
        for (int i = 0; i < seq.Length; i++) if (seq[i] == bell) return i;
        return -1;
    }

    /// <summary>Does this order keep the clue?</summary>
    public static bool Holds(Clue c, int[] seq)
    {
        int pa = PosOf(seq, c.A);
        int pb = c.Type is ClueType.At or ClueType.NotAt or ClueType.Silent ? -1 : PosOf(seq, c.B);
        return c.Type switch
        {
            ClueType.Before => pa >= 0 && pb >= 0 && pa < pb,
            ClueType.Next => pa >= 0 && pb >= 0 && pa == pb + 1,   // A rings right after B
            ClueType.At => pa == c.B,
            ClueType.NotAt => pa != c.B,
            ClueType.Apart => pa >= 0 && pb >= 0 && Math.Abs(pa - pb) > 1,
            ClueType.Silent => pa < 0,
            _ => false,
        };
    }

    /// <summary>A minimal set of true clues that leaves only
    /// <paramref name="answer"/> among <paramref name="all"/>, or null.</summary>
    private static Clue[]? MakeClues(int[] answer, int bells, List<int[]> all, ClueType[] types, Rng rng)
    {
        int struck = answer.Length;
        var pool = new List<Clue>();
        var strong = new List<Clue>();
        foreach (ClueType t in types)
        {
            var group = new List<Clue>();
            for (int a = 0; a < bells; a++)
            {
                switch (t)
                {
                    case ClueType.At:
                    case ClueType.NotAt:
                        for (int p = 0; p < struck; p++) group.Add(new Clue(t, a, p));
                        break;
                    case ClueType.Silent:
                        group.Add(new Clue(t, a, 0));
                        break;
                    default:
                        for (int b = 0; b < bells; b++)
                            if (b != a && (t != ClueType.Apart || a < b)) group.Add(new Clue(t, a, b));
                        break;
                }
            }
            group.RemoveAll(c => !Holds(c, answer));
            // Places and neighbours say a lot at once: they only come in
            // when the rest cannot pin the order.
            (t is ClueType.At or ClueType.Next ? strong : pool).AddRange(group);
        }
        // The weak kinds mixed (a chain of "before" alone would be a sort,
        // not a puzzle), the strong ones after.
        PuzzleGen.Shuffle(rng, pool);
        PuzzleGen.Shuffle(rng, strong);
        pool.AddRange(strong);
        // Add a clue whenever it rules something out.
        var chosen = new List<Clue>();
        List<int[]> left = all;
        foreach (Clue c in pool)
        {
            if (left.Count <= 1) break;
            List<int[]> next = left.Where(s => Holds(c, s)).ToList();
            if (next.Count == left.Count) continue;
            chosen.Add(c);
            left = next;
        }
        if (left.Count != 1) return null;
        // Then drop every clue the others already imply.
        PuzzleGen.Shuffle(rng, chosen);
        for (int i = chosen.Count - 1; i >= 0; i--)
        {
            Clue drop = chosen[i];
            chosen.RemoveAt(i);
            if (Count(all, chosen) != 1) chosen.Insert(i, drop);
        }
        return chosen.ToArray();
    }

    /// <summary>Orders that keep every clue.</summary>
    public static int Count(List<int[]> all, IReadOnlyList<Clue> clues)
    {
        int n = 0;
        foreach (int[] s in all)
        {
            bool ok = true;
            foreach (Clue c in clues) if (!Holds(c, s)) { ok = false; break; }
            if (ok) n++;
        }
        return n;
    }

    /// <summary>Tablets stand in the open strip by the walls, two per side,
    /// clear of the doorways and of the corner props.</summary>
    private static Vector2[] TabletSpots(RoomPlan room, Rng rng, int count)
    {
        var spots = new List<Vector2>();
        for (int side = 0; side < 4; side++)
        {
            var dir = new Vector2(RoomPlan.Dx[side], RoomPlan.Dz[side]);
            var along = new Vector2(-RoomPlan.Dz[side], RoomPlan.Dx[side]);
            foreach (float a in new[] { -5.5f, 5.5f }) spots.Add(dir * 10.6f + along * a);
        }
        PuzzleGen.Shuffle(rng, spots);
        // The entry side first: the first clues are read on the way in.
        Vector2 entry = room.EntrySide >= 0 ? new Vector2(RoomPlan.Dx[room.EntrySide], RoomPlan.Dz[room.EntrySide]) : Vector2.Zero;
        spots.Sort((a, b) => b.Dot(entry).CompareTo(a.Dot(entry)));
        return spots.Take(count).ToArray();
    }

    /// <summary>The clue in words, in the player's language.</summary>
    public static string Say(Data l, Clue c)
    {
        string a = Meta.Loc.T(PuzzleText.SymbolName(l.Symbols[c.A]));
        string b = c.Type is ClueType.At or ClueType.NotAt or ClueType.Silent ? "" : Meta.Loc.T(PuzzleText.SymbolName(l.Symbols[c.B]));
        return c.Type switch
        {
            ClueType.Before => Meta.Loc.F(ClueBefore, a, b),
            ClueType.Next => Meta.Loc.F(ClueNext, a, b),
            ClueType.At => Meta.Loc.F(ClueAt, a, c.B + 1),
            ClueType.NotAt => Meta.Loc.F(ClueNotAt, a, c.B + 1),
            ClueType.Apart => Meta.Loc.F(ClueApart, a, b),
            ClueType.Silent => Meta.Loc.F(ClueSilent, a),
            _ => "",
        };
    }

    public override void InitState(PuzzleState st, RoomPlan room) => st.V[room.Slot + PuzzleState.RB] = -1;

    /// <summary>Seconds of hum left, or -1 when no hum is running.</summary>
    public static float HumLeft(PuzzleState st, RoomPlan room, float now)
    {
        Data l = room.Layout<Data>();
        int at = st.Get(room, PuzzleState.RB);
        if (l.Hum <= 0f || at < 0 || st.Get(room, PuzzleState.RA) <= 0) return -1f;
        return Mathf.Max(0f, l.Hum - (now - at / 10f));
    }

    public override void HostStep(PuzzleState st, RoomPlan room, int r, IReadOnlyList<Vector3> players, float now)
    {
        // The hum faded before the next bell: the order starts over.
        if (HumLeft(st, room, now) == 0f)
        {
            st.SetR(room, PuzzleState.RA, 0);
            st.SetR(room, PuzzleState.RB, -1);
            st.SetR(room, PuzzleState.RFails, st.Get(room, PuzzleState.RFails) + 1);
        }
    }

    public override bool Act(PuzzleState st, RoomPlan room, int r, PuzzleAction action, int a, int b, float now)
    {
        Data l = room.Layout<Data>();
        if (action != PuzzleAction.Bell) return false;
        if (a < 0 || a >= l.Bells.Length) return true;
        int progress = Math.Clamp(st.Get(room, PuzzleState.RA), 0, l.Sequence.Length - 1);
        if (l.Sequence[progress] == a)
        {
            st.SetR(room, PuzzleState.RA, progress + 1);
            st.SetR(room, PuzzleState.RB, PuzzleState.Tenths(now));
            if (progress + 1 >= l.Sequence.Length) st.MarkSolved(r, now);
            return true;
        }
        // Wrong (or a silent decoy): start over.
        st.SetR(room, PuzzleState.RA, 0);
        st.SetR(room, PuzzleState.RB, -1);
        st.SetR(room, PuzzleState.RFails, st.Get(room, PuzzleState.RFails) + 1);
        return true;
    }

    public override async Task<string?> Solve(IPuzzleDriver d, RoomPlan room, int r)
    {
        Data l = room.Layout<Data>();
        if (l.Sequence.Length < 3 || l.Symbols.Distinct().Count() != l.Bells.Length) return "bells not told apart";
        if (l.Clues.Length != l.Tablets.Length) return "a clue has no tablet";
        if (Count(Orders(l.Bells.Length, l.Sequence.Length), l.Clues) != 1 || !l.Clues.All(c => Holds(c, l.Sequence)))
            return "the clues do not give exactly the order";
        await d.Stand(room.Centre + new Vector3(2f, 0f, 2f));
        // A wrong bell must start it over.
        int fails = d.State.Get(room, PuzzleState.RFails);
        await d.Ask(PuzzleAction.Bell, (l.Sequence[0] + 1) % l.Bells.Length);
        if (d.State.Get(room, PuzzleState.RFails) != fails + 1) return "a wrong bell was not wrong";
        if (d.State.Get(room, PuzzleState.RA) != 0) return "a wrong bell kept the progress";
        foreach (int b in l.Sequence)
        {
            await d.Ask(PuzzleAction.Bell, b);
            await d.Wait(0.2f);
        }
        return null;
    }

    // ------------------------------------------------------------ scene

    public override object Build(PieceKit kit, RoomPlan room)
    {
        Data l = room.Layout<Data>();
        var v = new View();
        for (int i = 0; i < l.Bells.Length; i++)
        {
            Vector3 at = room.World(l.Bells[i]);
            kit.Put(PieceKit.Mesh(new BoxMesh { Size = new Vector3(0.9f, 1.0f, 0.9f) }, at + Vector3.Up * 0.5f, PieceKit.Flat(PieceKit.WallCap)));
            // Two posts and a beam, the bell hanging under it.
            foreach (float s in new[] { -0.4f, 0.4f })
                kit.Put(PieceKit.Mesh(new BoxMesh { Size = new Vector3(0.1f, 1.1f, 0.1f) }, at + new Vector3(s, 1.5f, 0f), PieceKit.Flat(PieceKit.BronzeDark)));
            kit.Put(PieceKit.Mesh(new BoxMesh { Size = new Vector3(0.95f, 0.1f, 0.12f) }, at + Vector3.Up * 2.05f, PieceKit.Flat(PieceKit.BronzeDark)));
            var mat = PieceKit.Flat(PieceKit.Bronze);
            var bell = new Node3D { Position = at + Vector3.Up * 1.55f };
            kit.Put(bell);
            bell.AddChild(PieceKit.Mesh(new CylinderMesh { TopRadius = 0.18f, BottomRadius = 0.36f, Height = 0.55f, RadialSegments = 14 }, Vector3.Zero, mat));
            v.Bells.Add(bell);
            v.Mats.Add(mat);
            // The bell's symbol floats above it, turning slowly so it reads from anywhere.
            Node3D symbol = PieceKit.Symbol(l.Symbols[i], mat);
            symbol.Position = at + Vector3.Up * 2.75f;
            kit.Put(symbol);
            v.Symbols.Add(symbol);
            // Its name under it, so the tablets' words match a bell.
            kit.Put(new Label3D
            {
                Text = Meta.Loc.T(PuzzleText.SymbolName(l.Symbols[i])), FontSize = 40, PixelSize = 0.01f, OutlineSize = 10,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Position = at + Vector3.Up * 3.45f,
                Modulate = new Color(1f, 0.93f, 0.78f), OutlineModulate = new Color(0.1f, 0.08f, 0.05f),
            });
            kit.Post(at, 0.55f, 2.2f);
        }
        // A low stone in the middle, where the ring is read from.
        Vector3 c = room.Centre;
        kit.Put(PieceKit.Mesh(new CylinderMesh { TopRadius = 0.5f, BottomRadius = 0.65f, Height = 0.5f, RadialSegments = 12 }, c + Vector3.Up * 0.25f, PieceKit.Flat(PieceKit.WallCap)));
        // The clue tablets by the walls.
        var stone = PieceKit.Flat(new Color(0.62f, 0.58f, 0.66f));
        var glyph = PieceKit.Glow(PieceKit.Warm, 1.1f);
        for (int i = 0; i < l.Clues.Length; i++)
        {
            Vector3 at = room.World(l.Tablets[i]);
            Vector3 face = (room.Centre - at) with { Y = 0f };
            float yaw = Mathf.Atan2(face.X, face.Z);
            var slab = new Node3D { Position = at, Rotation = new Vector3(0f, yaw, 0f) };
            kit.Put(slab);
            slab.AddChild(PieceKit.Mesh(new BoxMesh { Size = new Vector3(2.2f, 1.7f, 0.3f) }, Vector3.Up * 0.85f, stone));
            slab.AddChild(PieceKit.Mesh(new BoxMesh { Size = new Vector3(2.4f, 0.16f, 0.4f) }, Vector3.Up * 1.78f, PieceKit.Flat(PieceKit.BronzeDark)));
            Clue clue = l.Clues[i];
            // The bells it names, as their carved shapes over the slab.
            Node3D first = PieceKit.Symbol(l.Symbols[clue.A], glyph);
            first.Scale = Vector3.One * 0.55f;
            first.Position = new Vector3(clue.Type is ClueType.At or ClueType.NotAt or ClueType.Silent ? 0f : -0.55f, 2.35f, 0f);
            slab.AddChild(first);
            if (clue.Type is not (ClueType.At or ClueType.NotAt or ClueType.Silent))
            {
                Node3D second = PieceKit.Symbol(l.Symbols[clue.B], glyph);
                second.Scale = Vector3.One * 0.55f;
                second.Position = new Vector3(0.55f, 2.35f, 0f);
                slab.AddChild(second);
            }
            // Carved on the face that looks at the bells (the slab's +Z).
            slab.AddChild(new Label3D
            {
                Text = Meta.Loc.T(Tablet) + " " + (i + 1) + "\n" + Say(l, clue),
                FontSize = 34, PixelSize = 0.0072f, OutlineSize = 8, Width = 280f,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                Position = new Vector3(0f, 0.95f, 0.17f),
                Modulate = new Color(1f, 0.96f, 0.86f), OutlineModulate = new Color(0.08f, 0.06f, 0.1f),
            });
            kit.Post(at, 0.9f, 1.8f);
        }
        return v;
    }

    public override void Sync(PieceKit kit, object view, RoomPlan room, PuzzleState st, float t, float dt, bool changed)
    {
        var v = (View)view;
        Data l = room.Layout<Data>();
        int progress = st.Get(room, PuzzleState.RA);
        bool solved = st.Solved(room);
        float hum = HumLeft(st, room, t);
        for (int i = 0; i < l.Bells.Length; i++)
        {
            int at = Array.IndexOf(l.Sequence, i);
            bool done = solved || at >= 0 && at < progress;
            bool ringing = i == v.Struck && v.StruckFor > 0f;
            StandardMaterial3D m = v.Mats[i];
            m.EmissionEnabled = done || ringing;
            m.Emission = PieceKit.Warm;
            // A humming bell fades with its hum, so the time left shows on it.
            m.EmissionEnergyMultiplier = ringing ? 2.6f : hum >= 0f && at == progress - 1 ? 0.6f + 1.6f * hum / Mathf.Max(0.1f, l.Hum) : 1.2f;
            v.Bells[i].Rotation = new Vector3(0f, 0f, ringing ? Mathf.Sin(t * 22f) * 0.25f : 0f);
            v.Symbols[i].RotateY(dt * (ringing ? 3f : 0.6f));
            v.Symbols[i].Scale = Vector3.One * (done ? 1.2f : 1f);
        }
        v.StruckFor = Mathf.Max(0f, v.StruckFor - dt);
    }

    public override void Sounds(object view, RoomPlan room, Func<int, int> was, Func<int, int> now)
    {
        var v = (View)view;
        Data l = room.Layout<Data>();
        if (now(PuzzleState.RA) > was(PuzzleState.RA))
        {
            int bell = l.Sequence[Mathf.Clamp(now(PuzzleState.RA) - 1, 0, l.Sequence.Length - 1)];
            v.Struck = bell;
            v.StruckFor = 0.5f;
            AudioDirector.PlayPuzzle(PuzzleSound.Bell, Pitch[bell % Pitch.Length]);
        }
        if (now(PuzzleState.RFails) > was(PuzzleState.RFails)) AudioDirector.PlayPuzzle(PuzzleSound.Wrong);
    }

    public override void Offer(PuzzleMap map, RoomPlan room, FocusOffer offer)
    {
        Data l = room.Layout<Data>();
        for (int i = 0; i < l.Bells.Length; i++) offer(PuzzleAction.Bell, i, room.World(l.Bells[i]), VerbBell);
    }

    private static int Next(PuzzleState st, RoomPlan room)
    {
        Data l = room.Layout<Data>();
        return l.Sequence[Mathf.Clamp(st.Get(room, PuzzleState.RA), 0, l.Sequence.Length - 1)];
    }

    public override PuzzleHint? HintAt(PuzzleState st, RoomPlan room, Vector3? me, float now)
        => new PuzzleHint(room.World(room.Layout<Data>().Bells[Next(st, room)]), "!");

    public override string Status(PuzzleMap map, RoomPlan room)
    {
        int progress = map.State.Get(room, PuzzleState.RA), total = room.Layout<Data>().Sequence.Length;
        float hum = HumLeft(map.State, room, map.Elapsed);
        return hum >= 0f ? Meta.Loc.F(ProgressHum, progress, total, Mathf.CeilToInt(hum)) : Meta.Loc.F(Progress, progress, total);
    }

    public override Vector3 Bot(PuzzleBot bot, RoomPlan room, Vector3 me) => BellThink(bot, room, me, Next(bot.State, room));

    /// <summary>Bells stand in a ring: go round outside the ring to the
    /// bell's spoke, then step in until it is the focus.</summary>
    private static Vector3 BellThink(PuzzleBot bot, RoomPlan room, Vector3 me, int bell)
    {
        if (bot.PressIf(PuzzleAction.Bell, bell, room.Index)) return Vector3.Zero;
        Vector3 c = room.Centre;
        Vector3 b = room.World(room.Layout<Data>().Bells[bell]);
        Vector3 spoke = ((b - c) with { Y = 0f }).Normalized();
        Vector3 off = (me - c) with { Y = 0f };
        float radius = off.Length();
        Vector3 stand = b + spoke * 1.5f;
        if (me.DistanceTo(stand) < 1.2f || (me - b).Length() < 2.2f) return (b - me) with { Y = 0f };
        Vector3 outward = radius > 0.01f ? off / radius : spoke;
        // Too far in: out past the ring first. Then round it at about 6.5 m.
        if (radius < 5.9f) return outward;
        float turn = outward.Cross(spoke).Y;   // which way round is shorter
        Vector3 tangent = new Vector3(-outward.Z, 0f, outward.X) * (turn >= 0f ? -1f : 1f);
        float angle = Mathf.Acos(Mathf.Clamp(outward.Dot(spoke), -1f, 1f));
        if (angle < 0.25f) return PuzzleBot.Toward(me, stand, 0.2f);
        return (tangent + outward * (6.6f - radius) * 0.8f).Normalized();
    }

    public override void Glyph(CanvasItem ci, Vector2 at, float size, Color c)
    {
        float h = size * 0.6f;
        ci.DrawPolyline(new[] { at + new Vector2(-h * 0.7f, h * 0.5f), at + new Vector2(-h * 0.35f, -h * 0.5f), at + new Vector2(h * 0.35f, -h * 0.5f), at + new Vector2(h * 0.7f, h * 0.5f), at + new Vector2(-h * 0.7f, h * 0.5f) }, c, Mathf.Max(1f, size * 0.16f));
        ci.DrawCircle(at + new Vector2(0f, h * 0.75f), size * 0.15f, c);
    }
}
