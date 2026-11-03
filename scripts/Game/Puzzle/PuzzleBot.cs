using System;
using System.Collections.Generic;
using Godot;

namespace Wavebreak.Game.Puzzle;

/// <summary>
/// What a <see cref="PuzzleKind.Bot"/> steers with: the autopilot's map and
/// clock, walking on the room's grid round pillars and pieces, and pressing
/// interact the way a player does (only when the HUD's focus is the piece it
/// means, once per state change). Owned by Game/Autopilot.Puzzle.cs.
/// </summary>
public sealed class PuzzleBot
{
    public PuzzleMap Map { get; private set; } = null!;
    public PuzzleState State => Map.State;
    /// <summary>The autopilot's clock (seconds).</summary>
    public float Clock { get; private set; }

    private float _actAt;
    private int _actVersion = -1;

    public void Begin(PuzzleMap map, float clock)
    {
        if (!ReferenceEquals(map, Map))
        {
            Map = map;
            _actAt = 0f;
            _actVersion = -1;
        }
        Clock = clock;
    }

    /// <summary>May the bot press now? One press per state change (and at
    /// most one every 0.45 s), so a round trip to the host never makes it
    /// press twice.</summary>
    public bool Ready()
    {
        if (Clock < _actAt) return false;
        if (State.Version == _actVersion && Clock < _actAt + 1.5f) return false;
        _actAt = Clock + 0.45f;
        _actVersion = State.Version;
        return true;
    }

    /// <summary>Press interact if the focus is this piece; true when it is.</summary>
    public bool PressIf(PuzzleAction action, int index, int room)
    {
        if (Map.Focus is not { } f || f.Action != action || f.Index != index || f.Room != room) return false;
        if (Ready()) Map.RequestInteract();
        return true;
    }

    /// <summary>Walk up to a piece and press interact once it is the focus.</summary>
    public Vector3 Use(RoomPlan room, Vector3 me, Vector3 piece, PuzzleAction action, int index, bool onTop = false)
    {
        if (PressIf(action, index, room.Index)) return Vector3.Zero;
        Vector3 d = piece - me;
        d.Y = 0f;
        if (onTop) return d.Length() < 0.4f ? Vector3.Zero : WalkGrid(room, me, room.CellAt(piece), false);
        if (d.Length() < 2.6f) return d.Normalized() * 0.6f;
        return WalkGrid(room, me, room.CellAt(piece), stopNextTo: true);
    }

    /// <summary>Not leading (co-op): stand clear in the strip beside the room's entrance.</summary>
    public static Vector3 Hold(RoomPlan room, Vector3 me)
    {
        int side = room.EntrySide < 0 ? 0 : room.EntrySide;
        Vector3 spot = room.Centre + new Vector3(RoomPlan.Dx[side], 0f, RoomPlan.Dz[side]) * 11.5f
                       - new Vector3(RoomPlan.Dz[side], 0f, RoomPlan.Dx[side]) * 3f;
        return Toward(me, spot, 0.8f);
    }

    public static Vector3 Toward(Vector3 me, Vector3 at, float stop)
    {
        Vector3 d = at - me;
        d.Y = 0f;
        float len = d.Length();
        if (len <= stop) return Vector3.Zero;
        return len < 1f ? d : d / len;
    }

    /// <summary>The next step along the room's grid to <paramref name="target"/>,
    /// around pillars, fixed pieces, spots and <paramref name="extra"/> cells.</summary>
    public static Vector3 WalkGrid(RoomPlan room, Vector3 me, int target, bool stopNextTo, Func<int, bool>? extra = null)
    {
        int from = room.CellAt(me);
        if (from < 0 || target < 0) return Toward(me, target >= 0 ? room.World(target) : room.Centre, 0.2f);
        bool Blocked(int c)
        {
            if (PuzzleSolver.Solid(room, c)) return true;
            if (extra is not null && extra(c)) return true;
            return room.NearSpot(RoomPlan.Local(c), 1.1f);
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

    /// <summary>Stand at <paramref name="stand"/> and walk into <paramref name="side"/>
    /// (pushing a crate or a stone): line up across the push, then lean in.</summary>
    public static Vector3 PushFrom(RoomPlan room, Vector3 me, int stand, int side, Func<int, bool>? extra = null)
    {
        var push = new Vector3(RoomPlan.Dx[side], 0f, RoomPlan.Dz[side]);
        if (stand != room.CellAt(me)) return WalkGrid(room, me, stand, false, extra);
        Vector3 off = me - room.World(stand);
        off.Y = 0f;
        Vector3 across = off - push * off.Dot(push);
        return across.Length() < 0.3f ? push : (-across).Normalized() * 0.7f + push * 0.1f;
    }
}
