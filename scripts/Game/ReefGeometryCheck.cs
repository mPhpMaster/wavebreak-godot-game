using System;
using Godot;
using Wavebreak.Actors;

namespace Wavebreak.Game;

public static class ReefGeometryCheck
{
    public static void Run(Player player, Action<bool, string> check)
    {
        var points = new[] { Vector3.Zero, new Vector3(-48f, 0f, -15f),
            new Vector3(20f, 0f, -64f), new Vector3(63f, 0f, 36f) };
        float low = float.MaxValue, high = float.MinValue;
        foreach (Vector3 at in points)
        {
            var query = PhysicsRayQueryParameters3D.Create(at + Vector3.Up * 60f, at + Vector3.Down * 40f, 1u);
            query.Exclude = new Godot.Collections.Array<Rid> { player.GetRid() };
            var hit = player.GetWorld3D().DirectSpaceState.IntersectRay(query);
            bool exists = hit.TryGetValue("position", out Variant position);
            float y = exists ? position.AsVector3().Y : 1000f;
            check(exists && Mathf.Abs(y - UnderwaterTerrain.Height(at)) < 0.8f,
                $"terrain collision follows relief at {at.X:0},{at.Z:0}");
            if (exists)
            {
                check(((Vector3)hit["normal"]).Y > 0f, "terrain collision faces upward");
                low = Mathf.Min(low, y); high = Mathf.Max(high, y);
            }
        }
        check(low < -10f && high > 10f && high - low > 25f, "substantial valleys and mountains have real collision");
        bool cliffs = true;
        for (int i = 0; i < 48; i++)
        {
            float a = i * Mathf.Tau / 48f, r = Arena.Current.InRadius - 2f;
            cliffs &= UnderwaterTerrain.Height(new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r)) > WaterSurfaceHeight.BaseHeight + 18f;
        }
        check(cliffs, "natural mountain boundary rises above the entire water column");
        Arena? arena = FindArena(player.GetTree().Root);
        int boxes = 0;
        if (arena?.GetNodeOrNull<StaticBody3D>("Solids") is { } body)
            foreach (Node child in body.GetChildren())
                if (child is CollisionShape3D { Shape: BoxShape3D }) boxes++;
        check(arena is not null && boxes == 0, "artificial box wall is absent from reef physics");
    }

    private static Arena? FindArena(Node node)
    {
        if (node is Arena arena) return arena;
        foreach (Node child in node.GetChildren())
            if (FindArena(child) is { } found) return found;
        return null;
    }
}
