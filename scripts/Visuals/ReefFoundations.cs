using Godot;
using Wavebreak.Game;

namespace Wavebreak.Visuals;

/// <summary>Footprint sampling prevents ruins from balancing on a single terrain point.</summary>
public static class ReefFoundations
{
    public static int SupportedStructures { get; private set; }
    public static int CulledStructures { get; private set; }
    public static void Ground(Node3D props)
    {
        SupportedStructures = CulledStructures = 0;
        var foundations = new StaticBody3D { Name = "GroundedReefFoundations", CollisionLayer = 1, CollisionMask = 1 };
        props.AddChild(foundations);
        foreach (Node child in props.GetChildren())
        {
            if (child is not MultiMeshInstance3D { Multimesh: { Mesh: { } mesh } batch }) continue;
            bool structure = child.Name.ToString().StartsWith("Arch") || child.Name.ToString().StartsWith("Pillar")
                || child.Name.ToString().StartsWith("Wall");
            bool rock = child.Name.ToString().StartsWith("Rock");
            Aabb bounds = mesh.GetAabb();
            for (int i = 0; i < batch.InstanceCount; i++)
            {
                Transform3D t = batch.GetInstanceTransform(i);
                Vector3 at = t.Origin;
                float high = UnderwaterTerrain.Height(at), low = high;
                if (structure || rock)
                    for (int x = 0; x < 2; x++)
                        for (int z = 0; z < 2; z++)
                        {
                            Vector3 local = new(bounds.Position.X + bounds.Size.X * x, 0f, bounds.Position.Z + bounds.Size.Z * z);
                            float height = UnderwaterTerrain.Height(at + t.Basis * local);
                            high = Mathf.Max(high, height); low = Mathf.Min(low, height);
                        }
                if (structure && high - low > 1.5f || !rock && high > WaterSurfaceHeight.BaseHeight - 4f)
                {
                    batch.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(Vector3.One * 0.001f), Vector3.Down * 1000f));
                    if (structure) CulledStructures++;
                    continue;
                }
                float ground = structure ? high : rock ? low - 0.15f : UnderwaterTerrain.Height(at);
                at.Y += ground - (t.Basis * new Vector3(0f, bounds.Position.Y, 0f)).Y;
                t.Origin = at;
                batch.SetInstanceTransform(i, t);
                if (!structure) continue;
                SupportedStructures++;
                Vector3 size = new(Mathf.Max(0.3f, bounds.Size.X * t.Basis.X.Length()), high - low + 0.5f,
                    Mathf.Max(0.3f, bounds.Size.Z * t.Basis.Z.Length()));
                Basis yaw = t.Basis.Orthonormalized();
                Vector3 centre = at + yaw * new Vector3(bounds.GetCenter().X * t.Basis.X.Length(), 0f,
                    bounds.GetCenter().Z * t.Basis.Z.Length());
                centre.Y = high - size.Y * 0.5f + 0.03f;
                var transform = new Transform3D(yaw, centre);
                foundations.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, Transform = transform,
                    MaterialOverride = Arena.Flat(new Color(0.18f, 0.31f, 0.32f)) });
                foundations.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size }, Transform = transform });
            }
        }
    }
}
