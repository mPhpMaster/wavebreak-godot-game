using Godot;
using Wavebreak.Actors;
using Wavebreak.Game;

namespace Wavebreak.Visuals;

public static class ReefActorSupport
{
    public static void Ground(Node3D actor, Vector3 half)
    {
        if (!Arena.Current.Underwater || Mathf.Abs(actor.GlobalPosition.Y - UnderwaterTerrain.Height(actor.GlobalPosition)) > 0.9f) return;
        float low = float.MaxValue, high = float.MinValue;
        var space = actor.GetWorld3D().DirectSpaceState;
        for (int x = -1; x <= 1; x += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 at = actor.GlobalPosition + actor.GlobalBasis * new Vector3(half.X * x, 0f, half.Z * z);
                var query = PhysicsRayQueryParameters3D.Create(at + Vector3.Up * 4f, at + Vector3.Down * 8f, 1u);
                if (Player.Instance is { } player) query.Exclude = new Godot.Collections.Array<Rid> { player.GetRid() };
                var hit = space.IntersectRay(query);
                if (!hit.TryGetValue("position", out Variant position)) return;
                low = Mathf.Min(low, position.AsVector3().Y); high = Mathf.Max(high, position.AsVector3().Y);
            }
        if (high - low > 1.5f) return;
        actor.GlobalPosition = actor.GlobalPosition with { Y = high + 0.03f };
        float height = high - low + 0.4f;
        var size = new Vector3(half.X * 2f, height, half.Z * 2f);
        var body = new StaticBody3D { Name = "SeabedSupport", Position = Vector3.Down * height * 0.5f };
        actor.AddChild(body);
        body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = Arena.Flat(new Color(0.20f, 0.32f, 0.32f)) });
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
    }
}
