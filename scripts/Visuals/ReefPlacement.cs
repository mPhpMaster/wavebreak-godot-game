using Godot;
using Wavebreak.Game;

namespace Wavebreak.Visuals;

public static class ReefPlacement
{
    public static readonly Vector3[] Ledges = new Vector3[4];

    public static void BuildLedges(Node3D parent, float radius)
    {
        for (int i = 0; i < Ledges.Length; i++)
        {
            float angle = 1.2f + i * 1.2f;
            Vector3 wall = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * (radius - 18f);
            Vector3 at = wall.Normalized() * (radius - 28f);
            float top = Mathf.Min(55f, UnderwaterTerrain.Height(wall) + 3f);
            float low = UnderwaterTerrain.Height(at) - 2;
            Ledges[i] = at with { Y = top };
            var body = new StaticBody3D { Name = $"CliffLedge{i}", Position = at with { Y = (low + top) * 0.5f },
                Rotation = new Vector3(0, -angle, 0), CollisionLayer = 1 };
            var mesh = new CylinderMesh { TopRadius = 12, BottomRadius = 14, Height = top - low, RadialSegments = 7 };
            body.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = Arena.Flat(new Color(0.16f, 0.29f, 0.3f)) });
            body.AddChild(new CollisionShape3D { Shape = mesh.CreateConvexShape() });
            parent.AddChild(body);
        }
    }

    public static void Ground(Node3D actor, float radius)
    {
        if (!Arena.Current.Underwater) return;
        Vector3 at = actor.GlobalPosition;
        float low = float.MaxValue, high = float.MinValue;
        for (int i = 0; i < 12; i++)
        {
            float angle = i * Mathf.Tau / 12;
            float y = UnderwaterTerrain.Height(at + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius);
            low = Mathf.Min(low, y); high = Mathf.Max(high, y);
        }
        actor.GlobalPosition = at with { Y = high + 0.04f };
        float height = high - low + 0.5f;
        var body = new StaticBody3D { Name = "AnchoredFoundation", Position = Vector3.Down * height * 0.5f, CollisionLayer = 1 };
        var size = new Vector3(radius * 2, height, radius * 2);
        body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = Arena.Flat(new Color(0.19f, 0.3f, 0.29f)) });
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        actor.AddChild(body);
    }

    public static float SupportBelow(Node3D owner, Vector3 at)
    {
        var query = PhysicsRayQueryParameters3D.Create(at + Vector3.Up * 0.3f, at + Vector3.Down * 180f, 1);
        var hit = owner.GetWorld3D().DirectSpaceState.IntersectRay(query);
        return hit.TryGetValue("position", out Variant p) ? p.AsVector3().Y : ReefCavePath.Floor(at);
    }
}
