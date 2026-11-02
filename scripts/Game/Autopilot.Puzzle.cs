using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Game.Puzzle;

namespace Wavebreak.Game;

/// <summary>
/// The bot on a puzzle map. It asks <see cref="PuzzleSolver"/> what to do
/// from the room as it stands (so a teammate's push or a shove never leaves
/// it lost), walks there on the room's grid around pillars and pieces, and
/// uses the real controls: it walks into crates to push them, presses
/// interact only when the HUD's focus is the piece it means, waits for calm
/// at a gust ring, and stands on plates. With every room solved it claims the
/// first prize and walks into the exit.
///
/// It never hangs a run: if it is still at it well after the SKIP portal has
/// opened (or <c>--puzzle-skip</c> asks), it takes the skip. In co-op only the
/// lowest slot works the pieces that toggle (two bots pressing the same
/// mirror at once would undo each other); the twin plates split by slot.
/// </summary>
public sealed partial class Autopilot
{
    private PuzzleMap? _puzzleMap;
    private float _puzzleActAt;
    private int _puzzleActVersion = -1;

    /// <summary>Test hook (Net/NetSession.Test.Puzzle.cs): when set and it
    /// returns a wish, the bot follows it instead.</summary>
    public static Func<Vector3, Vector3?>? PuzzleSteer { get; set; }

    private static readonly bool SkipPuzzles = Array.IndexOf(OS.GetCmdlineUserArgs(), "--puzzle-skip") >= 0;

    private Vector3 PuzzleThink(PuzzleMap map, Vector3 me)
    {
        if (!ReferenceEquals(map, _puzzleMap))
        {
            _puzzleMap = map;
            _puzzleActAt = 0f;
            _puzzleActVersion = -1;
        }
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
                if (map.Focus is { Claim: true, Index: 0 } && Ready(map)) map.RequestInteract();
                return Toward(me, ped, 0.8f);
            }
            return map.ExitTarget is { } exit ? Toward(me, exit, 0.4f) : Vector3.Zero;
        }
        int frontier = st.Frontier;
        if (st.SkipOpen && map.SkipPosition is { } skip && (SkipPuzzles || map.Elapsed > plan.SkipSeconds + 25f))
        {
            int at = plan.RoomAt(skip);
            return r == at || at < 0 ? Toward(me, skip, 0.4f) : TowardRoom(map, me, r, at);
        }

        if (r != frontier) return TowardRoom(map, me, r, frontier);
        RoomPlan room = plan.Rooms[r];
        bool leader = PuzzleLeader();

        switch (room.Kind)
        {
            case RoomKind.Crates: return leader ? CratesThink(map, room, me) : Hold(room, me);
            case RoomKind.Mirrors:
            {
                if (!leader) return Hold(room, me);
                int bits = st.Get(room, PuzzleState.RA);
                int answer = PuzzleSolver.SolveMirrors(room, bits);
                for (int i = 0; i < room.MirrorCells.Length; i++)
                    if (answer >= 0 && ((answer ^ bits) >> i & 1) != 0)
                        return UsePiece(map, room, me, room.World(room.MirrorCells[i]), PuzzleAction.Mirror, i);
                return Vector3.Zero;
            }
            case RoomKind.Levers:
            {
                if (!leader) return Hold(room, me);
                int answer = PuzzleSolver.SolveLevers(room, st.Get(room, PuzzleState.RA));
                for (int i = 0; i < room.Levers.Length; i++)
                    if (answer > 0 && (answer >> i & 1) != 0)
                        return UsePiece(map, room, me, room.World(room.Levers[i]), PuzzleAction.Lever, i);
                return Vector3.Zero;
            }
            case RoomKind.Runes:
            {
                if (!leader) return Hold(room, me);
                if (st.RuneShowing(room, map.Elapsed, out _)) return Toward(me, room.Centre + new Vector3(1.6f, 0f, 1.6f), 0.5f);
                int next = room.Sequence[Mathf.Clamp(st.Get(room, PuzzleState.RA), 0, room.Sequence.Length - 1)];
                return UsePiece(map, room, me, room.World(room.Bells[next]), PuzzleAction.Bell, next);
            }
            case RoomKind.Wind: return WindThink(map, room, me);
            case RoomKind.Twin: return TwinThink(map, room, me);
        }
        return Vector3.Zero;
    }

    /// <summary>Co-op: the lowest slot works the pieces that toggle.</summary>
    private static bool PuzzleLeader()
    {
        if (!Net.Coop.InRun || Net.NetSession.Instance is not { } net) return true;
        int lowest = int.MaxValue;
        foreach (Net.CoopPeer p in Net.Coop.Roster) lowest = Math.Min(lowest, p.Slot);
        return net.LocalSlot <= lowest;
    }

    /// <summary>Not leading: stand clear by the room's entrance.</summary>
    private static Vector3 Hold(RoomPlan room, Vector3 me)
    {
        int side = room.EntrySide;
        Vector3 spot = room.Centre + new Vector3(RoomPlan.Dx[side], 0f, RoomPlan.Dz[side]) * 6.5f
                       + new Vector3(RoomPlan.Dz[side], 0f, RoomPlan.Dx[side]) * 3f;
        return Toward(me, spot, 0.8f);
    }

    private bool Ready(PuzzleMap map)
    {
        // One press per state change (and at most one every 0.45 s), so a
        // round trip to the host never makes the bot press twice.
        if (_clock < _puzzleActAt) return false;
        if (map.State.Version == _puzzleActVersion && _clock < _puzzleActAt + 1.5f) return false;
        _puzzleActAt = _clock + 0.45f;
        _puzzleActVersion = map.State.Version;
        return true;
    }

    /// <summary>Walk up to a piece and press interact once it is the focus.</summary>
    private Vector3 UsePiece(PuzzleMap map, RoomPlan room, Vector3 me, Vector3 piece, PuzzleAction action, int index)
    {
        if (map.Focus is { } f && f.Action == action && f.Index == index && f.Room == room.Index)
        {
            if (Ready(map)) map.RequestInteract();
            return Vector3.Zero;
        }
        Vector3 d = piece - me;
        d.Y = 0f;
        if (d.Length() < 2.6f) return d.Normalized() * 0.6f;
        return WalkGrid(room, me, room.CellAt(piece), stopNextTo: true);
    }

    // -------------------------------------------------------------- crates

    private Vector3 CratesThink(PuzzleMap map, RoomPlan room, Vector3 me)
    {
        int[] crates = map.State.Crates(room);
        int from = room.CellAt(me);
        if (from < 0 || PuzzleSolver.Solid(room, from) || Array.IndexOf(crates, from) >= 0) from = NearestFree(room, me, crates);
        List<int>? moves = PuzzleSolver.SolveCrates(room, from, crates, out _, 120000);
        if (moves is null)
        {
            // Stuck (a crate in a corner): ring the reset bell.
            return UsePiece(map, room, me, room.World(room.ResetCell), PuzzleAction.Reset, 0);
        }
        int p = from;
        foreach (int side in moves)
        {
            int next = PuzzleSolver.Step(p, side);
            if (Array.IndexOf(crates, next) >= 0)
            {
                // p is where to stand; then walk into the crate, lined up
                // with it across the push (anywhere along it is fine).
                Vector3 stand = room.World(p);
                var push = new Vector3(RoomPlan.Dx[side], 0f, RoomPlan.Dz[side]);
                Vector3 off = me - stand;
                off.Y = 0f;
                Vector3 across = off - push * off.Dot(push);
                if (p == room.CellAt(me))
                    return across.Length() < 0.3f ? push : (-across).Normalized() * 0.7f + push * 0.1f;
                return WalkGrid(room, me, p, false, crates);
            }
            p = next;
        }
        // No push left: walk onto the last plate.
        return WalkGrid(room, me, p, false, crates);
    }

    private static int NearestFree(RoomPlan room, Vector3 me, int[] crates)
    {
        int best = room.EntryCell;
        float bestD = float.MaxValue;
        for (int c = 0; c < RoomPlan.N * RoomPlan.N; c++)
        {
            if (PuzzleSolver.Solid(room, c) || Array.IndexOf(crates, c) >= 0) continue;
            float d = room.World(c).DistanceSquaredTo(me);
            if (d < bestD) { bestD = d; best = c; }
        }
        return best;
    }

    // ---------------------------------------------------------------- wind

    private Vector3 WindThink(PuzzleMap map, RoomPlan room, Vector3 me)
    {
        int touched = map.State.Get(room, PuzzleState.RA);
        for (int i = 0; i < room.WindCentres.Length; i++)
        {
            if ((touched >> i & 1) != 0) continue;
            Vector3 c = room.World(room.WindCentres[i]);
            Vector3 d = me - c;
            d.Y = 0f;
            float dist = d.Length();
            if (dist <= room.WindOuter + 0.2f) return Toward(me, c, 0.1f);
            // Wait at the edge for a fresh calm, then run straight in.
            Vector3 edge = c + (dist > 0.01f ? d / dist : Vector3.Right) * (room.WindOuter + 0.9f);
            float cycle = PuzzleGen.WindCycle(room, i, map.Elapsed);
            float need = (room.WindOuter - room.WindInner + 1f) / PuzzleSolver.WalkSpeed * 1.3f;
            bool go = cycle < room.Calm - need;
            if (me.DistanceTo(edge) > 0.8f && !go) return Toward(me, edge, 0.3f);
            return go ? Toward(me, c, 0.1f) : Vector3.Zero;
        }
        return Vector3.Zero;
    }

    // ---------------------------------------------------------------- twin

    private Vector3 TwinThink(PuzzleMap map, RoomPlan room, Vector3 me)
    {
        PuzzleState st = map.State;
        bool assist = st.Get(room, PuzzleState.RAssist) != 0;
        int mine;
        if (!assist && Net.Coop.InRun && Net.NetSession.Instance is { } net)
        {
            // Two of us: the lower slot takes plate A, the next one plate B.
            int rank = 0;
            foreach (Net.CoopPeer p in Net.Coop.Roster) if (p.Slot < net.LocalSlot) rank++;
            mine = rank % 2;
        }
        else mine = st.TwinHeld(room, 0, map.Elapsed) ? 1 : 0;
        int cell = mine == 0 ? room.PlateA : room.PlateB;
        if (room.CellAt(me) == cell) return Toward(me, room.World(cell), 0.05f);
        return WalkGrid(room, me, cell, false);
    }

    // ------------------------------------------------------------- walking

    /// <summary>Between rooms: through the doors in order towards <paramref name="target"/>.</summary>
    private static Vector3 TowardRoom(PuzzleMap map, Vector3 me, int r, int target)
    {
        PuzzlePlan plan = map.Plan;
        if (r < 0) return Toward(me, plan.Rooms[target].Centre, 0.5f);
        int step = r < target ? r : r - 1;
        int into = r < target ? r + 1 : r - 1;
        Vector3 door = plan.DoorBetween(step);
        RoomPlan here = plan.Rooms[r];
        // Line up with the doorway first, then go through it.
        Vector3 inside = door - (door - here.Centre).Normalized() * 2.2f;
        Vector3 beyond = door + (plan.Rooms[into].Centre - door).Normalized() * 2.5f;
        int doorCell = here.CellAt(inside);
        Vector3 flat = me - door;
        flat.Y = 0f;
        if (flat.Length() > 2.6f && doorCell >= 0 && here.CellAt(me) != doorCell)
            return WalkGrid(here, me, doorCell, false);
        return Toward(me, beyond, 0.3f);
    }

    private static Vector3 Toward(Vector3 me, Vector3 at, float stop)
    {
        Vector3 d = at - me;
        d.Y = 0f;
        float len = d.Length();
        if (len <= stop) return Vector3.Zero;
        return len < 1f ? d : d / len;
    }

    /// <summary>The next step along the room's grid to <paramref name="target"/>,
    /// around pillars, crates and pieces.</summary>
    private static Vector3 WalkGrid(RoomPlan room, Vector3 me, int target, bool stopNextTo, int[]? crates = null)
    {
        int from = room.CellAt(me);
        if (from < 0 || target < 0) return Toward(me, target >= 0 ? room.World(target) : room.Centre, 0.2f);
        bool Blocked(int c)
        {
            if (PuzzleSolver.Solid(room, c)) return true;
            if (crates is not null && Array.IndexOf(crates, c) >= 0) return true;
            Vector2 l = RoomPlan.Local(c);
            foreach (Vector2 p in room.Levers) if (p.DistanceTo(l) < 1.1f) return true;
            foreach (Vector2 p in room.Lamps) if (p.DistanceTo(l) < 1.1f) return true;
            foreach (Vector2 p in room.Bells) if (p.DistanceTo(l) < 1.1f) return true;
            return room.Kind == RoomKind.Runes && l.Length() < 1.1f;
        }
        List<int>? path = PuzzleSolver.Path(room, from, target, c => Blocked(c) && c != target);
        if (path is null) return Toward(me, room.World(target), 0.2f);
        if (stopNextTo && path.Count > 0) path.RemoveAt(path.Count - 1);
        if (path.Count == 0) return Toward(me, room.World(stopNextTo ? from : target), 0.1f);
        // Aim at the first cell, or the second once close to the first.
        Vector3 next = room.World(path[0]);
        if (path.Count > 1 && me.DistanceTo(next) < 0.7f) next = room.World(path[1]);
        return Toward(me, next, 0.05f);
    }
}
