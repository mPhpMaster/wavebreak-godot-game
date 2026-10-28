using Godot;

namespace Wavebreak.Game;

public static class GrappleAnchors
{
    private static PhysicsRayQueryParameters3D? _query;
    private static Rid _excludedPlayer;
    private static bool _hasExclusion;
    public static int QueryCount { get; private set; }

    internal static void End()
    {
        _query?.Dispose(); _query = null; _hasExclusion = false;
    }
    public static bool Ray(Node3D world, Vector3 from, Vector3 direction, Vector3 player, out Vector3 anchor)
        => RayTarget(world, from, direction, player, out anchor, out _);

    public static bool RayTarget(Node3D world, Vector3 from, Vector3 direction, Vector3 player,
        out Vector3 anchor, out CollisionObject3D? target)
    {
        using var timing = Prof.Scope("City.anchorRay");
        anchor = Vector3.Zero; target = null;
        if (!Arena.IsCity || !from.IsFinite() || !direction.IsFinite() || direction.LengthSquared() < 0.9f) return false;
        var hit = world.GetWorld3D().DirectSpaceState.IntersectRay(
            Query(from, from + direction.Normalized() * 110f));
        if (hit.Count == 0 || hit["collider"].AsGodotObject() is not CollisionObject3D body
            || (body.CollisionLayer & (Arena.HookLayer | 64u)) == 0) return false;
        Vector3 point = hit["position"].AsVector3();
        Vector3 normal = hit["normal"].AsVector3();
        if (point.DistanceTo(player) > GrapplePhysics.Range || point.DistanceTo(player) < 2f) return false;
        anchor = point + normal * 0.12f;
        target = body;
        return Clear(world, player, anchor);
    }

    public static bool Valid(Node3D world, Vector3 player, Vector3 anchor)
    {
        if (!Arena.IsCity || !anchor.IsFinite() || anchor.DistanceTo(player) > GrapplePhysics.Range + 2f) return false;
        if (!Clear(world, player, anchor)) return false;
        return Probe(world, anchor, out _);
    }

    internal static bool ValidOnBody(Node3D world, Vector3 player, Vector3 anchor, Actors.Enemy body)
    {
        if (!Arena.IsCity || !anchor.IsFinite() || anchor.DistanceTo(player) > GrapplePhysics.Range + 2f
            || !body.IsAlive || !Probe(world, anchor, out CollisionObject3D? surface) || surface != body) return false;
        // Receiver interpolation may place the remote origin just behind the
        // attached body's surface. The verified target cannot occlude its own
        // cable; buildings and every other giant must still block the ray.
        using var query = PhysicsRayQueryParameters3D.Create(player, anchor, 1u | 64u);
        var excluded = new Godot.Collections.Array<Rid> { body.GetRid() };
        if (Actors.Player.Instance is { } local) excluded.Add(local.GetRid());
        query.Exclude = excluded;
        return world.GetWorld3D().DirectSpaceState.IntersectRay(query).Count == 0;
    }

    internal static bool Probe(Node3D world, Vector3 anchor, out CollisionObject3D? target)
    {
        target = null;
        // Probe across the surface independently of the cable's angle. Extending
        // the cable ray misses a wall when the player swings directly below it.
        for (int i = 0; i < 3; i++)
        {
            Vector3 axis = i == 0 ? Vector3.Right : i == 1 ? Vector3.Up : Vector3.Back;
            for (int sign = -1; sign <= 1; sign += 2)
            {
                var hit = world.GetWorld3D().DirectSpaceState.IntersectRay(
                    Query(anchor + axis * (0.25f * sign), anchor - axis * (0.25f * sign)));
                if (hit.Count > 0 && hit["collider"].AsGodotObject() is CollisionObject3D body
                    && (body.CollisionLayer & (Arena.HookLayer | 64u)) != 0) { target = body; return true; }
            }
        }
        return false;
    }

    internal static bool Clear(Node3D world, Vector3 from, Vector3 to)
        => world.GetWorld3D().DirectSpaceState.IntersectRay(Query(from, to)).Count == 0;

    private static PhysicsRayQueryParameters3D Query(Vector3 from, Vector3 to)
    {
        QueryCount++;
        if (!PerformanceScenario.Reference)
        {
            _query ??= PhysicsRayQueryParameters3D.Create(from, to, 1u | 64u);
            _query.From = from; _query.To = to;
            if (Actors.Player.Instance is { } active && (!_hasExclusion || !_excludedPlayer.Equals(active.GetRid())))
            {
                _excludedPlayer = active.GetRid(); _hasExclusion = true;
                var excluded = new Godot.Collections.Array<Rid> { _excludedPlayer };
                _query.Exclude = excluded;
            }
            return _query;
        }
        var query = PhysicsRayQueryParameters3D.Create(from, to, 1u | 64u);
        // The third-person camera ray crosses our own layer-1 capsule.
        if (Actors.Player.Instance is { } player)
            query.Exclude = new Godot.Collections.Array<Rid> { player.GetRid() };
        return query;
    }
}
