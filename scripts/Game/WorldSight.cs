using Godot;

namespace Wavebreak.Game;

public static class WorldSight
{
    public static bool Clear(Node3D world, Vector3 from, Vector3 to)
    {
        if (!from.IsFinite() || !to.IsFinite() || !world.IsInsideTree()) return false;
        var query = PhysicsRayQueryParameters3D.Create(from, to, 1u);
        return world.GetWorld3D().DirectSpaceState.IntersectRay(query).Count == 0;
    }
}
