using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Wavebreak.Audio;
using Wavebreak.Core;

namespace Wavebreak.Game.Puzzle.Kinds;

/// <summary>
/// Bell Room: rune bells in a ring ring an order on entry (each lights, sways
/// and plays its own note); strike them in that order. A wrong bell starts
/// over and the order rings again; the centre bell (and a hint) rings it too.
///
/// State: RA bells struck right in a row; RB order shown from (tenths, -1
/// none); RC times shown; RFails wrong bells.
/// </summary>
public sealed class RunesKind : PuzzleKind
{
    public sealed class Data
    {
        public Vector2[] Bells = Array.Empty<Vector2>();
        public RuneSymbol[] Symbols = Array.Empty<RuneSymbol>();
        public int[] Sequence = Array.Empty<int>();
    }

    private sealed class View
    {
        public readonly List<Node3D> Bells = new();
        public readonly List<Node3D> Symbols = new();
        public readonly List<StandardMaterial3D> Mats = new();
        public int ShowStep = -1;
        public int Struck = -1;
        public float StruckFor;
    }

    private const string VerbBell = "Strike the bell";
    private const string VerbEcho = "Ring the order again";
    private const string Listen = "Listen and watch the order...";
    private const string Progress = "Bells struck in order: {0}/{1}";

    public override string Id => "runes";
    public override string Title => "Bell Room";
    public override string Objective => "Strike the bells in the order they rang.";
    public override string Hint => "Hint: the next bell is marked. Strike the centre bell to hear the order again.";
    public override IEnumerable<string> Strings => new[] { VerbBell, VerbEcho, Listen, Progress };
    public override HallStyle Style => HallStyle.Temple;

    /// <summary>How long one bell of the shown order stays lit.</summary>
    public const float Beat = 0.85f;

    private static readonly float[] Pitch = { 1f, 1.122f, 1.26f, 1.335f, 1.498f, 1.682f };

    // ------------------------------------------------------------- data

    public override void Generate(RoomPlan room, Rng rng, int d)
    {
        var l = new Data();
        room.Data = l;
        int bells = 4 + Math.Min(d, 2);
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
        int length = 3 + Math.Min(d, 3);
        l.Sequence = new int[length];
        for (int i = 0; i < length; i++)
        {
            int b;
            do b = rng.RangeInt(0, bells); while (i > 0 && b == l.Sequence[i - 1]);
            l.Sequence[i] = b;
        }
        room.Spots.AddRange(l.Bells);
        room.Spots.Add(Vector2.Zero);   // the echo bell
        if (d >= 1) PuzzleGen.AddSpikes(room, rng, c => RoomPlan.Local(c).Length() < 2.5f || PuzzleGen.NearAny(RoomPlan.Local(c), l.Bells, 2.2f));
    }

    public override void InitState(PuzzleState st, RoomPlan room) => st.V[room.Slot + PuzzleState.RB] = -1;

    public override void OnEnter(PuzzleState st, RoomPlan room, int r, float now) => st.SetR(room, PuzzleState.RB, PuzzleState.Tenths(now + 0.8f));

    /// <summary>Is the order being shown at <paramref name="now"/>, and which bell is lit?</summary>
    public static bool Showing(PuzzleState st, RoomPlan room, float now, out int bell)
    {
        bell = -1;
        Data l = room.Layout<Data>();
        int from = st.Get(room, PuzzleState.RB);
        if (from < 0) return false;
        float t = now - from / 10f;
        if (t < 0f) return true;   // about to start: hold still
        int step = (int)(t / Beat);
        if (step >= l.Sequence.Length) return false;
        if (t - step * Beat < Beat * 0.75f) bell = l.Sequence[step];
        return true;
    }

    /// <summary>Ring the order again from the start (the echo bell, a hint, a mistake).</summary>
    private static void ShowOrder(PuzzleState st, RoomPlan room, float at)
    {
        st.SetR(room, PuzzleState.RA, 0);
        st.SetR(room, PuzzleState.RB, PuzzleState.Tenths(at));
        st.SetR(room, PuzzleState.RC, st.Get(room, PuzzleState.RC) + 1);
    }

    public override bool Act(PuzzleState st, RoomPlan room, int r, PuzzleAction action, int a, int b, float now)
    {
        Data l = room.Layout<Data>();
        if (action == PuzzleAction.Echo)
        {
            ShowOrder(st, room, now + 0.4f);
            return true;
        }
        if (action != PuzzleAction.Bell) return false;
        if (a < 0 || a >= l.Bells.Length || Showing(st, room, now, out _)) return true;
        int progress = st.Get(room, PuzzleState.RA);
        if (l.Sequence[progress] == a)
        {
            st.SetR(room, PuzzleState.RA, progress + 1);
            if (progress + 1 >= l.Sequence.Length) st.MarkSolved(r, now);
            return true;
        }
        // Wrong: start over, and the order rings again.
        st.SetR(room, PuzzleState.RA, 0);
        st.SetR(room, PuzzleState.RFails, st.Get(room, PuzzleState.RFails) + 1);
        ShowOrder(st, room, now + 0.9f);
        return true;
    }

    public override void OnHint(PuzzleState st, RoomPlan room, int r, float now) => ShowOrder(st, room, now + 0.3f);

    public override async Task<string?> Solve(IPuzzleDriver d, RoomPlan room, int r)
    {
        Data l = room.Layout<Data>();
        if (l.Sequence.Length < 3 || l.Symbols.Distinct().Count() != l.Bells.Length) return "bells not told apart";
        await d.Stand(room.Centre + new Vector3(2f, 0f, 2f));
        await d.Wait(1.2f + l.Sequence.Length * Beat);
        // A wrong bell must start it over.
        int fails = d.State.Get(room, PuzzleState.RFails);
        await d.Ask(PuzzleAction.Bell, (l.Sequence[0] + 1) % l.Bells.Length);
        if (d.State.Get(room, PuzzleState.RFails) != fails + 1) return "a wrong bell was not wrong";
        await d.Wait(1.2f + l.Sequence.Length * Beat);
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
            kit.Post(at, 0.55f, 2.2f);
        }
        // The echo bell in the middle: rings the order again.
        Vector3 c = room.Centre;
        kit.Put(PieceKit.Mesh(new CylinderMesh { TopRadius = 0.5f, BottomRadius = 0.65f, Height = 0.8f, RadialSegments = 12 }, c + Vector3.Up * 0.4f, PieceKit.Flat(PieceKit.WallCap)));
        kit.Put(PieceKit.Mesh(new CylinderMesh { TopRadius = 0.25f, BottomRadius = 0.55f, Height = 0.6f, RadialSegments = 14 }, c + Vector3.Up * 1.15f, PieceKit.Flat(PieceKit.BronzeDark)));
        for (int k = 0; k < 3; k++)
        {
            var wave = PieceKit.Mesh(new TorusMesh { InnerRadius = 0.3f + k * 0.22f, OuterRadius = 0.36f + k * 0.22f, Rings = 20, RingSegments = 4 }, c + Vector3.Up * 1.9f, PieceKit.Glow(PieceKit.Teal, 1.2f), false);
            wave.Rotation = new Vector3(Mathf.Pi * 0.5f, 0f, 0f);
            kit.Put(wave);
        }
        kit.Post(c, 0.65f, 1.6f);
        return v;
    }

    public override void Sync(PieceKit kit, object view, RoomPlan room, PuzzleState st, float t, float dt, bool changed)
    {
        var v = (View)view;
        Data l = room.Layout<Data>();
        bool showing = Showing(st, room, t, out int lit);
        int step = -1;
        int from = st.Get(room, PuzzleState.RB);
        if (showing && from >= 0) step = Mathf.FloorToInt((t - from / 10f) / Beat);
        if (step != v.ShowStep)
        {
            v.ShowStep = step;
            if (lit >= 0) AudioDirector.PlayPuzzle(PuzzleSound.Bell, Pitch[lit % Pitch.Length]);
        }
        for (int i = 0; i < l.Bells.Length; i++)
        {
            bool on = i == lit || i == v.Struck && v.StruckFor > 0f;
            StandardMaterial3D m = v.Mats[i];
            m.EmissionEnabled = on;
            m.Emission = PieceKit.Warm;
            m.EmissionEnergyMultiplier = 2.6f;
            v.Bells[i].Rotation = new Vector3(0f, 0f, on ? Mathf.Sin(t * 22f) * 0.25f : 0f);
            v.Symbols[i].RotateY(dt * (on ? 3f : 0.6f));
            v.Symbols[i].Scale = Vector3.One * (on ? 1.35f : 1f);
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
        offer(PuzzleAction.Echo, 0, room.Centre, VerbEcho);
    }

    private static int Next(PuzzleState st, RoomPlan room)
    {
        Data l = room.Layout<Data>();
        return l.Sequence[Mathf.Clamp(st.Get(room, PuzzleState.RA), 0, l.Sequence.Length - 1)];
    }

    public override PuzzleHint? HintAt(PuzzleState st, RoomPlan room, Vector3? me, float now)
        => new PuzzleHint(room.World(room.Layout<Data>().Bells[Next(st, room)]), "!");

    public override string Status(PuzzleMap map, RoomPlan room)
        => Showing(map.State, room, map.Elapsed, out _) ? Meta.Loc.T(Listen)
            : Meta.Loc.F(Progress, map.State.Get(room, PuzzleState.RA), room.Layout<Data>().Sequence.Length);

    public override Vector3 Bot(PuzzleBot bot, RoomPlan room, Vector3 me)
    {
        if (Showing(bot.State, room, bot.Map.Elapsed, out _)) return PuzzleBot.Toward(me, room.Centre + new Vector3(1.6f, 0f, 1.6f), 0.5f);
        return BellThink(bot, room, me, Next(bot.State, room));
    }

    /// <summary>Bells stand in a ring round the echo bell: go round outside
    /// the ring to the bell's spoke, then step in until it is the focus.</summary>
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
