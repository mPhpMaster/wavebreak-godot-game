using System;
using System.Collections.Generic;
using Godot;

namespace Wavebreak.Game.Puzzle;

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
/// solved, entered, entered at (<see cref="RE"/>), and the slots a room's
/// <see cref="PuzzleKind"/> owns: <see cref="RA"/>-<see cref="RD"/>,
/// <see cref="RX"/>, <see cref="RY"/>, <see cref="RPressed"/>,
/// <see cref="RResets"/> and <see cref="RFails"/> (each kind's file says what
/// it keeps in them). The rules for a room are its kind's; this class keeps
/// the map-wide ones: rooms entered, hints, medals, the skip portal.
/// </summary>
public sealed class PuzzleState
{
    public const int Globals = 8, PerRoom = 12;
    public const float SkipSeconds = 150f;

    // Globals.
    public const int GFlags = 0, GTier = 1, GHints = 2, GSolveTenths = 3, GHintRoom = 4, GHintSeq = 5;
    public const int FlagFinished = 1, FlagSkipOpen = 2, FlagSkipped = 4;

    // Per room. RX was the twin plates' assist (gone: no room needs two players).
    public const int RSolved = 0, REntered = 1, RX = 2, RA = 3, RB = 4, RC = 5, RD = 6, RE = 7, RPressed = 8, RResets = 9, RFails = 10, RY = 11;

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
            room.Def?.InitState(this, room);
            if (room.Kind is RoomKind.Start) V[room.Slot + RSolved] = 1;
        }
        V[GHintRoom] = -1;
        Dirty.Clear();
    }

    // ----------------------------------------------------------- reading

    public int this[int i] => V[i];

    public int Get(RoomPlan room, int offset) => V[room.Slot + offset];

    public bool Solved(int room) => V[Plan.Rooms[room].Slot + RSolved] != 0;

    public bool Solved(RoomPlan room) => V[room.Slot + RSolved] != 0;

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

    // ----------------------------------------------------------- writing

    /// <summary>Write one value (host); remembered for the next broadcast.</summary>
    public void Set(int index, int value)
    {
        if (index < 0 || index >= V.Length || V[index] == value) return;
        V[index] = value;
        Dirty.Add(index);
        Version++;
    }

    /// <summary>Write one of a room's slots (host).</summary>
    public void SetR(RoomPlan room, int offset, int value) => Set(room.Slot + offset, value);

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

    public static int Tenths(float seconds) => Mathf.RoundToInt(seconds * 10f);

    /// <summary>
    /// The host's per-frame pass over where everyone stands: rooms entered,
    /// then each room's kind (plates, tiles, rings), then the skip portal.
    /// Returns the rooms entered for the first time this call (their enemies
    /// arrive). <paramref name="connected"/> is how many players are in the
    /// session (kept for callers; no rule depends on it any more).
    /// </summary>
    public List<int> Step(IReadOnlyList<Vector3> players, float now, int connected = 1)
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
                room.Def?.OnEnter(this, room, r, now);
                entered.Add(r);
            }
            if (room.Def is { } def && !Solved(room)) def.HostStep(this, room, r, players, now);
        }
        if (!Finished && !Skipped && now >= Plan.SkipSeconds && (V[GFlags] & FlagSkipOpen) == 0)
            Set(GFlags, V[GFlags] | FlagSkipOpen);
        return entered;
    }

    /// <summary>Host: an ask from any player. Hints are the map's; the rest
    /// go to the room's kind. False when nothing took it.</summary>
    public bool Act(int r, PuzzleAction action, int a, int b, float now)
    {
        if (r < 0 || r >= Plan.Rooms.Count) return false;
        if (action == PuzzleAction.Hint)
        {
            Hint(r, now);
            return true;
        }
        RoomPlan room = Plan.Rooms[r];
        if (room.Def is not { } def || Solved(room)) return false;
        return def.Act(this, room, r, action, a, b, now);
    }

    public void Hint(int r, float now)
    {
        Set(GHints, V[GHints] + 1);
        Set(GHintRoom, r);
        Set(GHintSeq, V[GHintSeq] + 1);
        if (r > 0 && r < Plan.Rooms.Count && Plan.Rooms[r].Def is { } def && !Solved(r)) def.OnHint(this, Plan.Rooms[r], r, now);
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

    // ------------------------------------------------------------ helpers

    /// <summary>Is this body standing on <paramref name="cell"/> (feet low enough to press)?</summary>
    public static bool OnCell(RoomPlan room, Vector3 p, int cell) => cell >= 0 && room.CellAt(p) == cell && p.Y < room.Centre.Y + 1.6f;
}
