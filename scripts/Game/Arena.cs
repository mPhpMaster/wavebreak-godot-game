using Godot;

namespace Wavebreak.Game;

/// <summary>
/// The playfield: ground, walls, lighting and scatter, all built from primitives.
///
/// Generated in code rather than authored as a scene because at this stage the
/// arena's job is to be legible, not pretty — a flat lit disc with a hard
/// boundary and enough scatter to judge your own speed against. Swapping in a
/// real level later means replacing this one node; nothing else refers to its
/// contents.
/// </summary>
public sealed partial class Arena : Node3D
{
    /// <summary>Play radius. The wall sits here and the director spawns inside it.</summary>
    public const float Radius = 60f;

    private static readonly Color Ground = new(0.16f, 0.18f, 0.22f);
    private static readonly Color Grid = new(0.24f, 0.27f, 0.33f);
    private static readonly Color Wall = new(0.35f, 0.30f, 0.45f);

    public override void _Ready()
    {
        Name = "Arena";
        BuildGround();
        BuildWall();
        BuildScatter();
        BuildLighting();
    }

    private void BuildGround()
    {
        var mesh = new CylinderMesh
        {
            TopRadius = Radius,
            BottomRadius = Radius,
            Height = 1f,
            RadialSegments = 64,
        };
        var mi = new MeshInstance3D
        {
            Mesh = mesh,
            Position = new Vector3(0f, -0.5f, 0f),
            MaterialOverride = Flat(Ground),
        };
        AddChild(mi);

        var body = new StaticBody3D();
        var shape = new CollisionShape3D
        {
            Shape = new CylinderShape3D { Radius = Radius, Height = 1f },
            Position = new Vector3(0f, -0.5f, 0f),
        };
        body.AddChild(shape);
        AddChild(body);

        // Concentric rings. Without something static on the floor there is no
        // sense of speed at all -- the player reads as stationary while the world
        // slides, which makes movement feel weightless.
        for (int r = 10; r < (int)Radius; r += 10)
        {
            var ring = new MeshInstance3D
            {
                Mesh = new TorusMesh { InnerRadius = r - 0.12f, OuterRadius = r + 0.12f, RingSegments = 64 },
                Position = new Vector3(0f, 0.01f, 0f),
                MaterialOverride = Flat(Grid),
            };
            AddChild(ring);
        }
    }

    private void BuildWall()
    {
        const float height = 4f;
        var mi = new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = Radius - 0.5f, OuterRadius = Radius + 0.5f, RingSegments = 64 },
            Position = new Vector3(0f, height * 0.25f, 0f),
            Scale = new Vector3(1f, height, 1f),
            MaterialOverride = Flat(Wall, emission: 0.25f),
        };
        AddChild(mi);

        // A ring of thin static bodies is far cheaper than a mesh collider and
        // accurate enough to keep bodies inside a circle.
        var body = new StaticBody3D();
        const int segments = 48;
        for (int i = 0; i < segments; i++)
        {
            float a = Mathf.Tau * i / segments;
            var col = new CollisionShape3D
            {
                Shape = new BoxShape3D { Size = new Vector3(Mathf.Tau * Radius / segments + 1f, height, 1f) },
                Position = new Vector3(Mathf.Cos(a) * Radius, height * 0.5f, Mathf.Sin(a) * Radius),
                Rotation = new Vector3(0f, -a, 0f),
            };
            body.AddChild(col);
        }
        AddChild(body);
    }

    private void BuildScatter()
    {
        var rng = new RandomNumberGenerator { Seed = 20260815 };
        for (int i = 0; i < 90; i++)
        {
            float a = rng.RandfRange(0f, Mathf.Tau);
            float d = Mathf.Sqrt(rng.Randf()) * (Radius - 6f);
            float h = rng.RandfRange(0.6f, 2.4f);
            float w = rng.RandfRange(0.5f, 1.4f);
            var mi = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(w, h, w) },
                Position = new Vector3(Mathf.Cos(a) * d, h * 0.5f, Mathf.Sin(a) * d),
                Rotation = new Vector3(0f, rng.RandfRange(0f, Mathf.Tau), 0f),
                MaterialOverride = Flat(Grid.Lerp(Wall, rng.Randf())),
            };
            AddChild(mi);
        }
    }

    private void BuildLighting()
    {
        var sun = new DirectionalLight3D
        {
            Rotation = new Vector3(Mathf.DegToRad(-55f), Mathf.DegToRad(35f), 0f),
            LightEnergy = 1.1f,
            ShadowEnabled = true,
        };
        AddChild(sun);

        var env = new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Sky,
                Sky = new Sky { SkyMaterial = new ProceduralSkyMaterial
                {
                    SkyTopColor = new Color(0.10f, 0.11f, 0.18f),
                    SkyHorizonColor = new Color(0.28f, 0.24f, 0.36f),
                    GroundHorizonColor = new Color(0.12f, 0.12f, 0.16f),
                    GroundBottomColor = new Color(0.06f, 0.06f, 0.09f),
                } },
                AmbientLightSource = Godot.Environment.AmbientSource.Sky,
                AmbientLightEnergy = 0.6f,
                GlowEnabled = true,
                GlowIntensity = 0.5f,
            },
        };
        AddChild(env);
    }

    /// <summary>Unshaded-ish flat material. Emission is used sparingly to make
    /// gameplay-relevant things read against the scenery.</summary>
    public static StandardMaterial3D Flat(Color color, float emission = 0f)
    {
        var m = new StandardMaterial3D { AlbedoColor = color, Roughness = 0.85f, Metallic = 0f };
        if (emission > 0f)
        {
            m.EmissionEnabled = true;
            m.Emission = color;
            m.EmissionEnergyMultiplier = emission;
        }
        return m;
    }

    /// <summary>Clamp a position inside the arena, used by spawning and knockback.</summary>
    public static Vector3 Clamp(Vector3 p, float margin = 2f)
    {
        var flat = new Vector2(p.X, p.Z);
        float max = Radius - margin;
        if (flat.Length() > max) flat = flat.Normalized() * max;
        return new Vector3(flat.X, p.Y, flat.Y);
    }
}
