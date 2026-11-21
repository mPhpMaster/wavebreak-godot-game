using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Wavebreak.Audio;
using Wavebreak.Core;

namespace Wavebreak.Game.Puzzle.Kinds;

/// <summary>
/// Mirror Room: light beams from lamps on the grid's edge, turned by mirrors
/// ("/" or "\", interact flips one) onto crystals. Every crystal must be lit
/// at once, each by the beam of its own lamp's colour AND shape (a disc lamp
/// lights disc crystals, a diamond lamp diamond ones; a crystal ignores the
/// other beam, and either beam stops at it).
///
/// Difficulty: d0 one lamp, one crystal, a three-mirror path and decoys; d1 a
/// longer path; d2 a glass splitter on the path (light goes straight through
/// it AND turns) feeding a second crystal; d3 two lamps (disc and diamond)
/// whose beams cross, and linked mirrors (the same rune on the floor beside
/// them and a line between: turning one turns its partner too); d4 and d5
/// three and four crystals, more decoys, pillars, links and splitters. The
/// fewest turns from the start climbs from 2 to 5.
///
/// State: RA orientation bits (one per mirror). Solver: every combination of
/// the link groups (at most 2^12), the fewest turns; a room is kept only when
/// its start is unlit and the fewest turns reach the difficulty's count.
/// </summary>
public sealed class MirrorsKind : PuzzleKind
{
    public sealed class Data
    {
        public int[] Emitters = Array.Empty<int>(), EmitterDirs = Array.Empty<int>();
        /// <summary>Per crystal: its cell and its colour (the lamp index).</summary>
        public int[] Receivers = Array.Empty<int>(), ReceiverColour = Array.Empty<int>();
        public int[] Mirrors = Array.Empty<int>();
        public bool[] Splitter = Array.Empty<bool>();
        /// <summary>Per mirror: its link group (mirrors of a group turn together).</summary>
        public int[] Group = Array.Empty<int>();
        public int Groups;
        /// <summary>Starting orientation per mirror: 0 is "/", 1 is "\".</summary>
        public int[] Start = Array.Empty<int>();
        public readonly Dictionary<int, int> Cache = new();
        /// <summary>Per cell: the mirror there, the crystal there (or -1), a lamp there.</summary>
        public int[]? MirrorAt, CrystalAt;
        public bool[]? LampAt;

        public void Index()
        {
            int n = RoomPlan.N * RoomPlan.N;
            MirrorAt = new int[n];
            CrystalAt = new int[n];
            LampAt = new bool[n];
            Array.Fill(MirrorAt, -1);
            Array.Fill(CrystalAt, -1);
            for (int i = 0; i < Mirrors.Length; i++) MirrorAt[Mirrors[i]] = i;
            for (int i = 0; i < Receivers.Length; i++) CrystalAt[Receivers[i]] = i;
            foreach (int e in Emitters) LampAt[e] = true;
            Cache.Clear();
        }
    }

    private sealed class View
    {
        public readonly List<Node3D> Mirrors = new();
        public Node3D Beam = null!;
        public int BeamBits = -1;
        public readonly List<Node3D> Receivers = new();
        public readonly List<StandardMaterial3D> ReceiverMats = new();
        public int LitBits;
    }

    private const string Verb = "Turn the mirror";
    private const string Line = "Crystals lit: {0}/{1}";

    public override string Id => "mirrors";
    public override string Title => "Mirror Room";
    public override string Objective => "Turn the mirrors so every crystal is lit by the beam of its own lamp (same shape). Glass splitters send light on and aside; mirrors sharing a rune turn together.";
    public override string Hint => "Hint: the marked mirror is turned the wrong way.";
    public override IEnumerable<string> Strings => new[] { Verb, Line };
    public override HallStyle Style => HallStyle.Observatory;
    public override float ParSeconds => 42f;

    private static readonly Color MirrorTint = new(0.86f, 0.94f, 1f);
    private static readonly Color[] BeamColours = { new(1f, 0.86f, 0.42f), new(0.45f, 0.72f, 1f) };
    private static readonly RuneSymbol[] LampShape = { RuneSymbol.Circle, RuneSymbol.Diamond };
    private static readonly RuneSymbol[] LinkShape = { RuneSymbol.Triangle, RuneSymbol.Square, RuneSymbol.Star, RuneSymbol.Cross };

    // Per difficulty: crystals per lamp, turns on each lamp's main path, decoys, links, fewest turns.
    private static readonly int[][] Crystals = { new[] { 1 }, new[] { 1 }, new[] { 2 }, new[] { 1, 1 }, new[] { 2, 1 }, new[] { 2, 2 } };
    private static readonly int[] Turns = { 3, 3, 3, 3, 3, 3 };
    private static readonly int[] Decoys = { 2, 3, 3, 4, 4, 4 };
    private static readonly int[] LinkCount = { 0, 0, 0, 1, 2, 3 };
    private static readonly int[] MinFlips = { 2, 3, 3, 4, 5, 5 };

    // ------------------------------------------------------------- data

    public override void Generate(RoomPlan room, Rng rng, int d)
    {
        d = Math.Clamp(d, 0, 5);
        var l = new Data();
        room.Data = l;
        for (int attempt = 0; attempt < 500; attempt++)
        {
            PuzzleGen.Clear(room);
            int need = attempt < 350 ? MinFlips[d] : Math.Max(2, MinFlips[d] - 2);
            int dd = attempt < 420 ? d : Math.Max(0, d - 2);
            if (TryBuild(room, l, rng, dd, need)) return;
        }
        // Fallback: one lamp on the left edge, one mirror turning the beam up to a crystal.
        PuzzleGen.Clear(room);
        int row = room.EntrySide == 2 || room.ExitSide == 2 ? 2 : RoomPlan.Mid;
        l.Emitters = new[] { RoomPlan.CellOf(0, row) };
        l.EmitterDirs = new[] { 0 };
        int mirror = RoomPlan.CellOf(row == 2 ? 6 : 3, row);
        l.Mirrors = new[] { mirror };
        l.Splitter = new[] { false };
        l.Group = new[] { 0 };
        l.Groups = 1;
        l.Receivers = new[] { RoomPlan.CellOf(RoomPlan.X(mirror), 0) };
        l.ReceiverColour = new[] { 0 };
        l.Start = new[] { 1 };
        l.Index();
        MarkFixed(room, l);
    }

    private static void MarkFixed(RoomPlan room, Data l)
    {
        foreach (int e in l.Emitters) room.Fixed[e] = true;
        foreach (int c in l.Receivers) room.Fixed[c] = true;
        foreach (int m in l.Mirrors) room.Fixed[m] = true;
    }

    /// <summary>Lay the beams' true paths, then decoys, pillars and links.</summary>
    private static bool TryBuild(RoomPlan room, Data l, Rng rng, int d, int need)
    {
        var piece = new HashSet<int>();
        // Beam cells and the axis each runs along (bit 0 across x, bit 1 along z): a crossing is fine, a shared run is not.
        var axis = new Dictionary<int, int>();
        var emitters = new List<int>();
        var dirs = new List<int>();
        var receivers = new List<int>();
        var colours = new List<int>();
        var mirrors = new List<int>();
        var orient = new List<int>();
        var splitter = new List<bool>();
        var arrive = new List<int>();

        bool Free(int c) => c >= 0 && !piece.Contains(c) && !room.NearDoor(c);
        void Mark(int c, int side) => axis[c] = (axis.TryGetValue(c, out int a) ? a : 0) | (side % 2 == 0 ? 1 : 2);
        bool CanRun(int c, int side) => Free(c) && (!axis.TryGetValue(c, out int a) || (a & (side % 2 == 0 ? 1 : 2)) == 0);

        // Walk from cur along side with this many turns; mirrors at the turns, a crystal on the edge at the end.
        bool Walk(int cur, int side, int turns, int colour)
        {
            var path = new List<(int Cell, int Side)>();
            var newMirrors = new List<(int Cell, int O, int In)>();
            for (int k = 0; k < turns; k++)
            {
                int steps = rng.RangeInt(1, 4);
                for (int t = 0; t < steps; t++)
                {
                    cur = PuzzleSolver.Step(cur, side);
                    if (cur < 0 || !PuzzleGen.Inner(cur) || !CanRun(cur, side) || path.Any(p => p.Cell == cur)) return false;
                    path.Add((cur, side));
                }
                // The turn cell holds the mirror: no beam may already run there.
                if (axis.ContainsKey(cur)) return false;
                int turn = rng.Chance(0.5f) ? (side + 1) % 4 : (side + 3) % 4;
                int o = Reflect(side, 0) == turn ? 0 : 1;
                newMirrors.Add((cur, o, side));
                side = turn;
            }
            while (true)
            {
                cur = PuzzleSolver.Step(cur, side);
                if (cur < 0 || path.Any(p => p.Cell == cur) || newMirrors.Any(m => m.Cell == cur)) return false;
                if (PuzzleGen.Edge(cur))
                {
                    if (!Free(cur) || axis.ContainsKey(cur)) return false;
                    break;
                }
                if (!CanRun(cur, side)) return false;
                path.Add((cur, side));
            }
            foreach (var (c, s) in path) Mark(c, s);
            foreach (var (c, o, inSide) in newMirrors)
            {
                piece.Add(c);
                mirrors.Add(c);
                orient.Add(o);
                splitter.Add(false);
                arrive.Add(inSide);
                Mark(c, 0);
                Mark(c, 1);
            }
            piece.Add(cur);
            receivers.Add(cur);
            colours.Add(colour);
            return true;
        }

        int[] perLamp = Crystals[d];
        for (int c = 0; c < perLamp.Length; c++)
        {
            bool placed = false;
            for (int tries = 0; tries < 30 && !placed; tries++)
            {
                int wall = rng.RangeInt(0, 4);
                int along = rng.RangeInt(1, RoomPlan.N - 1);
                int e = wall switch
                {
                    0 => RoomPlan.CellOf(RoomPlan.N - 1, along),
                    1 => RoomPlan.CellOf(along, RoomPlan.N - 1),
                    2 => RoomPlan.CellOf(0, along),
                    _ => RoomPlan.CellOf(along, 0),
                };
                if (!Free(e) || axis.ContainsKey(e)) continue;
                int dir = (wall + 2) % 4;
                int mirrorsBefore = mirrors.Count;
                if (!Walk(e, dir, Turns[d], c)) continue;
                piece.Add(e);
                emitters.Add(e);
                dirs.Add(dir);
                placed = true;
                // Extra crystals of this colour: a turn on the path becomes a splitter, and the
                // light it lets through runs on to a crystal of its own.
                for (int extra = 1; extra < perLamp[c]; extra++)
                {
                    bool branched = false;
                    for (int m = mirrorsBefore; m < mirrors.Count && !branched; m++)
                    {
                        if (splitter[m]) continue;
                        int cell = mirrors[m];
                        // Light arriving travelling inSide also goes straight on through a splitter.
                        int inSide = arrive[m];
                        int saveMirrors = mirrors.Count, saveRecv = receivers.Count;
                        var saveAxis = new Dictionary<int, int>(axis);
                        var savePiece = new HashSet<int>(piece);
                        splitter[m] = true;
                        if (Walk(cell, inSide, rng.RangeInt(1, 3), c)) branched = true;
                        else
                        {
                            splitter[m] = false;
                            mirrors.RemoveRange(saveMirrors, mirrors.Count - saveMirrors);
                            orient.RemoveRange(saveMirrors, orient.Count - saveMirrors);
                            splitter.RemoveRange(saveMirrors, splitter.Count - saveMirrors);
                            arrive.RemoveRange(saveMirrors, arrive.Count - saveMirrors);
                            receivers.RemoveRange(saveRecv, receivers.Count - saveRecv);
                            colours.RemoveRange(saveRecv, colours.Count - saveRecv);
                            axis.Clear();
                            foreach (var kv in saveAxis) axis[kv.Key] = kv.Value;
                            piece.Clear();
                            piece.UnionWith(savePiece);
                        }
                    }
                    if (!branched) return false;
                }
            }
            if (!placed) return false;
        }

        // Pillars and decoys off every beam; some decoys are splitters from difficulty 4.
        var spare = PuzzleGen.FreeInner(room).Where(c => !piece.Contains(c) && !axis.ContainsKey(c)).ToList();
        int pillars = 1 + rng.RangeInt(0, d / 2 + 1);
        for (int i = 0; i < pillars && spare.Count > 0; i++) room.Pillar[PuzzleGen.Take(rng, spare)] = true;
        for (int i = 0; i < Decoys[d] && spare.Count > 0; i++)
        {
            mirrors.Add(PuzzleGen.Take(rng, spare));
            orient.Add(rng.RangeInt(0, 2));
            splitter.Add(d >= 4 && i == 0);
        }
        if (mirrors.Count > 14) return false;

        l.Emitters = emitters.ToArray();
        l.EmitterDirs = dirs.ToArray();
        l.Receivers = receivers.ToArray();
        l.ReceiverColour = colours.ToArray();
        l.Mirrors = mirrors.ToArray();
        l.Splitter = splitter.ToArray();
        l.Index();

        // Links: pairs of mirrors that turn together.
        l.Group = new int[mirrors.Count];
        for (int i = 0; i < l.Group.Length; i++) l.Group[i] = -1;
        int groups = 0;
        var order = Enumerable.Range(0, mirrors.Count).ToList();
        PuzzleGen.Shuffle(rng, order);
        for (int i = 0; i + 1 < order.Count && groups < LinkCount[d]; i += 2)
        {
            l.Group[order[i]] = groups;
            l.Group[order[i + 1]] = groups;
            groups++;
        }
        for (int i = 0; i < l.Group.Length; i++) if (l.Group[i] < 0) l.Group[i] = groups++;
        l.Groups = groups;

        int answer = 0;
        for (int i = 0; i < orient.Count; i++) answer |= orient[i] << i;
        if (!AllLit(room, l, answer)) return false;
        // The start: the answer with some groups turned, unlit, and far enough from any answer.
        bool ok = false;
        for (int s = 0; s < 30 && !ok; s++)
        {
            int start = answer;
            for (int g = 0; g < groups; g++) if (rng.Chance(0.5f)) start ^= GroupMask(l, g);
            if (AllLit(room, l, start)) continue;
            l.Cache.Clear();
            int flips = FlipsFor(room, l, start, out _);
            if (flips < need) continue;
            l.Start = new int[mirrors.Count];
            for (int i = 0; i < mirrors.Count; i++) l.Start[i] = (start >> i) & 1;
            ok = true;
        }
        if (!ok) return false;

        MarkFixed(room, l);
        if (!PuzzleSolver.DoorsConnected(room)) return false;
        // Every mirror must have a floor cell beside it to stand on and turn it.
        foreach (int m in l.Mirrors) if (!PuzzleSolver.Reachable(room, m)) return false;
        return true;
    }

    public static int GroupMask(Data l, int g)
    {
        int m = 0;
        for (int i = 0; i < l.Group.Length; i++) if (l.Group[i] == g) m |= 1 << i;
        return m;
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
        Data l = room.Layout<Data>();
        if (a < 0 || a >= l.Mirrors.Length) return true;
        int bits = st.Get(room, PuzzleState.RA) ^ GroupMask(l, l.Group[a]);
        st.SetR(room, PuzzleState.RA, bits);
        if (AllLit(room, l, bits)) st.MarkSolved(r, now);
        return true;
    }

    // ----------------------------------------------------------- solver

    /// <summary>Where a beam travelling towards <paramref name="side"/> goes off
    /// a mirror of orientation <paramref name="o"/> (0 "/", 1 "\").</summary>
    public static int Reflect(int side, int o)
    {
        int dx = RoomPlan.Dx[side], dz = RoomPlan.Dz[side];
        int nx = o == 0 ? -dz : dz, nz = o == 0 ? -dx : dx;
        for (int s = 0; s < 4; s++) if (RoomPlan.Dx[s] == nx && RoomPlan.Dz[s] == nz) return s;
        return side;
    }

    /// <summary>One straight run of light: from a cell to the next (half a
    /// cell when it stops at a pillar), or off the floor's edge.</summary>
    public readonly record struct Ray(int From, int To, int Colour, bool Half, int OffSide);

    /// <summary>Trace every lamp's light. <c>Lit</c>: the crystals lit by their
    /// own colour (bits); <c>All</c>: every crystal lit.</summary>
    public static (List<Ray> Rays, int Lit, bool All) Beam(RoomPlan room, Data l, int orient)
    {
        var rays = new List<Ray>();
        int lit = 0;
        for (int e = 0; e < l.Emitters.Length; e++)
        {
            var seen = new HashSet<int>();
            var stack = new Stack<(int Cell, int Side)>();
            stack.Push((l.Emitters[e], l.EmitterDirs[e]));
            while (stack.Count > 0)
            {
                (int cur, int side) = stack.Pop();
                if (!seen.Add(cur * 4 + side)) continue;
                int next = PuzzleSolver.Step(cur, side);
                if (next < 0) { rays.Add(new Ray(cur, -1, e, false, side)); continue; }
                int rc = Array.IndexOf(l.Receivers, next);
                if (rc >= 0)
                {
                    rays.Add(new Ray(cur, next, e, false, -1));
                    if (l.ReceiverColour[rc] == e) lit |= 1 << rc;
                    continue;
                }
                if (room.Pillar[next] || Array.IndexOf(l.Emitters, next) >= 0) { rays.Add(new Ray(cur, next, e, true, -1)); continue; }
                rays.Add(new Ray(cur, next, e, false, -1));
                int m = Array.IndexOf(l.Mirrors, next);
                if (m < 0) { stack.Push((next, side)); continue; }
                stack.Push((next, Reflect(side, (orient >> m) & 1)));
                if (l.Splitter[m]) stack.Push((next, side));
            }
        }
        return (rays, lit, lit == (1 << l.Receivers.Length) - 1);
    }

    /// <summary>Crystals lit by their own colour (bits), without drawing anything.</summary>
    public static int LitBits(RoomPlan room, Data l, int orient)
    {
        if (l.MirrorAt is null) l.Index();
        int lit = 0;
        Span<bool> seen = stackalloc bool[RoomPlan.N * RoomPlan.N * 4];
        Span<int> stack = stackalloc int[RoomPlan.N * RoomPlan.N * 8 + 8];
        for (int e = 0; e < l.Emitters.Length; e++)
        {
            seen.Clear();
            int top = 0;
            stack[top++] = l.Emitters[e] * 4 + l.EmitterDirs[e];
            while (top > 0)
            {
                int item = stack[--top];
                if (seen[item]) continue;
                seen[item] = true;
                int cur = item >> 2, side = item & 3;
                int next = PuzzleSolver.Step(cur, side);
                if (next < 0) continue;
                int rc = l.CrystalAt![next];
                if (rc >= 0) { if (l.ReceiverColour[rc] == e) lit |= 1 << rc; continue; }
                if (room.Pillar[next] || l.LampAt![next]) continue;
                int m = l.MirrorAt![next];
                if (m < 0) { stack[top++] = next * 4 + side; continue; }
                stack[top++] = next * 4 + Reflect(side, (orient >> m) & 1);
                if (l.Splitter[m]) stack[top++] = next * 4 + side;
            }
        }
        return lit;
    }

    public static bool AllLit(RoomPlan room, Data l, int orient) => LitBits(room, l, orient) == (1 << l.Receivers.Length) - 1;

    private static readonly Dictionary<int, int[]> ByCount = new();

    /// <summary>Every subset of <paramref name="n"/> things, fewest first.</summary>
    private static int[] SetsByCount(int n)
    {
        lock (ByCount)
        {
            if (ByCount.TryGetValue(n, out int[]? sets)) return sets;
            sets = Enumerable.Range(0, 1 << n).OrderBy(PuzzleSolver.PopCount).ThenBy(x => x).ToArray();
            ByCount[n] = sets;
            return sets;
        }
    }

    /// <summary>The fewest group turns that light every crystal from
    /// <paramref name="current"/>; <paramref name="turn"/> gets which groups
    /// (bits). int.MaxValue when nothing does.</summary>
    public static int FlipsFor(RoomPlan room, Data l, int current, out int turn)
    {
        if (l.Cache.TryGetValue(current, out turn)) return turn < 0 ? int.MaxValue : PuzzleSolver.PopCount(turn);
        int best = -1;
        var masks = new int[l.Groups];
        for (int g = 0; g < l.Groups; g++) masks[g] = GroupMask(l, g);
        foreach (int set in SetsByCount(l.Groups))
        {
            int bits = current;
            for (int g = 0; g < l.Groups; g++) if ((set >> g & 1) != 0) bits ^= masks[g];
            if (AllLit(room, l, bits)) { best = set; break; }
        }
        if (l.Cache.Count > 1024) l.Cache.Clear();
        l.Cache[current] = best;
        turn = best;
        return best < 0 ? int.MaxValue : PuzzleSolver.PopCount(best);
    }

    /// <summary>A mirror of the first group the answer turns, or -1.</summary>
    private static int NextMirror(PuzzleState st, RoomPlan room, Vector3? me)
    {
        Data l = room.Layout<Data>();
        int bits = st.Get(room, PuzzleState.RA);
        if (FlipsFor(room, l, bits, out int turn) == int.MaxValue || turn <= 0) return -1;
        int pick = -1;
        float bestD = float.MaxValue;
        for (int i = 0; i < l.Mirrors.Length; i++)
        {
            if ((turn >> l.Group[i] & 1) == 0) continue;
            float dd = me is { } at ? room.World(l.Mirrors[i]).DistanceSquaredTo(at) : i;
            if (dd < bestD) { bestD = dd; pick = i; }
        }
        return pick;
    }

    public override async Task<string?> Solve(IPuzzleDriver d, RoomPlan room, int r)
    {
        Data l = room.Layout<Data>();
        int start = d.State.Get(room, PuzzleState.RA);
        if (AllLit(room, l, start)) return "beam lit at the start";
        if (FlipsFor(room, l, start, out int turn) == int.MaxValue) return "no mirror solution";
        for (int g = 0; g < l.Groups; g++)
        {
            if ((turn >> g & 1) == 0) continue;
            int i = Array.IndexOf(l.Group, g);
            await d.Ask(PuzzleAction.Mirror, i);
        }
        return null;
    }

    // ------------------------------------------------------------ scene

    /// <summary>Y rotation of a mirror plate: "/" is +45 degrees, "\" is -45.</summary>
    private static float Angle(int o) => o == 0 ? Mathf.Pi * 0.25f : -Mathf.Pi * 0.25f;

    private static void FloorSymbol(PieceKit kit, RuneSymbol s, Material mat, Vector3 at, float scale)
    {
        var n = PieceKit.Symbol(s, mat);
        n.Position = at + Vector3.Up * 0.08f;
        n.Rotation = new Vector3(-Mathf.Pi * 0.5f, 0f, 0f);
        n.Scale = Vector3.One * scale;
        kit.Put(n);
    }

    public override object Build(PieceKit kit, RoomPlan room)
    {
        Data l = room.Layout<Data>();
        var v = new View();
        for (int e = 0; e < l.Emitters.Length; e++)
        {
            // Lamp: a housing on the edge, its lens facing in, its shape on the floor before it.
            Vector3 at = room.World(l.Emitters[e]);
            var inward = new Vector3(RoomPlan.Dx[l.EmitterDirs[e]], 0f, RoomPlan.Dz[l.EmitterDirs[e]]);
            kit.Put(PieceKit.Mesh(new BoxMesh { Size = new Vector3(1.3f, 1.6f, 1.3f) }, at + Vector3.Up * 0.8f, PieceKit.Flat(PieceKit.WallCap)));
            var lens = PieceKit.Mesh(new CylinderMesh { TopRadius = 0.32f, BottomRadius = 0.32f, Height = 0.3f, RadialSegments = 16 }, at + Vector3.Up * 1.15f + inward * 0.7f, PieceKit.Glow(BeamColours[e], 3f), false);
            lens.Rotation = l.EmitterDirs[e] % 2 == 0 ? new Vector3(0f, 0f, Mathf.Pi * 0.5f) : new Vector3(Mathf.Pi * 0.5f, 0f, 0f);
            kit.Put(lens);
            kit.Solid(new BoxShape3D { Size = new Vector3(1.3f, 1.6f, 1.3f) }, at + Vector3.Up * 0.8f);
            var top = PieceKit.Symbol(LampShape[e], PieceKit.Glow(BeamColours[e], 1.5f));
            top.Position = at + Vector3.Up * 1.68f;
            top.Rotation = new Vector3(-Mathf.Pi * 0.5f, 0f, 0f);
            top.Scale = Vector3.One * 1.2f;
            kit.Put(top);
        }

        for (int c = 0; c < l.Receivers.Length; c++)
        {
            // Crystal: a prism on a plinth carrying its lamp's shape; rises and spins when lit.
            Vector3 rc = room.World(l.Receivers[c]);
            int colour = l.ReceiverColour[c];
            kit.Put(PieceKit.Mesh(new BoxMesh { Size = new Vector3(1.3f, 0.7f, 1.3f) }, rc + Vector3.Up * 0.35f, PieceKit.Flat(PieceKit.WallCap)));
            var mat = PieceKit.Flat(BeamColours[colour].Darkened(0.45f));
            var crystal = new Node3D { Position = rc + Vector3.Up * 1.2f };
            kit.Put(crystal);
            int sides = colour == 0 ? 8 : 4;
            crystal.AddChild(PieceKit.Mesh(new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.42f, Height = 0.9f, RadialSegments = sides }, Vector3.Up * 0.2f, mat));
            crystal.AddChild(PieceKit.Mesh(new CylinderMesh { TopRadius = 0.42f, BottomRadius = 0.05f, Height = 0.5f, RadialSegments = sides }, Vector3.Down * 0.5f, mat));
            kit.Solid(new BoxShape3D { Size = new Vector3(1.3f, 1.6f, 1.3f) }, rc + Vector3.Up * 0.8f);
            var mark = PieceKit.Symbol(LampShape[colour], PieceKit.Flat(BeamColours[colour].Darkened(0.2f)));
            mark.Position = rc + Vector3.Up * 0.72f;
            mark.Rotation = new Vector3(-Mathf.Pi * 0.5f, 0f, 0f);
            mark.Scale = Vector3.One * 1.1f;
            kit.Put(mark);
            v.Receivers.Add(crystal);
            v.ReceiverMats.Add(mat);
        }

        var linkMat = PieceKit.Flat(PieceKit.Bronze);
        for (int i = 0; i < l.Mirrors.Length; i++)
        {
            Vector3 at = room.World(l.Mirrors[i]);
            kit.Put(PieceKit.Mesh(new CylinderMesh { TopRadius = 0.5f, BottomRadius = 0.6f, Height = 0.7f, RadialSegments = 12 }, at + Vector3.Up * 0.35f, PieceKit.Flat(PieceKit.WallCap)));
            var turn = new Node3D { Position = at + Vector3.Up * 1.15f, Rotation = new Vector3(0f, Angle(l.Start[i]), 0f) };
            kit.Put(turn);
            if (l.Splitter[i])
            {
                // A glass splitter: clear pane in a teal frame, light passes and turns.
                turn.AddChild(PieceKit.Mesh(new BoxMesh { Size = new Vector3(1.6f, 1.0f, 0.06f) }, Vector3.Zero, new StandardMaterial3D
                {
                    AlbedoColor = new Color(0.75f, 1f, 0.95f, 0.35f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    Roughness = 0.05f, EmissionEnabled = true, Emission = PieceKit.Teal, EmissionEnergyMultiplier = 0.25f,
                }, false));
                foreach (float x in new[] { -0.82f, 0.82f })
                    turn.AddChild(PieceKit.Mesh(new BoxMesh { Size = new Vector3(0.1f, 1.1f, 0.16f) }, new Vector3(x, 0f, 0f), PieceKit.Flat(PieceKit.Teal.Darkened(0.3f))));
                var gem = PieceKit.Mesh(new BoxMesh { Size = new Vector3(0.3f, 0.3f, 0.12f) }, Vector3.Up * 0.7f, PieceKit.Flat(PieceKit.Teal.Darkened(0.3f)));
                gem.Rotation = new Vector3(0f, 0f, Mathf.Pi * 0.25f);
                turn.AddChild(gem);
            }
            else
            {
                turn.AddChild(PieceKit.Mesh(new BoxMesh { Size = new Vector3(1.6f, 1.0f, 0.12f) }, Vector3.Zero, new StandardMaterial3D
                {
                    AlbedoColor = MirrorTint, Metallic = 0.35f, Roughness = 0.1f,
                    EmissionEnabled = true, Emission = MirrorTint, EmissionEnergyMultiplier = 0.35f,
                }));
                turn.AddChild(PieceKit.Mesh(new BoxMesh { Size = new Vector3(1.72f, 0.1f, 0.2f) }, Vector3.Up * 0.55f, PieceKit.Flat(PieceKit.Bronze)));
                turn.AddChild(PieceKit.Mesh(new BoxMesh { Size = new Vector3(1.72f, 0.1f, 0.2f) }, Vector3.Down * 0.55f, PieceKit.Flat(PieceKit.Bronze)));
            }
            v.Mirrors.Add(turn);
            kit.Post(at, 0.62f, 1.8f);
        }
        // Linked mirrors: the same rune on the floor beside each, and a line between them.
        int shape = 0;
        for (int g = 0; g < l.Groups; g++)
        {
            int mask = GroupMask(l, g);
            if (PuzzleSolver.PopCount(mask) < 2) continue;
            var members = new List<int>();
            for (int i = 0; i < l.Mirrors.Length; i++) if ((mask >> i & 1) != 0) members.Add(i);
            RuneSymbol rune = LinkShape[shape++ % LinkShape.Length];
            foreach (int i in members) FloorSymbol(kit, rune, linkMat, room.World(l.Mirrors[i]) + new Vector3(0.72f, 0f, 0.72f), 0.55f);
            var holder = new Node3D();
            kit.Put(holder);
            for (int k = 1; k < members.Count; k++)
                PieceKit.Segment(holder, room.World(l.Mirrors[members[k - 1]]) + Vector3.Up * 0.06f, room.World(l.Mirrors[members[k]]) + Vector3.Up * 0.06f, linkMat, 0.1f);
        }
        v.Beam = new Node3D { Name = "Beam" };
        kit.Put(v.Beam);
        return v;
    }

    private static void RebuildBeam(RoomPlan room, Data l, View v, int bits)
    {
        foreach (Node n in v.Beam.GetChildren()) n.QueueFree();
        var mats = BeamColours.Select(c => PieceKit.Glow(c, 4f)).ToArray();
        (List<Ray> rays, _, _) = Beam(room, l, bits);
        // Two colours in one cell sit side by side, so a crossing shows both.
        Vector3 Point(int cell, int colour) => room.World(cell) + Vector3.Up * (1.15f + 0.12f * colour);
        foreach (Ray ray in rays)
        {
            Vector3 a = Point(ray.From, ray.Colour);
            if (ray.To < 0)
            {
                // Off the edge of the floor: run the beam on a metre.
                PieceKit.Segment(v.Beam, a, a + new Vector3(RoomPlan.Dx[ray.OffSide], 0f, RoomPlan.Dz[ray.OffSide]), mats[ray.Colour]);
                continue;
            }
            Vector3 b = Point(ray.To, ray.Colour);
            if (ray.Half) b = a.Lerp(b, 0.55f);
            PieceKit.Segment(v.Beam, a, b, mats[ray.Colour]);
        }
    }

    public override void Sync(PieceKit kit, object view, RoomPlan room, PuzzleState st, float t, float dt, bool changed)
    {
        var v = (View)view;
        Data l = room.Layout<Data>();
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
            RebuildBeam(room, l, v, bits);
            v.LitBits = st.Solved(room) ? (1 << l.Receivers.Length) - 1 : LitBits(room, l, bits);
            for (int c = 0; c < l.Receivers.Length; c++)
            {
                Color glow = BeamColours[l.ReceiverColour[c]];
                PieceKit.Lit(v.ReceiverMats[c], (v.LitBits >> c & 1) != 0, glow, glow.Darkened(0.45f), 2.5f);
            }
        }
        for (int c = 0; c < l.Receivers.Length; c++)
        {
            bool lit = (v.LitBits >> c & 1) != 0;
            Vector3 home = room.World(l.Receivers[c]) + Vector3.Up * (lit ? 1.65f : 1.2f);
            v.Receivers[c].Position = v.Receivers[c].Position.MoveToward(home, dt * 2f);
            if (lit) v.Receivers[c].RotateY(dt * 2.2f);
        }
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
        int i = NextMirror(st, room, me);
        return i < 0 ? null : new PuzzleHint(room.World(room.Layout<Data>().Mirrors[i]), "?");
    }

    public override string Status(PuzzleMap map, RoomPlan room)
    {
        Data l = room.Layout<Data>();
        int lit = map.State.Solved(room) ? (1 << l.Receivers.Length) - 1 : LitBits(room, l, map.State.Get(room, PuzzleState.RA));
        return Meta.Loc.F(Line, PuzzleSolver.PopCount(lit), l.Receivers.Length);
    }

    public override Vector3 Bot(PuzzleBot bot, RoomPlan room, Vector3 me)
    {
        int i = NextMirror(bot.State, room, me);
        return i < 0 ? Vector3.Zero : bot.Use(room, me, room.World(room.Layout<Data>().Mirrors[i]), PuzzleAction.Mirror, i);
    }

    public override void Glyph(CanvasItem ci, Vector2 at, float size, Color c)
    {
        float h = size * 0.6f, w = Mathf.Max(1f, size * 0.18f);
        ci.DrawLine(at + new Vector2(-h, h), at + new Vector2(h, -h), c, w);
        ci.DrawLine(at + new Vector2(-h, -h * 0.2f), at + new Vector2(0f, -h * 0.2f), c, w * 0.6f);
    }
}
