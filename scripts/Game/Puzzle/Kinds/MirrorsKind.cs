using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Wavebreak.Audio;
using Wavebreak.Core;

namespace Wavebreak.Game.Puzzle.Kinds;

/// <summary>
/// Mirror Room: a light beam from an emitter on the grid's edge, turned by
/// mirrors ("/" or "\", interact flips one) onto a crystal. Decoy mirrors and
/// pillars stand off the true path; the start always needs two turns or more.
///
/// State: RA orientation bits (one per mirror). Solver: every on/off
/// combination (at most 2^7).
/// </summary>
public sealed class MirrorsKind : PuzzleKind
{
    public sealed class Data
    {
        public int Emitter = -1, EmitterDir, Receiver = -1;
        public int[] Mirrors = Array.Empty<int>();
        /// <summary>Starting orientation per mirror: 0 is "/", 1 is "\".</summary>
        public int[] Start = Array.Empty<int>();
    }

    private sealed class View
    {
        public readonly List<Node3D> Mirrors = new();
        public Node3D Beam = null!;
        public int BeamBits = -1;
        public Node3D Receiver = null!;
        public StandardMaterial3D ReceiverMat = null!;
        public bool Lit;
    }

    private const string Verb = "Turn the mirror";
    private const string Line = "Stand by a mirror to turn it.";

    public override string Id => "mirrors";
    public override string Title => "Mirror Room";
    public override string Objective => "Turn the mirrors until the beam reaches the crystal.";
    public override string Hint => "Hint: the marked mirror is turned the wrong way.";
    public override IEnumerable<string> Strings => new[] { Verb, Line };
    public override HallStyle Style => HallStyle.Observatory;

    private static readonly Color MirrorTint = new(0.86f, 0.94f, 1f);
    private static readonly Color BeamColour = new(1f, 0.92f, 0.55f);

    // ------------------------------------------------------------- data

    public override void Generate(RoomPlan room, Rng rng, int d)
    {
        var l = new Data();
        room.Data = l;
        int onPath = 2 + Math.Min(d, 2);
        int decoys = 1 + Math.Min(d, 2);
        for (int attempt = 0; attempt < 400; attempt++)
        {
            if (attempt == 250) onPath = 2;
            PuzzleGen.Clear(room);
            l.Mirrors = Array.Empty<int>();
            l.Emitter = l.Receiver = -1;
            if (TryPath(room, l, rng, onPath, decoys, d)) return;
        }
        // Fallback: emitter on the left edge, one mirror turning the beam up to a crystal.
        PuzzleGen.Clear(room);
        int row = room.EntrySide == 2 || room.ExitSide == 2 ? 2 : RoomPlan.Mid;
        l.Emitter = RoomPlan.CellOf(0, row);
        l.EmitterDir = 0;
        int mirror = RoomPlan.CellOf(row == 2 ? 6 : 3, row);
        l.Mirrors = new[] { mirror };
        l.Receiver = RoomPlan.CellOf(RoomPlan.X(mirror), 0);
        l.Start = new[] { 1 };
        MarkFixed(room, l);
    }

    private static void MarkFixed(RoomPlan room, Data l)
    {
        room.Fixed[l.Emitter] = room.Fixed[l.Receiver] = true;
        foreach (int m in l.Mirrors) room.Fixed[m] = true;
    }

    private static bool TryPath(RoomPlan room, Data l, Rng rng, int onPath, int decoys, int d)
    {
        int wall = rng.RangeInt(0, 4);
        int along = rng.RangeInt(1, RoomPlan.N - 1);
        int emitter = wall switch
        {
            0 => RoomPlan.CellOf(RoomPlan.N - 1, along),
            1 => RoomPlan.CellOf(along, RoomPlan.N - 1),
            2 => RoomPlan.CellOf(0, along),
            _ => RoomPlan.CellOf(along, 0),
        };
        if (room.NearDoor(emitter)) return false;
        int dir = (wall + 2) % 4;
        var path = new HashSet<int> { emitter };
        var mirrors = new List<int>();
        int cur = emitter;
        for (int k = 0; k < onPath; k++)
        {
            int steps = rng.RangeInt(1, k == 0 ? 4 : 5);
            for (int t = 0; t < steps; t++)
            {
                cur = PuzzleSolver.Step(cur, dir);
                if (cur < 0 || !PuzzleGen.Inner(cur) || path.Contains(cur) || room.NearDoor(cur)) return false;
                path.Add(cur);
            }
            int turn = rng.Chance(0.5f) ? (dir + 1) % 4 : (dir + 3) % 4;
            mirrors.Add(cur);
            dir = turn;
        }
        while (true)
        {
            cur = PuzzleSolver.Step(cur, dir);
            if (cur < 0 || path.Contains(cur) || room.NearDoor(cur)) return false;
            path.Add(cur);
            if (PuzzleGen.Edge(cur)) break;
        }
        int receiver = cur;
        if (receiver == emitter) return false;

        l.Emitter = emitter;
        l.EmitterDir = (wall + 2) % 4;
        l.Receiver = receiver;

        // Pillars and decoy mirrors, off the beam's true path.
        var spare = PuzzleGen.FreeInner(room).Where(c => !path.Contains(c)).ToList();
        for (int i = 0; i < 1 + rng.RangeInt(0, d + 1) && spare.Count > 0; i++) room.Pillar[PuzzleGen.Take(rng, spare)] = true;
        var cellsAll = new List<int>(mirrors);
        for (int i = 0; i < decoys && spare.Count > 0; i++) cellsAll.Add(PuzzleGen.Take(rng, spare));
        l.Mirrors = cellsAll.ToArray();

        int start = 0;
        for (int i = 0; i < cellsAll.Count; i++) if (rng.Chance(0.5f)) start |= 1 << i;
        if (Beam(room, start).Hit) start ^= 1;
        // At least two turns to make, so a room is never one press from done.
        int answer = SolveBits(room, start);
        if (answer < 0) return false;
        if (PuzzleSolver.PopCount(answer ^ start) < 2)
        {
            for (int i = 0; i < mirrors.Count; i++)
                if (((answer ^ start) >> i & 1) == 0) { start ^= 1 << i; break; }
            answer = SolveBits(room, start);
            if (answer < 0 || Beam(room, start).Hit) return false;
        }
        l.Start = new int[cellsAll.Count];
        for (int i = 0; i < cellsAll.Count; i++) l.Start[i] = (start >> i) & 1;

        MarkFixed(room, l);
        if (!PuzzleSolver.DoorsConnected(room)) return false;
        // Every mirror must have a floor cell beside it to stand on and turn it.
        foreach (int m in l.Mirrors) if (!PuzzleSolver.Reachable(room, m)) return false;
        return true;
    }

    public static int StartBits(RoomPlan room)
    {
        Data l = room.Layout<Data>();
        int bits = 0;
        for (int i = 0; i < l.Start.Length; i++) bits |= l.Start[i] << i;
        return bits;
    }

    public override void InitState(PuzzleState st, RoomPlan room) => st.V[room.Slot + PuzzleState.RA] = StartBits(room);

    public override bool Act(PuzzleState st, RoomPlan room, int r, PuzzleAction action, int a, int b, float now)
    {
        if (action != PuzzleAction.Mirror) return false;
        if (a < 0 || a >= room.Layout<Data>().Mirrors.Length) return true;
        int bits = st.Get(room, PuzzleState.RA) ^ (1 << a);
        st.SetR(room, PuzzleState.RA, bits);
        if (Beam(room, bits).Hit) st.MarkSolved(r, now);
        return true;
    }

    // ----------------------------------------------------------- solver

    /// <summary>Where a beam leaving <paramref name="side"/> goes off a mirror of
    /// orientation <paramref name="o"/> (0 "/", 1 "\").</summary>
    public static int Reflect(int side, int o)
    {
        int dx = RoomPlan.Dx[side], dz = RoomPlan.Dz[side];
        int nx = o == 0 ? -dz : dz, nz = o == 0 ? -dx : dx;
        for (int s = 0; s < 4; s++) if (RoomPlan.Dx[s] == nx && RoomPlan.Dz[s] == nz) return s;
        return side;
    }

    /// <summary>The beam's cells from the emitter, whether it lands on the
    /// crystal, and the side it was last travelling.</summary>
    public static (List<int> Cells, bool Hit, int Side) Beam(RoomPlan room, int orient)
    {
        Data l = room.Layout<Data>();
        var cells = new List<int> { l.Emitter };
        int cur = l.Emitter, side = l.EmitterDir;
        for (int guard = 0; guard < RoomPlan.N * RoomPlan.N * 4; guard++)
        {
            int next = PuzzleSolver.Step(cur, side);
            if (next < 0) return (cells, false, side);
            cells.Add(next);
            if (next == l.Receiver) return (cells, true, side);
            if (room.Pillar[next]) return (cells, false, side);
            int m = Array.IndexOf(l.Mirrors, next);
            if (m >= 0) side = Reflect(side, (orient >> m) & 1);
            cur = next;
        }
        return (cells, false, side);
    }

    /// <summary>The orientation bits that light the crystal with the fewest
    /// turns from <paramref name="current"/>; -1 when nothing does.</summary>
    public static int SolveBits(RoomPlan room, int current)
    {
        int m = room.Layout<Data>().Mirrors.Length, best = -1, bestFlips = int.MaxValue;
        for (int bits = 0; bits < 1 << m; bits++)
        {
            if (!Beam(room, bits).Hit) continue;
            int flips = PuzzleSolver.PopCount(bits ^ current);
            if (flips < bestFlips) { best = bits; bestFlips = flips; }
        }
        return best;
    }

    /// <summary>The first mirror the answer from the current state turns, or -1.</summary>
    private static int NextMirror(PuzzleState st, RoomPlan room)
    {
        int bits = st.Get(room, PuzzleState.RA);
        int answer = SolveBits(room, bits);
        for (int i = 0; i < room.Layout<Data>().Mirrors.Length; i++)
            if (answer >= 0 && ((answer ^ bits) >> i & 1) != 0) return i;
        return -1;
    }

    public override async Task<string?> Solve(IPuzzleDriver d, RoomPlan room, int r)
    {
        int start = d.State.Get(room, PuzzleState.RA);
        if (Beam(room, start).Hit) return "beam lit at the start";
        int answer = SolveBits(room, start);
        if (answer < 0) return "no mirror solution";
        for (int i = 0; i < room.Layout<Data>().Mirrors.Length; i++)
            if (((answer ^ start) >> i & 1) != 0) await d.Ask(PuzzleAction.Mirror, i);
        return null;
    }

    // ------------------------------------------------------------ scene

    /// <summary>Y rotation of a mirror plate: "/" is +45 degrees, "\" is -45.</summary>
    private static float Angle(int o) => o == 0 ? Mathf.Pi * 0.25f : -Mathf.Pi * 0.25f;

    public override object Build(PieceKit kit, RoomPlan room)
    {
        Data l = room.Layout<Data>();
        var v = new View();
        // Emitter: a lamp housing on the edge, its lens facing in.
        Vector3 e = room.World(l.Emitter);
        var inward = new Vector3(RoomPlan.Dx[l.EmitterDir], 0f, RoomPlan.Dz[l.EmitterDir]);
        kit.Put(PieceKit.Mesh(new BoxMesh { Size = new Vector3(1.3f, 1.6f, 1.3f) }, e + Vector3.Up * 0.8f, PieceKit.Flat(PieceKit.WallCap)));
        var lens = PieceKit.Mesh(new CylinderMesh { TopRadius = 0.32f, BottomRadius = 0.32f, Height = 0.3f, RadialSegments = 16 }, e + Vector3.Up * 1.15f + inward * 0.7f, PieceKit.Glow(BeamColour, 3f), false);
        lens.Rotation = l.EmitterDir % 2 == 0 ? new Vector3(0f, 0f, Mathf.Pi * 0.5f) : new Vector3(Mathf.Pi * 0.5f, 0f, 0f);
        kit.Put(lens);
        kit.Solid(new BoxShape3D { Size = new Vector3(1.3f, 1.6f, 1.3f) }, e + Vector3.Up * 0.8f);

        // Crystal: a hexagonal prism on a plinth; rises and spins when lit.
        Vector3 rc = room.World(l.Receiver);
        kit.Put(PieceKit.Mesh(new BoxMesh { Size = new Vector3(1.3f, 0.7f, 1.3f) }, rc + Vector3.Up * 0.35f, PieceKit.Flat(PieceKit.WallCap)));
        v.ReceiverMat = PieceKit.Flat(PieceKit.Teal.Darkened(0.35f));
        v.Receiver = new Node3D { Position = rc + Vector3.Up * 1.2f };
        kit.Put(v.Receiver);
        v.Receiver.AddChild(PieceKit.Mesh(new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.42f, Height = 0.9f, RadialSegments = 6 }, Vector3.Up * 0.2f, v.ReceiverMat));
        v.Receiver.AddChild(PieceKit.Mesh(new CylinderMesh { TopRadius = 0.42f, BottomRadius = 0.05f, Height = 0.5f, RadialSegments = 6 }, Vector3.Down * 0.5f, v.ReceiverMat));
        kit.Solid(new BoxShape3D { Size = new Vector3(1.3f, 1.6f, 1.3f) }, rc + Vector3.Up * 0.8f);

        for (int i = 0; i < l.Mirrors.Length; i++)
        {
            Vector3 at = room.World(l.Mirrors[i]);
            kit.Put(PieceKit.Mesh(new CylinderMesh { TopRadius = 0.5f, BottomRadius = 0.6f, Height = 0.7f, RadialSegments = 12 }, at + Vector3.Up * 0.35f, PieceKit.Flat(PieceKit.WallCap)));
            var turn = new Node3D { Position = at + Vector3.Up * 1.15f, Rotation = new Vector3(0f, Angle(l.Start[i]), 0f) };
            kit.Put(turn);
            turn.AddChild(PieceKit.Mesh(new BoxMesh { Size = new Vector3(1.6f, 1.0f, 0.12f) }, Vector3.Zero, new StandardMaterial3D
            {
                AlbedoColor = MirrorTint, Metallic = 0.35f, Roughness = 0.1f,
                EmissionEnabled = true, Emission = MirrorTint, EmissionEnergyMultiplier = 0.35f,
            }));
            turn.AddChild(PieceKit.Mesh(new BoxMesh { Size = new Vector3(1.72f, 0.1f, 0.2f) }, Vector3.Up * 0.55f, PieceKit.Flat(PieceKit.Bronze)));
            turn.AddChild(PieceKit.Mesh(new BoxMesh { Size = new Vector3(1.72f, 0.1f, 0.2f) }, Vector3.Down * 0.55f, PieceKit.Flat(PieceKit.Bronze)));
            v.Mirrors.Add(turn);
            kit.Post(at, 0.62f, 1.8f);
        }
        v.Beam = new Node3D { Name = "Beam" };
        kit.Put(v.Beam);
        return v;
    }

    private static void RebuildBeam(RoomPlan room, View v, int bits)
    {
        foreach (Node n in v.Beam.GetChildren()) n.QueueFree();
        (List<int> cells, bool hit, int side) = Beam(room, bits);
        var mat = PieceKit.Glow(BeamColour, 4f);
        Vector3 Point(int i) => room.World(cells[i]) + Vector3.Up * 1.15f;
        for (int i = 0; i + 1 < cells.Count; i++)
        {
            Vector3 a = Point(i), b = Point(i + 1);
            if (i + 1 == cells.Count - 1 && !hit && room.Pillar[cells[i + 1]]) b = a.Lerp(b, 0.55f);
            PieceKit.Segment(v.Beam, a, b, mat);
        }
        // Off the edge of the floor: run the beam on a metre.
        if (!hit && cells.Count > 0 && PuzzleSolver.Step(cells[^1], side) < 0)
            PieceKit.Segment(v.Beam, Point(cells.Count - 1), Point(cells.Count - 1) + new Vector3(RoomPlan.Dx[side], 0f, RoomPlan.Dz[side]), mat);
    }

    public override void Sync(PieceKit kit, object view, RoomPlan room, PuzzleState st, float t, float dt, bool changed)
    {
        var v = (View)view;
        int bits = st.Get(room, PuzzleState.RA);
        for (int i = 0; i < v.Mirrors.Count; i++)
        {
            float target = Angle((bits >> i) & 1);
            Vector3 rot = v.Mirrors[i].Rotation;
            v.Mirrors[i].Rotation = new Vector3(0f, Mathf.MoveToward(rot.Y, target, dt * 6f), 0f);
        }
        if (bits != v.BeamBits && v.Beam.IsInsideTree())
        {
            v.BeamBits = bits;
            RebuildBeam(room, v, bits);
            v.Lit = Beam(room, bits).Hit;
            PieceKit.Lit(v.ReceiverMat, v.Lit, PieceKit.Teal, PieceKit.Teal.Darkened(0.35f), 2.5f);
        }
        Vector3 home = room.World(room.Layout<Data>().Receiver) + Vector3.Up * (v.Lit ? 1.65f : 1.2f);
        v.Receiver.Position = v.Receiver.Position.MoveToward(home, dt * 2f);
        if (v.Lit) v.Receiver.RotateY(dt * 2.2f);
    }

    public override void Sounds(object view, RoomPlan room, Func<int, int> was, Func<int, int> now)
    {
        if (was(PuzzleState.RA) != now(PuzzleState.RA)) AudioDirector.PlayPuzzle(PuzzleSound.Click, 1.2f);
    }

    public override void Offer(PuzzleMap map, RoomPlan room, FocusOffer offer)
    {
        int[] mirrors = room.Layout<Data>().Mirrors;
        for (int i = 0; i < mirrors.Length; i++) offer(PuzzleAction.Mirror, i, room.World(mirrors[i]), Verb);
    }

    public override PuzzleHint? HintAt(PuzzleState st, RoomPlan room, Vector3? me, float now)
    {
        int i = NextMirror(st, room);
        return i < 0 ? null : new PuzzleHint(room.World(room.Layout<Data>().Mirrors[i]), "?");
    }

    public override string Status(PuzzleMap map, RoomPlan room) => Meta.Loc.T(Line);

    public override Vector3 Bot(PuzzleBot bot, RoomPlan room, Vector3 me)
    {
        int i = NextMirror(bot.State, room);
        return i < 0 ? Vector3.Zero : bot.Use(room, me, room.World(room.Layout<Data>().Mirrors[i]), PuzzleAction.Mirror, i);
    }

    public override void Glyph(CanvasItem ci, Vector2 at, float size, Color c)
    {
        float h = size * 0.6f, w = Mathf.Max(1f, size * 0.18f);
        ci.DrawLine(at + new Vector2(-h, h), at + new Vector2(h, -h), c, w);
        ci.DrawLine(at + new Vector2(-h, -h * 0.2f), at + new Vector2(0f, -h * 0.2f), c, w * 0.6f);
    }
}
