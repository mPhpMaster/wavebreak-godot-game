using System;
using System.Collections.Generic;
using Godot;

namespace Wavebreak.Game.Puzzle;

/// <summary>What happened to a bell strike, for sound and the HUD.</summary>
public enum StrikeResult { Ignored, Right, Wrong, Done }

/// <summary>
/// The whole state of a puzzle map as one flat array of ints, and the rules
/// that change it. Flat on purpose: co-op sends changed slots as (index,
/// value) pairs, a late joiner or a new host gets the array whole, and the
/// self-check compares two arrays to prove two machines agree.
///
/// Only the host (or the single player) calls the rule methods; clients send
/// their wishes to the host (Net/NetSession.Puzzle.cs) and display whatever
/// the array says. Times are puzzle seconds since the map was entered, kept in
/// tenths so they fit the array.
///
/// Layout: <see cref="Globals"/> values for the map, then
/// <see cref="PerRoom"/> per room starting at <see cref="RoomPlan.Slot"/>:
/// solved, entered, the twin plates' assist, five piece values (A-E, below),
/// pressed plates, crate resets and wrong bells.
///
///   crates   A-C: crate cells
///   mirrors  A: orientation bits
///   wind     A: plates touched (bits)
///   runes    A: bells struck right in a row, B: order shown from (tenths, -1 none), C: times shown
///   levers   A: levers pulled (bits)
///   twin     A, B: latch of plate A, B until (tenths)
///   every    E: entered at (tenths)
/// </summary>
public sealed class PuzzleState
{
    public const int Globals = 8, PerRoom = 12;
    public const float SkipSeconds = 150f;
    /// <summary>Seconds a twin room waits for a second player before its
    /// plates latch anyway, so a partner who wanders off cannot block it.</summary>
    public const float TwinAssistAfter = 45f;

    // Globals.
    public const int GFlags = 0, GTier = 1, GHints = 2, GSolveTenths = 3, GHintRoom = 4, GHintSeq = 5;
    public const int FlagFinished = 1, FlagSkipOpen = 2, FlagSkipped = 4;

    // Per room.
    public const int RSolved = 0, REntered = 1, RAssist = 2, RA = 3, RB = 4, RC = 5, RD = 6, RE = 7, RPressed = 8, RResets = 9, RFails = 10;

    public readonly PuzzlePlan Plan;
    public readonly int[] V;

    /// <summary>Slots changed since the host last sent them.</summary>
    public readonly SortedSet<int> Dirty = new();

    /// <summary>Bumped on every change, so views redraw only when something moved.</summary>
    public int Version { get; private set; }

    public PuzzleState(PuzzlePlan plan)
    {
        Plan = plan;
        V = new int[plan.StateLength];
        foreach (RoomPlan room in plan.Rooms)
        {
            switch (room.Kind)
            {
                case RoomKind.Crates:
                    for (int i = 0; i < room.CrateStart.Length; i++) V[room.Slot + RA + i] = room.CrateStart[i];
                    break;
                case RoomKind.Mirrors:
                    V[room.Slot + RA] = PuzzleGen.MirrorStartBits(room);
                    break;
                case RoomKind.Runes:
                    V[room.Slot + RB] = -1;
                    break;
                case RoomKind.Twin:
                    V[room.Slot + RAssist] = plan.Players >= 2 ? 0 : 1;
                    V[room.Slot + RA] = V[room.Slot + RB] = -1;
                    break;
            }
            if (room.Kind is RoomKind.Start) V[room.Slot + RSolved] = 1;
        }
        V[GHintRoom] = -1;
    }

    // ----------------------------------------------------------- reading

    public int this[int i] => V[i];

    public int Get(RoomPlan room, int offset) => V[room.Slot + offset];

    public bool Solved(int room) => V[Plan.Rooms[room].Slot + RSolved] != 0;

    public bool Finished => (V[GFlags] & FlagFinished) != 0;
    public bool SkipOpen => (V[GFlags] & FlagSkipOpen) != 0 && !Finished;
    public bool Skipped => (V[GFlags] & FlagSkipped) != 0;
    public RewardTier Tier => (RewardTier)V[GTier];
    public int Hints => V[GHints];
    public float SolveSeconds => V[GSolveTenths] / 10f;

    /// <summary>The first puzzle room not yet solved, or the vault when all are.</summary>
    public int Frontier
    {
        get
        {
            for (int i = 1; i < Plan.Rooms.Count - 1; i++) if (!Solved(i)) return i;
            return Plan.Rooms.Count - 1;
        }
    }

    public int SolvedCount
    {
        get
        {
            int n = 0;
            for (int i = 1; i < Plan.Rooms.Count - 1; i++) if (Solved(i)) n++;
            return n;
        }
    }

    /// <summary>Is the door between room <paramref name="i"/> and the next open?</summary>
    public bool DoorOpen(int i) => i == 0 || Solved(i);

    public int[] Crates(RoomPlan room)
    {
        var c = new int[room.CrateStart.Length];
        for (int i = 0; i < c.Length; i++) c[i] = V[room.Slot + RA + i];
        return c;
    }

    /// <summary>Is the rune order being shown at <paramref name="now"/>?</summary>
    public bool RuneShowing(RoomPlan room, float now, out int bell)
    {
        bell = -1;
        int from = Get(room, RB);
        if (from < 0) return false;
        float t = now - from / 10f;
        if (t < 0f) return true;   // about to start: hold still
        int step = (int)(t / PuzzleGen.RuneBeat);
        if (step >= room.Sequence.Length) return false;
        if (t - step * PuzzleGen.RuneBeat < PuzzleGen.RuneBeat * 0.75f) bell = room.Sequence[step];
        return true;
    }

    /// <summary>A plate of the twin room counts as held at <paramref name="now"/>.</summary>
    public bool TwinHeld(RoomPlan room, int plate, float now)
    {
        bool on = (Get(room, RPressed) >> plate & 1) != 0;
        int until = Get(room, plate == 0 ? RA : RB);
        return on || Get(room, RAssist) != 0 && until >= 0 && now * 10f < until;
    }

    // ----------------------------------------------------------- writing

    /// <summary>Write one value (host); remembered for the next broadcast.</summary>
    public void Set(int index, int value)
    {
        if (index < 0 || index >= V.Length || V[index] == value) return;
        V[index] = value;
        Dirty.Add(index);
        Version++;
    }

    private void SetR(RoomPlan room, int offset, int value) => Set(room.Slot + offset, value);

    /// <summary>A value from the host (client); not re-sent.</summary>
    public void Apply(int index, int value)
    {
        if (index < 0 || index >= V.Length || V[index] == value) return;
        V[index] = value;
        Version++;
    }

    /// <summary>The whole array from the host (late join, new host).</summary>
    public void ApplyAll(int[] values)
    {
        Array.Copy(values, V, Math.Min(values.Length, V.Length));
        Version++;
    }

    public int Hash()
    {
        int h = 17;
        foreach (int v in V) h = h * 31 + v;
        return h;
    }

    // ------------------------------------------------------------- rules

    private static int Tenths(float seconds) => Mathf.RoundToInt(seconds * 10f);

    /// <summary>
    /// The host's per-frame pass over where everyone stands: rooms entered,
    /// plates held, wind plates touched, twin latches, the twin assist, the
    /// skip portal. Returns the rooms entered for the first time this call
    /// (their enemies arrive).
    /// </summary>
    public List<int> Step(IReadOnlyList<Vector3> players, float now, int connected)
    {
        var entered = new List<int>();
        for (int r = 0; r < Plan.Rooms.Count; r++)
        {
            RoomPlan room = Plan.Rooms[r];
            bool anyone = false;
            foreach (Vector3 p in players) anyone |= room.Contains(p, 0.4f);
            if (anyone && Get(room, REntered) == 0)
            {
                SetR(room, REntered, 1);
                SetR(room, RE, Tenths(now));
                if (room.Kind == RoomKind.Runes) SetR(room, RB, Tenths(now + 0.8f));
                entered.Add(r);
            }

            switch (room.Kind)
            {
                case RoomKind.Crates:
                {
                    int[] crates = Crates(room);
                    int pressed = 0;
                    for (int i = 0; i < room.Plates.Length; i++)
                    {
                        int plate = room.Plates[i];
                        bool on = Array.IndexOf(crates, plate) >= 0;
                        foreach (Vector3 p in players) on |= room.CellAt(p) == plate && p.Y < room.Centre.Y + 1.6f;
                        if (on) pressed |= 1 << i;
                    }
                    SetR(room, RPressed, pressed);
                    if (pressed == (1 << room.Plates.Length) - 1) MarkSolved(r, now);
                    break;
                }
                case RoomKind.Wind:
                {
                    int touched = Get(room, RA), on = 0;
                    for (int i = 0; i < room.WindCentres.Length; i++)
                        foreach (Vector3 p in players)
                        {
                            Vector3 d = p - room.World(room.WindCentres[i]);
                            if (new Vector2(d.X, d.Z).Length() <= room.WindInner && d.Y < 1.6f) on |= 1 << i;
                        }
                    SetR(room, RPressed, on);
                    SetR(room, RA, touched | on);
                    if ((touched | on) == (1 << room.WindCentres.Length) - 1) MarkSolved(r, now);
                    break;
                }
                case RoomKind.Twin:
                {
                    int on = 0;
                    foreach (Vector3 p in players)
                    {
                        int cell = room.CellAt(p);
                        if (cell == room.PlateA && p.Y < 1.6f) on |= 1;
                        if (cell == room.PlateB && p.Y < 1.6f) on |= 2;
                    }
                    int was = Get(room, RPressed);
                    SetR(room, RPressed, on);
                    bool assist = connected <= 1 || Get(room, REntered) != 0 && now - Get(room, RE) / 10f >= TwinAssistAfter;
                    if (Get(room, RSolved) == 0) SetR(room, RAssist, assist ? 1 : 0);
                    // The latch runs down from the moment a plate is let go
                    // (one write per release, not one per frame held).
                    if ((was & 1) != 0 && (on & 1) == 0) SetR(room, RA, Tenths(now + room.Latch));
                    if ((was & 2) != 0 && (on & 2) == 0) SetR(room, RB, Tenths(now + room.Latch));
                    if (TwinHeld(room, 0, now) && TwinHeld(room, 1, now)) MarkSolved(r, now);
                    break;
                }
            }
        }
        if (!Finished && !Skipped && now >= Plan.SkipSeconds && (V[GFlags] & FlagSkipOpen) == 0)
            Set(GFlags, V[GFlags] | FlagSkipOpen);
        return entered;
    }

    /// <summary>Push crate <paramref name="crate"/> one cell towards
    /// <paramref name="side"/>. False when it is not there or cannot move.</summary>
    public bool Push(int r, int crate, int side, float now)
    {
        RoomPlan room = Plan.Rooms[r];
        if (room.Kind != RoomKind.Crates || crate < 0 || crate >= room.CrateStart.Length || side is < 0 or > 3) return false;
        int[] crates = Crates(room);
        int target = PuzzleSolver.Step(crates[crate], side);
        if (!PuzzleSolver.CrateCanEnter(room, target, crates)) return false;
        SetR(room, RA + crate, target);
        return true;
    }

    public void ResetCrates(int r)
    {
        RoomPlan room = Plan.Rooms[r];
        if (room.Kind != RoomKind.Crates) return;
        for (int i = 0; i < room.CrateStart.Length; i++) SetR(room, RA + i, room.CrateStart[i]);
        SetR(room, RResets, Get(room, RResets) + 1);
    }

    public void TurnMirror(int r, int mirror, float now)
    {
        RoomPlan room = Plan.Rooms[r];
        if (room.Kind != RoomKind.Mirrors || mirror < 0 || mirror >= room.MirrorCells.Length) return;
        int bits = Get(room, RA) ^ (1 << mirror);
        SetR(room, RA, bits);
        if (PuzzleSolver.Beam(room, bits).Hit) MarkSolved(r, now);
    }

    public void PullLever(int r, int lever, float now)
    {
        RoomPlan room = Plan.Rooms[r];
        if (room.Kind != RoomKind.Levers || lever < 0 || lever >= room.LeverMasks.Length) return;
        int pulled = Get(room, RA) ^ (1 << lever);
        SetR(room, RA, pulled);
        if (PuzzleSolver.Lamps(room, pulled) == PuzzleSolver.FullLamps(room)) MarkSolved(r, now);
    }

    public StrikeResult StrikeBell(int r, int bell, float now)
    {
        RoomPlan room = Plan.Rooms[r];
        if (room.Kind != RoomKind.Runes || bell < 0 || bell >= room.Bells.Length || Get(room, RSolved) != 0) return StrikeResult.Ignored;
        if (RuneShowing(room, now, out _)) return StrikeResult.Ignored;
        int progress = Get(room, RA);
        if (room.Sequence[progress] == bell)
        {
            SetR(room, RA, progress + 1);
            if (progress + 1 >= room.Sequence.Length)
            {
                MarkSolved(r, now);
                return StrikeResult.Done;
            }
            return StrikeResult.Right;
        }
        // Wrong: start over, and the order rings again.
        SetR(room, RA, 0);
        SetR(room, RFails, Get(room, RFails) + 1);
        ShowOrder(r, now + 0.9f);
        return StrikeResult.Wrong;
    }

    /// <summary>Ring the order again from the start (the echo bell, a hint, a mistake).</summary>
    public void ShowOrder(int r, float at)
    {
        RoomPlan room = Plan.Rooms[r];
        if (room.Kind != RoomKind.Runes || Get(room, RSolved) != 0) return;
        SetR(room, RA, 0);
        SetR(room, RB, Tenths(at));
        SetR(room, RC, Get(room, RC) + 1);
    }

    public void Hint(int r, float now)
    {
        Set(GHints, V[GHints] + 1);
        Set(GHintRoom, r);
        Set(GHintSeq, V[GHintSeq] + 1);
        if (r > 0 && r < Plan.Rooms.Count && Plan.Rooms[r].Kind == RoomKind.Runes) ShowOrder(r, now + 0.3f);
    }

    public void MarkSolved(int r, float now)
    {
        RoomPlan room = Plan.Rooms[r];
        if (Get(room, RSolved) != 0) return;
        SetR(room, RSolved, 1);
        if (Finished || Skipped) return;
        for (int i = 1; i < Plan.Rooms.Count - 1; i++) if (!Solved(i)) return;
        Set(GSolveTenths, Tenths(now));
        Set(GTier, (int)TierFor(now, V[GHints]));
        Set(GFlags, (V[GFlags] | FlagFinished) & ~FlagSkipOpen);
    }

    /// <summary>Gold: under the gold time with no hint. Silver: under the
    /// silver time with at most one. Bronze: solved at all.</summary>
    public RewardTier TierFor(float seconds, int hints)
    {
        if (seconds <= Plan.GoldSeconds && hints == 0) return RewardTier.Gold;
        if (seconds <= Plan.SilverSeconds && hints <= 1) return RewardTier.Silver;
        return RewardTier.Bronze;
    }

    /// <summary>The team took the SKIP portal: the lowest reward.</summary>
    public void Skip(float now)
    {
        if (Finished || Skipped) return;
        Set(GSolveTenths, Tenths(now));
        Set(GTier, (int)RewardTier.Skipped);
        Set(GFlags, V[GFlags] | FlagSkipped);
    }
}
