using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Core;

namespace Wavebreak.Game.Puzzle;

/// <summary>What a player can ask a puzzle to do. Sent as an int in co-op.
/// New kinds use <see cref="Use"/> (a = which piece, b = an argument) and
/// never need a new value; the older ones keep theirs.</summary>
public enum PuzzleAction { None, Push, Mirror, Lever, Bell, Echo, Reset, Hint, Use }

/// <summary>How a kind's hall is dressed (colours, drawn surfaces, props):
/// PuzzleMap.Build.cs. Pick one; a new style is only needed for a new look.</summary>
public enum HallStyle { Plain, Workshop, Observatory, Gallery, Temple, Foundry, Frost, Aqueduct, Ember, Counting, Garden, Vault }

/// <summary>Where a hint points: a world position, the letter on the marker,
/// and (crates, stones) the side the marked piece moves towards, or -1.</summary>
public readonly record struct PuzzleHint(Vector3 At, string Label = "?", int ArrowSide = -1);

/// <summary>Offer a piece to the interact focus: <paramref name="index"/> and
/// <paramref name="arg"/> travel with <paramref name="action"/>.</summary>
public delegate void FocusOffer(PuzzleAction action, int index, Vector3 at, string verb, int arg = 0);

/// <summary>
/// What a kind's solution script drives (<see cref="PuzzleKind.Solve"/>): one
/// player's body and their asks. On paper (<c>--check-puzzles</c>) it runs
/// the rules directly and completes at once; live it teleports the real
/// player and asks the real map, waiting real time. Only ONE body exists, so
/// a script that passes proves the kind can be solved alone.
/// </summary>
public interface IPuzzleDriver
{
    PuzzleState State { get; }
    /// <summary>Puzzle seconds.</summary>
    float Now { get; }
    /// <summary>Put the one player at <paramref name="world"/> (on the floor).</summary>
    Task Stand(Vector3 world);
    /// <summary>Ask for an action, as interact would.</summary>
    Task Ask(PuzzleAction action, int a, int b = 0);
    Task Wait(float seconds);
}

/// <summary>
/// One kind of puzzle room. <b>Adding a puzzle is one new file</b> in
/// Game/Puzzle/Kinds with a class deriving from this (it is found and
/// registered by <see cref="PuzzleKinds"/>; nothing else needs editing) plus
/// its Arabic lines. See ARCHITECTURE.md "How to add a puzzle".
///
/// A room is data first (<see cref="RoomPlan"/> with this kind's own layout
/// object in <see cref="RoomPlan.Data"/>), its live values are slots of the
/// shared <see cref="PuzzleState"/> array (<see cref="PuzzleState.RA"/> to
/// <see cref="PuzzleState.RD"/>, <see cref="PuzzleState.RX"/>,
/// <see cref="PuzzleState.RY"/>, <see cref="PuzzleState.RPressed"/>,
/// <see cref="PuzzleState.RResets"/>, <see cref="PuzzleState.RFails"/>), and
/// only the host changes them, through <see cref="HostStep"/> (where bodies
/// stand) and <see cref="Act"/> (what players ask). Every machine draws from
/// the array, so co-op sync is free.
///
/// The contract every kind keeps, checked by <c>--check-puzzles</c>:
/// <list type="bullet">
/// <item><see cref="Generate"/> is deterministic from its inputs and only ever
/// leaves a layout its own solver solves (redraw, then a fixed fallback).</item>
/// <item><see cref="Solve"/> solves the fresh room with ONE player.</item>
/// <item>The way between the doors stays open; nothing needs two players.</item>
/// <item><see cref="HintAt"/> points at the next useful step from the room as it stands.</item>
/// <item><see cref="Bot"/> gets the autopilot through it from any state.</item>
/// </list>
/// </summary>
public abstract class PuzzleKind
{
    // ------------------------------------------------------------ identity

    /// <summary>Stable lowercase id (<c>--puzzle-rooms=</c>, logs, the bag order).</summary>
    public abstract string Id { get; }
    /// <summary>English; Loc.T'd where shown. Needs Arabic.</summary>
    public abstract string Title { get; }
    public abstract string Objective { get; }
    /// <summary>The hint line, shown with whatever <see cref="HintAt"/> marks.</summary>
    public abstract string Hint { get; }
    /// <summary>Every other English string this kind shows (verbs, HUD lines), for the audit.</summary>
    public virtual IEnumerable<string> Strings => Array.Empty<string>();
    /// <summary>Drawn into generated maps (false: only when forced with --puzzle-rooms).</summary>
    public virtual bool InBag => true;
    /// <summary>Players the room needs. Must be 1: no puzzle may need more (checked).</summary>
    public virtual int MinPlayers => 1;
    public virtual HallStyle Style => HallStyle.Plain;
    /// <summary>In co-op only the lowest slot's bot works this room (pieces
    /// that toggle would be undone by two bots); the others hold back.</summary>
    public virtual bool BotLeaderOnly => true;
    /// <summary>Seconds a quick, hint-free solve of this room takes: the
    /// map's gold time is the sum over its rooms plus a walk.</summary>
    public virtual float ParSeconds => 22f;

    // ------------------------------------------------------------- data

    /// <summary>Lay the room out (set <see cref="RoomPlan.Data"/>, pillars,
    /// <see cref="RoomPlan.Fixed"/> cells, <see cref="RoomPlan.Spots"/>). Must
    /// leave a layout that is solvable alone and keeps the doors connected.</summary>
    public abstract void Generate(RoomPlan room, Rng rng, int difficulty);

    /// <summary>Starting values in the state array.</summary>
    public virtual void InitState(PuzzleState st, RoomPlan room) { }

    /// <summary>Host: the team entered the room for the first time.</summary>
    public virtual void OnEnter(PuzzleState st, RoomPlan room, int r, float now) { }

    /// <summary>Host, every frame: where the bodies stand (plates, tiles...).</summary>
    public virtual void HostStep(PuzzleState st, RoomPlan room, int r, IReadOnlyList<Vector3> players, float now) { }

    /// <summary>Host: apply an ask. True when it was this kind's (applied or refused).</summary>
    public virtual bool Act(PuzzleState st, RoomPlan room, int r, PuzzleAction action, int a, int b, float now) => false;

    /// <summary>Host: someone asked for a hint in this room.</summary>
    public virtual void OnHint(PuzzleState st, RoomPlan room, int r, float now) { }

    /// <summary>Solve the room from its current state through
    /// <paramref name="d"/> (one body). Returns null when it ran its answer,
    /// or why it could not (the caller then checks the room is solved).</summary>
    public abstract Task<string?> Solve(IPuzzleDriver d, RoomPlan room, int r);

    // ------------------------------------------------------------ scene

    /// <summary>Build this room's pieces with <paramref name="kit"/>; return
    /// whatever the kind wants back in <see cref="Sync"/> (its view).</summary>
    public abstract object Build(PieceKit kit, RoomPlan room);

    /// <summary>Every frame: draw the state (<paramref name="changed"/> when
    /// the array moved since the last frame).</summary>
    public virtual void Sync(PieceKit kit, object view, RoomPlan room, PuzzleState st, float t, float dt, bool changed) { }

    /// <summary>Sounds for what changed in this room's slots (every machine).</summary>
    public virtual void Sounds(object view, RoomPlan room, Func<int, int> was, Func<int, int> now) { }

    /// <summary>Offer the pieces in reach to interact (unsolved rooms only).</summary>
    public virtual void Offer(PuzzleMap map, RoomPlan room, FocusOffer offer) { }

    /// <summary>This machine's own body in the room (pushing, wind...).</summary>
    public virtual void LocalStep(PuzzleMap map, object view, Player me, RoomPlan room, int r, float dt, bool control) { }

    /// <summary>The next useful step, from the room as it stands; null for none.</summary>
    public abstract PuzzleHint? HintAt(PuzzleState st, RoomPlan room, Vector3? me, float now);

    /// <summary>The HUD's live line for the room ("plates 1/2").</summary>
    public virtual string Status(PuzzleMap map, RoomPlan room) => "";

    // -------------------------------------------------------------- bot

    /// <summary>The autopilot's move wish in this (unsolved, current) room;
    /// it presses interact through <see cref="PuzzleBot.Use"/>.</summary>
    public abstract Vector3 Bot(PuzzleBot bot, RoomPlan room, Vector3 me);

    // ---------------------------------------------------------- minimap

    /// <summary>The room's mark on the minimap and Tab map, about
    /// <paramref name="size"/> pixels from its centre.</summary>
    public virtual void Glyph(CanvasItem ci, Vector2 at, float size, Color c)
        => ci.DrawArc(at, size * 0.6f, 0f, Mathf.Tau, 16, c, Mathf.Max(1f, size * 0.18f), true);

    public override string ToString() => Id;
}

/// <summary>
/// Every <see cref="PuzzleKind"/> in the assembly, found by reflection (sorted
/// by id, so every machine has the same order), and the shuffle bag that
/// decides which kinds a run's maps get.
/// </summary>
public static class PuzzleKinds
{
    private static List<PuzzleKind>? _all;

    public static IReadOnlyList<PuzzleKind> All => _all ??= typeof(PuzzleKind).Assembly.GetTypes()
        .Where(t => t.IsClass && !t.IsAbstract && t.IsSubclassOf(typeof(PuzzleKind)) && t.GetConstructor(Type.EmptyTypes) is not null)
        .Select(t => (PuzzleKind)Activator.CreateInstance(t)!)
        .OrderBy(k => k.Id, StringComparer.Ordinal)
        .ToList();

    /// <summary>The kinds generated maps draw from.</summary>
    public static IReadOnlyList<PuzzleKind> Bag => All.Where(k => k.InBag).ToList();

    public static PuzzleKind? ById(string id) => All.FirstOrDefault(k => string.Equals(k.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The first <paramref name="count"/> puzzle rooms of a run, in order,
    /// drawn from a shuffle bag seeded by the run: every kind once before any
    /// comes back, and never one of the last few rooms' kinds again (so a map
    /// never holds the same kind twice and a refill never repeats the room
    /// before it).
    /// </summary>
    public static List<PuzzleKind> Sequence(ulong runSeed, int count)
    {
        IReadOnlyList<PuzzleKind> kinds = Bag;
        var rng = new Rng(runSeed ^ 0xBA6_5EEDUL);
        var outList = new List<PuzzleKind>(count);
        var bag = new List<PuzzleKind>();
        int recent = Math.Min(4, kinds.Count - 1);
        while (outList.Count < count && kinds.Count > 0)
        {
            if (bag.Count == 0) bag.AddRange(kinds);
            var fresh = new List<int>();
            for (int i = 0; i < bag.Count; i++)
            {
                bool clash = false;
                for (int k = Math.Max(0, outList.Count - recent); k < outList.Count; k++) clash |= ReferenceEquals(outList[k], bag[i]);
                if (!clash) fresh.Add(i);
            }
            int pick = fresh.Count > 0 ? fresh[rng.RangeInt(0, fresh.Count)] : rng.RangeInt(0, bag.Count);
            outList.Add(bag[pick]);
            bag.RemoveAt(pick);
        }
        return outList;
    }
}
