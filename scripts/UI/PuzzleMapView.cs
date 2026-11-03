using System;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Game;
using Wavebreak.Game.Puzzle;

namespace Wavebreak.UI;

/// <summary>
/// A puzzle map on the minimap and the Tab big map (the fog of war is off in
/// puzzle maps; the rooms are the map). Only what the team has found is
/// drawn: the rooms someone has entered, the corridor out of a room once its
/// door is open, a shut door as a bar across its doorway, and the open EXIT
/// and SKIP portals. Room outlines are the hall walls, with the puzzle grid
/// inlaid and the room kind's mark (<see cref="PuzzleKind.Glyph"/>) in the middle;
/// solved rooms are tinted.
/// </summary>
public static class PuzzleMapView
{
    private static readonly Color Floor = new(0.27f, 0.32f, 0.38f, 0.95f);
    private static readonly Color FloorHere = new(0.38f, 0.46f, 0.52f, 0.97f);
    private static readonly Color FloorSolved = new(0.22f, 0.42f, 0.42f, 0.95f);
    private static readonly Color Grid = new(1f, 1f, 1f, 0.14f);
    private static readonly Color Wall = new(0.86f, 0.90f, 0.92f, 0.9f);
    private static readonly Color DoorShut = new(0.95f, 0.66f, 0.30f);
    private static readonly Color Exit = new(0.55f, 1.00f, 0.92f);
    private static readonly Color Skip = new(1.00f, 0.72f, 0.45f);
    private static readonly Color Glyph = new(1f, 0.92f, 0.70f, 0.95f);

    /// <summary>The puzzle map standing now, or null.</summary>
    public static PuzzleMap? Current => Main.Instance?.Stages is { InPuzzle: true, Puzzle: { } map } ? map : null;

    /// <summary>Has the team been in room <paramref name="i"/> (the start room always)?</summary>
    public static bool Found(PuzzleMap map, int i)
        => i == 0 || map.State.Get(map.Plan.Rooms[i], PuzzleState.REntered) != 0;

    /// <summary>The world rectangle (x, z) every room of the plan fits in.</summary>
    public static Rect2 Extent(PuzzlePlan plan)
    {
        float h = RoomPlan.Half + RoomPlan.WallThick;
        var r = new Rect2(plan.Rooms[0].Centre.X - h, plan.Rooms[0].Centre.Z - h, h * 2f, h * 2f);
        foreach (RoomPlan room in plan.Rooms) r = r.Merge(new Rect2(room.Centre.X - h, room.Centre.Z - h, h * 2f, h * 2f));
        return r;
    }

    /// <summary>Draw the found rooms. <paramref name="toScreen"/> maps a world
    /// point to the canvas; <paramref name="clip"/> (a convex polygon in canvas
    /// space, the minimap's disc) keeps the drawing inside it.</summary>
    public static void Draw(CanvasItem ci, PuzzleMap map, Func<Vector3, Vector2> toScreen, Vector2[]? clip, float line)
    {
        PuzzlePlan plan = map.Plan;
        int here = Player.Instance is { } me && GodotObject.IsInstanceValid(me) ? plan.NearestRoom(me.GlobalPosition) : -1;
        float h = RoomPlan.Half, g = RoomPlan.N * RoomPlan.Cell * 0.5f;
        for (int i = 0; i < plan.Rooms.Count; i++)
        {
            if (!Found(map, i)) continue;
            RoomPlan room = plan.Rooms[i];
            // The corridor out first, under the rooms.
            if (i + 1 < plan.Rooms.Count && map.DoorIsOpen(i))
            {
                var dir = new Vector3(RoomPlan.Dx[room.ExitSide], 0f, RoomPlan.Dz[room.ExitSide]);
                var across = new Vector3(dir.Z, 0f, dir.X) * (RoomPlan.CorridorWidth * 0.5f);
                Vector3 a = room.Centre + dir * h, b = room.Centre + dir * (RoomPlan.Pitch - h);
                Quad(ci, new[] { toScreen(a - across), toScreen(b - across), toScreen(b + across), toScreen(a + across) }, Floor, Wall, clip, line);
            }
            Color fill = i == here ? FloorHere : map.State.Solved(i) && room.Kind != RoomKind.Start ? FloorSolved : Floor;
            Quad(ci, Square(room.Centre, h, toScreen), fill, Wall, clip, line * 1.4f);
            if (room.Kind is not (RoomKind.Start or RoomKind.Reward))
                Quad(ci, Square(room.Centre, g, toScreen), Colors.Transparent, Grid, clip, line);
            // The kind's mark in the middle of the room, once it is big enough to read.
            if (room.Def is { } kind)
            {
                Vector2 at = toScreen(room.Centre);
                float size = (toScreen(room.Centre + new Vector3(3.2f, 0f, 0f)) - at).Length();
                if (size >= 4f && (clip is null || Geometry2D.IsPointInPolygon(at, clip)))
                    kind.Glyph(ci, at, size, map.State.Solved(i) ? Exit : Glyph);
            }
            // A shut door: a bar across the doorway.
            if (i + 1 < plan.Rooms.Count && !map.DoorIsOpen(i))
            {
                var dir = new Vector3(RoomPlan.Dx[room.ExitSide], 0f, RoomPlan.Dz[room.ExitSide]);
                var across = new Vector3(dir.Z, 0f, dir.X) * (RoomPlan.CorridorWidth * 0.5f);
                Vector3 d = room.Centre + dir * h;
                Segment(ci, toScreen(d - across), toScreen(d + across), DoorShut, line * 2.6f, clip);
            }
        }
        if (map.ExitOpen && map.ExitTarget is { } exit) Marker(ci, toScreen(exit), Exit, line, clip);
        if (map.SkipPosition is { } skip) Marker(ci, toScreen(skip), Skip, line, clip);
    }

    private static Vector2[] Square(Vector3 c, float h, Func<Vector3, Vector2> toScreen) => new[]
    {
        toScreen(c + new Vector3(-h, 0f, -h)), toScreen(c + new Vector3(h, 0f, -h)),
        toScreen(c + new Vector3(h, 0f, h)), toScreen(c + new Vector3(-h, 0f, h)),
    };

    private static void Quad(CanvasItem ci, Vector2[] poly, Color fill, Color edge, Vector2[]? clip, float line)
    {
        if (clip is null)
        {
            if (fill.A > 0f) ci.DrawColoredPolygon(poly, fill);
            ci.DrawPolyline(new[] { poly[0], poly[1], poly[2], poly[3], poly[0] }, edge, line, true);
            return;
        }
        foreach (Vector2[] part in Geometry2D.IntersectPolygons(poly, clip))
        {
            if (part.Length < 3) continue;
            if (fill.A > 0f) ci.DrawColoredPolygon(part, fill);
        }
        // Edges clipped one by one, so the disc's rim is not drawn as a wall.
        for (int k = 0; k < 4; k++) Segment(ci, poly[k], poly[(k + 1) % 4], edge, line, clip);
    }

    private static void Segment(CanvasItem ci, Vector2 a, Vector2 b, Color c, float width, Vector2[]? clip)
    {
        if (clip is null)
        {
            ci.DrawLine(a, b, c, width, true);
            return;
        }
        foreach (Vector2[] part in Geometry2D.IntersectPolylineWithPolygon(new[] { a, b }, clip))
            if (part.Length >= 2) ci.DrawLine(part[0], part[^1], c, width, true);
    }

    private static void Marker(CanvasItem ci, Vector2 at, Color c, float line, Vector2[]? clip)
    {
        if (clip is not null && !Geometry2D.IsPointInPolygon(at, clip)) return;
        ci.DrawCircle(at, 3.4f * line + 2f, new Color(0.04f, 0.05f, 0.08f));
        ci.DrawArc(at, 3.4f * line, 0f, Mathf.Tau, 20, c, line * 1.2f, true);
        ci.DrawCircle(at, line * 1.1f, c);
    }
}
