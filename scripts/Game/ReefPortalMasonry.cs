using Godot;

namespace Wavebreak.Game;

/// <summary>Faceted stone footings connect the mouth arch to its real seabed.</summary>
public static class ReefPortalMasonry
{
    public static int GroundContacts { get; private set; }

    public static void Build(Node3D parent, Vector3 center, Vector3 right, Vector3 up)
    {
        GroundContacts = 0;
        var body = new StaticBody3D { Name = "GroundJoinedCaveFootings", CollisionLayer = 1 };
        parent.AddChild(body);
        for (int i = 0; i < 15; i++)
        {
            float angle = i * Mathf.Tau / 15;
            Vector3 top = center + (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * 8.1f;
            if (top.Y > center.Y + 1f) continue;
            float floor = UnderwaterTerrain.Height(top) - .8f;
            float height = top.Y - floor;
            if (height <= 0) continue;
            var mesh = new CylinderMesh { TopRadius = 2.1f, BottomRadius = 3.4f, Height = height,
                RadialSegments = 7, Rings = 2 };
            var transform = new Transform3D(new Basis(Vector3.Up, i * .63f), top - Vector3.Up * (height * .5f));
            body.AddChild(new MeshInstance3D { Name = "ArchFooting" + i, Mesh = mesh, Transform = transform,
                MaterialOverride = Arena.Flat(new Color(.16f, .29f, .32f).Lightened(i % 3 * .025f)) });
            body.AddChild(new CollisionShape3D { Shape = mesh.CreateConvexShape(), Transform = transform });
            GroundContacts++;
        }
    }
}
