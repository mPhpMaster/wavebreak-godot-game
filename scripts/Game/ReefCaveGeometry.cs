using Godot;

namespace Wavebreak.Game;

public static class ReefCaveGeometry
{
    public static void Build(Node3D parent)
    {
        using var timing = Prof.Scope("Startup.reefCave");
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        const int sides = 24;
        Vector3[] path = ReefCavePath.Points;
        var rights = new Vector3[path.Length];
        var ups = new Vector3[path.Length];
        Vector3 previous = Vector3.Forward;
        for (int i = 0; i < path.Length; i++)
        {
            Vector3 tangent = (path[Mathf.Min(i + 1, path.Length - 1)] - path[Mathf.Max(0, i - 1)]).Normalized();
            Vector3 right = (previous - tangent * previous.Dot(tangent)).Normalized();
            if (right.LengthSquared() < 0.5f) right = tangent.Cross(Vector3.Up).Normalized();
            rights[i] = right; ups[i] = right.Cross(tangent).Normalized(); previous = right;
        }
        Vector3 Ring(int i, int side)
        {
            Vector3 right = rights[i], up = ups[i];
            float angle = side * Mathf.Tau / sides;
            float radius = ReefCavePath.Width(i) + 0.15f * Mathf.Sin(side * 1.7f + i * 0.3f);
            return path[i] + (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * radius;
        }
        void Vertex(Vector3 p) { tool.SetUV(new Vector2(p.X + p.Z, p.Y) * 0.1f); tool.AddVertex(p); }
        for (int i = 0; i < path.Length - 1; i++)
            for (int s = 0; s < sides; s++)
            {
                Vector3 a = Ring(i, s), b = Ring(i + 1, s), c = Ring(i, (s + 1) % sides), d = Ring(i + 1, (s + 1) % sides);
                Vertex(a); Vertex(c); Vertex(b); Vertex(b); Vertex(c); Vertex(d);
            }
        tool.GenerateNormals();
        ArrayMesh mesh = tool.Commit();
        var material = Game.Arena.Flat(new Color(0.25f, 0.39f, 0.42f), 0.15f);
        material.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        parent.AddChild(new MeshInstance3D { Name = "WindingCliffCave", Mesh = mesh, MaterialOverride = material });
        var body = new StaticBody3D { CollisionLayer = 1, CollisionMask = 1 };
        var shape = mesh.CreateTrimeshShape();
        shape.BackfaceCollision = true;
        body.AddChild(new CollisionShape3D { Shape = shape });
        parent.AddChild(body);
        ReefCaveWayfinding.Build(parent, path);
        PortalRocks(parent, path[0], rights[0], ups[0], false);
        PortalRocks(parent, path[^1], rights[^1], ups[^1], true);
        var coral = Arena.Flat(new Color(0.38f, 0.85f, 0.95f), 1.4f);
        for (int i = 4; i < path.Length - 3; i += 5)
        {
            parent.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(1.3f, .12f, .18f) },
                Position = path[i] + new Vector3(i % 2 == 0 ? -3 : 3, -ReefCavePath.Width(i) + 1.3f, 0), MaterialOverride = coral });
            if (i % 2 == 0) parent.AddChild(new OmniLight3D { Position = path[i], LightColor = new Color(0.2f, 0.8f, 1),
                OmniRange = 15, LightEnergy = 0.65f, ShadowEnabled = false });
        }
    }

    private static void PortalRocks(Node3D parent, Vector3 center, Vector3 right, Vector3 up, bool exit)
    {
        if (!exit)
        {
            ReefPortalMasonry.Build(parent, center, right, up);
            parent.AddChild(new OmniLight3D { Name = "CaveMouthGlow", Position = center - right.Cross(up) * 2f,
                LightColor = new Color(.25f, .74f, .82f), LightEnergy = .65f, OmniRange = 15f, ShadowEnabled = false });
        }
        var body = new StaticBody3D { Name = exit ? "SeabedChimneyRim" : "CliffMouthRim", CollisionLayer = 1 };
        parent.AddChild(body);
        var rock = new SphereMesh { Radius = 1, Height = 2, RadialSegments = 7, Rings = 4 };
        for (int i = 0; i < 15; i++)
        {
            float angle = i * Mathf.Tau / 15;
            Vector3 at = center + (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * 8.1f;
            if (exit) at.Y = UnderwaterTerrain.Height(at) - 0.6f;
            var transform = new Transform3D(Basis.FromEuler(new Vector3(i * 0.17f, angle, 0.2f)).Scaled(
                exit ? new Vector3(2.5f, 1.5f, 2.5f) : new Vector3(3.1f, 2.8f, 3.2f)), at);
            body.AddChild(new MeshInstance3D { Mesh = rock, Transform = transform,
                MaterialOverride = Arena.Flat(new Color(0.16f, 0.29f, 0.32f).Lightened(i % 3 * 0.025f)) });
            body.AddChild(new CollisionShape3D { Shape = rock.CreateConvexShape(), Transform = transform });
        }
    }
}


