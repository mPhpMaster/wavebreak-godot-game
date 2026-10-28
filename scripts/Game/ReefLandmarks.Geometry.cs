using Godot;

namespace Wavebreak.Game;

public sealed partial class ReefLandmarks
{
    private Vector3 BuildWreck(Vector2 centre)
    {
        (float low, float high) = ReefSites.GroundRange(centre, 6f, 16f);
        var origin = new Vector3(centre.X, high + 0.2f, centre.Y);
        var wood = Arena.Flat(new Color(0.30f, 0.27f, 0.22f));
        var dark = Arena.Flat(new Color(0.15f, 0.20f, 0.21f));
        float keel = origin.Y - low + 2f;
        Solid(new PrismMesh { Size = new Vector3(12f, keel, 31f), LeftToRight = 0.5f },
            new Transform3D(new Basis(Vector3.Forward, Mathf.Pi), origin - Vector3.Up * keel * 0.5f), dark);
        // Open hold and breaks in both sides make the interior a real swim-through.
        Solid(new BoxMesh { Size = new Vector3(11f, 0.4f, 30f) }, new Transform3D(Basis.Identity, origin - Vector3.Up * 0.2f), wood);
        foreach (float side in new[] { -5.5f, 5.5f })
            foreach (float z in new[] { -10.5f, 10.5f })
                Solid(new BoxMesh { Size = new Vector3(0.8f, 3.5f, 9f) },
                    new Transform3D(new Basis(Vector3.Forward, side * -0.025f), origin + new Vector3(side, 1.5f, z)), wood);
        for (int i = 0; i < 7; i++)
        {
            float z = -13f + i * 4.3f;
            foreach (float x in new[] { -5f, 5f })
                Solid(new BoxMesh { Size = new Vector3(0.35f, 5f, 0.4f) },
                    new Transform3D(new Basis(Vector3.Forward, -x * 0.08f), origin + new Vector3(x, 2f, z)), dark);
        }
        Solid(new CylinderMesh { TopRadius = 0.18f, BottomRadius = 0.35f, Height = 14f, RadialSegments = 8 },
            new Transform3D(new Basis(Vector3.Forward, -0.28f), origin + new Vector3(-1.5f, 6.5f, 3f)), wood);
        AddChild(new MeshInstance3D
        {
            Mesh = new PrismMesh { Size = new Vector3(7f, 5.5f, 0.15f), LeftToRight = 0.1f },
            Position = origin + new Vector3(-0.7f, 9f, 3f), Rotation = new Vector3(0f, 0f, -0.28f),
            MaterialOverride = Arena.Flat(new Color(0.62f, 0.72f, 0.64f)),
        });
        ReefWreckDetails.Build(this, origin, wood, dark);
        Label("The Sunken Skipper", origin + Vector3.Up * 15f);
        return origin;
    }
}

