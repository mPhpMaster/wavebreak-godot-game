using System;
using Godot;
using Wavebreak.Game.Puzzle;

namespace Wavebreak.Game;

/// <summary>
/// The bot on a puzzle map. Between rooms it walks the doors and corridors in
/// order; in the first unsolved room it asks that room's kind
/// (<see cref="PuzzleKind.Bot"/>) what to do from the room as it stands, so a
/// teammate's move or a shove never leaves it lost, and the kind uses the
/// real controls through <see cref="PuzzleBot"/>: it walks into crates and
/// stones, presses interact only when the HUD's focus is the piece it means,
/// waits for calm at a gust ring, and stands on plates and tiles. With every
/// room solved it claims the first prize and walks into the exit.
///
/// It never hangs a run: if it is still at it well after the SKIP portal has
/// opened (or <c>--puzzle-skip</c> asks), it takes the skip. In co-op only the
/// lowest slot works most rooms (two bots toggling the same pieces would
/// undo each other); the others hold back by the entrance.
/// </summary>
public sealed partial class Autopilot
{
    private readonly PuzzleBot _puzzleBot = new();

    /// <summary>Test hook (Net/NetSession.Test.Puzzle.cs): when set and it
    /// returns a wish, the bot follows it instead.</summary>
    public static Func<Vector3, Vector3?>? PuzzleSteer { get; set; }

    /// <summary>Test hook: who works room r in co-op (true: this machine,
    /// false: hold back, null: the lowest slot, as usual).</summary>
    public static Func<int, bool?>? PuzzleLeaderOverride { get; set; }

    private static readonly bool DebugBot = Array.IndexOf(OS.GetCmdlineUserArgs(), "--puzzle-bot-log") >= 0;
    private static readonly bool SkipPuzzles = Array.IndexOf(OS.GetCmdlineUserArgs(), "--puzzle-skip") >= 0;

    private Vector3 PuzzleThink(PuzzleMap map, Vector3 me)
    {
        _puzzleBot.Begin(map, _clock);
        if (PuzzleSteer?.Invoke(me) is { } steer) return steer;
        PuzzleState st = map.State;
        PuzzlePlan plan = map.Plan;
        int r = plan.RoomAt(me);

        if (st.Finished)
        {
            // Through the doors to the vault first: its prizes and exit are there.
            int vault = plan.Rooms.Count - 1;
            if (r != vault) return TowardRoom(map, me, r, vault);
            if (!map.Claimed && map.Offers is { Count: > 0 } && map.PedestalAt(0) is { } ped)
            {
                if (map.Focus is { Claim: true, Index: 0 } && _puzzleBot.Ready()) map.RequestInteract();
                return PuzzleBot.Toward(me, ped, 0.8f);
            }
            return map.ExitTarget is { } exit ? PuzzleBot.Toward(me, exit, 0.4f) : Vector3.Zero;
        }
        int frontier = st.Frontier;
        if (DebugBot && Engine.GetPhysicsFrames() % 90 == 0) GD.Print($"[puzzle-bot] at={me} room={r} frontier={frontier} focus={map.Focus} t={map.Elapsed:0.0}");
        if (st.SkipOpen && map.SkipPosition is { } skip && (SkipPuzzles || map.Elapsed > plan.SkipSeconds + 25f))
        {
            int at = plan.RoomAt(skip);
            return r == at || at < 0 ? PuzzleBot.Toward(me, skip, 0.4f) : TowardRoom(map, me, r, at);
        }

        if (r != frontier) return TowardRoom(map, me, r, frontier);
        RoomPlan room = plan.Rooms[r];
        if (room.Def is not { } kind) return Vector3.Zero;
        bool leads = PuzzleLeaderOverride?.Invoke(r) ?? (!kind.BotLeaderOnly || PuzzleLeader());
        return leads ? kind.Bot(_puzzleBot, room, me) : PuzzleBot.Hold(room, me);
    }

    private Vector3 _stuckFrom;
    private float _stuckFor, _sidestepFor;
    private Vector3 _sidestep;

    /// <summary>Wanting to move but going nowhere for a second and a half
    /// (wedged on a pillar corner or a piece): sidestep for a moment.</summary>
    private Vector3 Unstick(Vector3 me, Vector3 wish)
    {
        float dt = (float)_player.GetPhysicsProcessDeltaTime();
        if (_sidestepFor > 0f)
        {
            _sidestepFor -= dt;
            return _sidestep;
        }
        if (wish.LengthSquared() < 0.04f || me.DistanceTo(_stuckFrom) > 0.4f)
        {
            _stuckFrom = me;
            _stuckFor = 0f;
            return wish;
        }
        _stuckFor += dt;
        if (_stuckFor < 1.5f) return wish;
        _stuckFor = 0f;
        _stuckFrom = me;
        Vector3 w = wish.Normalized();
        _sidestep = (new Vector3(-w.Z, 0f, w.X) * (_clock % 2f < 1f ? 1f : -1f) - w * 0.5f).Normalized();
        _sidestepFor = 0.6f;
        return _sidestep;
    }

    /// <summary>Co-op: the lowest slot works the pieces that toggle.</summary>
    private static bool PuzzleLeader()
    {
        if (!Net.Coop.InRun || Net.NetSession.Instance is not { } net) return true;
        int lowest = int.MaxValue;
        foreach (Net.CoopPeer p in Net.Coop.Roster) lowest = Math.Min(lowest, p.Slot);
        return net.LocalSlot <= lowest;
    }

    /// <summary>Between rooms: through the doors and corridors in order
    /// towards <paramref name="target"/>. On the puzzle grid it walks the grid
    /// to the cell before the doorway; from the open strip round the grid it
    /// lines up with the doorway; lined up, it goes straight through (a
    /// doorway and its corridor sit on the line between two room centres).</summary>
    private static Vector3 TowardRoom(PuzzleMap map, Vector3 me, int r, int target)
    {
        PuzzlePlan plan = map.Plan;
        if (r < 0)
        {
            int corridor = plan.CorridorAt(me, 1.5f);
            if (corridor < 0) return PuzzleBot.Toward(me, plan.Rooms[plan.NearestRoom(me)].Centre, 0.5f);
            return PuzzleBot.Toward(me, plan.Rooms[target > corridor ? corridor + 1 : corridor].Centre, 0.5f);
        }
        RoomPlan here = plan.Rooms[r];
        int into = r < target ? r + 1 : r - 1;
        int side = r < target ? here.ExitSide : here.EntrySide;
        var dir = new Vector3(RoomPlan.Dx[side], 0f, RoomPlan.Dz[side]);
        Vector3 rel = me - here.Centre;
        float along = rel.X * dir.X + rel.Z * dir.Z;
        float across = Mathf.Abs(rel.X * dir.Z - rel.Z * dir.X);
        float grid = RoomPlan.N * RoomPlan.Cell * 0.5f;
        int doorCell = RoomPlan.DoorCell(side);
        int cell = here.CellAt(me);
        if (across < 1.2f && (along > grid - 1.2f || cell == doorCell)) return PuzzleBot.Toward(me, plan.Rooms[into].Centre, 0.3f);
        if (cell >= 0) return PuzzleBot.WalkGrid(here, me, doorCell, false);
        return PuzzleBot.Toward(me, here.Centre + dir * (grid + 1.5f), 0.3f);
    }

}
