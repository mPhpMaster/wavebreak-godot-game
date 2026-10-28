using Godot;

namespace Wavebreak.Game;

internal static class ReefWreckDetails
{
    public static void Build(Node3D parent, Vector3 origin, Material wood, Material dark)
    {
        void Beam(Vector3 from, Vector3 to, float width, Material material)
        {
            Vector3 delta = to - from;
            var beam = new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = width, BottomRadius = width, Height = delta.Length(), RadialSegments = 6 },
                Position = origin + (from + to) * .5f, MaterialOverride = material,
            };
            beam.Quaternion = new Quaternion(Vector3.Up, delta.Normalized());
            parent.AddChild(beam);
        }
        // Curved open ribs and longitudinal rails give the wreck a boat silhouette.
        for (int rib = 0; rib < 9; rib++)
        {
            float z = -14 + rib * 3.5f;
            float width = Mathf.Lerp(2, 5.8f, Mathf.Sin((rib + 1) / 10f * Mathf.Pi));
            foreach (float side in new[] { -1f, 1f })
            {
                Beam(new Vector3(0, -.8f, z), new Vector3(side * width * .65f, .7f, z), .2f, dark);
                Beam(new Vector3(side * width * .65f, .7f, z), new Vector3(side * width, 3.3f, z), .18f, wood);
                if (rib < 8 && rib != 4)
                {
                    float nextWidth = Mathf.Lerp(2, 5.8f, Mathf.Sin((rib + 2) / 10f * Mathf.Pi));
                    Beam(new Vector3(side * width, 3.3f, z), new Vector3(side * nextWidth, 3.3f, z + 3.5f), .16f, wood);
                }
            }
        }
        Beam(new Vector3(0, 1, -14), new Vector3(0, 4, -22), .22f, wood);
        Beam(new Vector3(0, -.5f, 14), new Vector3(0, -3, 17), .35f, dark);
        parent.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(5, 3.2f, 4) }, Position = origin + new Vector3(0, 2, 11), MaterialOverride = wood,
        });
        parent.AddChild(new MeshInstance3D
        {
            Mesh = new PrismMesh { Size = new Vector3(5.6f, 1.5f, 4.6f) }, Position = origin + new Vector3(0, 4.3f, 11), MaterialOverride = dark,
        });
        var cabinCollision = new StaticBody3D { Position = origin + new Vector3(0, 2, 11), CollisionLayer = 1 };
        cabinCollision.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(5, 3.2f, 4) } });
        parent.AddChild(cabinCollision);
        Material glass = Arena.Flat(new Color(.24f, .65f, .69f), .3f);
        foreach (float x in new[] { -1.4f, 1.4f })
            parent.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(1, 1, .08f) }, Position = origin + new Vector3(x, 2.2f, 8.96f), MaterialOverride = glass,
            });
    }
}

