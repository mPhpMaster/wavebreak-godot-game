using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Data;

namespace Wavebreak.Game;

public static class CityRooftops
{
    private static Node3D? _owner;
    private static readonly List<CityRooftop> Roofs = new();
    public static int LastVisibilityQueries { get; private set; }
    public const int VisibilityBudget = 12;
    public static int Count => Roofs.Count;
    public static void Begin(Node3D owner) { _owner = owner; Roofs.Clear(); }
    public static void End(Node3D owner) { if (_owner == owner) { Roofs.Clear(); _owner = null; } }
    public static void Register(StaticBody3D body, Vector2 at, float width, float depth, float height)
        => Roofs.Add(new(body, new Vector3(at.X, 0f, at.Y), new Vector2(width, depth) * .5f, height));

    public static bool Find(Node3D world, Vector3 player, Vector3 forward, HookSide side, out Vector3 anchor, Vector3? otherAnchor = null)
    {
        anchor = Vector3.Zero; LastVisibilityQueries = 0;
        if (!Arena.IsCity || !player.IsFinite() || !forward.IsFinite()) return false;
        forward = (forward with { Y = 0f }).Normalized();
        if (forward.LengthSquared() < .5f) forward = Vector3.Forward;
        Vector3 right = forward.Cross(Vector3.Up);
        SpanCandidates candidates = new(stackalloc Vector3[VisibilityBudget], stackalloc float[VisibilityBudget]);
        foreach (CityRooftop roof in Roofs)
        {
            if (!GodotObject.IsInstanceValid(roof.Body) || !roof.Body.IsInsideTree()) continue;
            Vector3 center = roof.Body.GlobalPosition + Vector3.Up * roof.Height;
            Vector3 offset = center - player;
            if (offset.Dot(forward) < -2f) continue;
            Vector3 point = center;
            point.X = Mathf.Clamp(player.X, center.X - roof.Half.X + .025f, center.X + roof.Half.X - .025f);
            point.Z = Mathf.Clamp(player.Z, center.Z - roof.Half.Y + .025f, center.Z + roof.Half.Y - .025f);
            point.Y += .12f;
            float distance = player.DistanceTo(point);
            if (distance < 3f || distance > GrapplePhysics.Range) continue;
            float lateral = offset.Dot(right) * (side == HookSide.Right ? 1f : -1f);
            float wrongSide = lateral < -.5f ? 28f : 0f;
            float score = distance - Mathf.Clamp(point.Y - player.Y, 0f, 40f) * .65f + wrongSide;
            if (otherAnchor is { } occupied && point.DistanceSquaredTo(occupied) < 64f) score += 35f;
            candidates.Insert(point, score);
        }
        for (int i = 0; i < candidates.Count; i++)
        {
            LastVisibilityQueries++;
            if (!GrappleAnchors.Clear(world, player, candidates.Points[i])) continue;
            anchor = candidates.Points[i]; return true;
        }
        return false;
    }
}
