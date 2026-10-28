using Godot;
using Wavebreak.Game;

namespace Wavebreak.Visuals;

public static class UnderwaterDressing
{
    public static Node3D Build(float radius, int seed)
    {
        var root = new Node3D { Name = "SubmergedAtmosphere" };
        var rng = new RandomNumberGenerator { Seed = (ulong)seed ^ 0xBABB1EUL };
        var bubbles = Bubbles(radius, rng);
        root.AddChild(bubbles);
        root.AddChild(new ReefWaterSurface(radius));
        var shaftMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.35f, 0.85f, 0.82f, 0.055f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            NoDepthTest = false,
        };
        for (int i = 0; i < 12; i++)
        {
            float a = i * Mathf.Tau / 12f;
            float d = i < 4 ? 24f : rng.RandfRange(45f, radius - 30f);
            root.AddChild(new MeshInstance3D
            {
                Name = "FilteredSunShaft",
                Mesh = new CylinderMesh { TopRadius = 1.3f, BottomRadius = 4.5f, Height = 90f, RadialSegments = 12 },
                Position = new Vector3(Mathf.Cos(a) * d, WaterSurfaceHeight.BaseHeight - 45f, Mathf.Sin(a) * d),
                Rotation = new Vector3(0.12f, 0f, -0.2f),
                MaterialOverride = shaftMaterial,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }
        return root;
    }

    private static MultiMeshInstance3D Bubbles(float radius, RandomNumberGenerator rng)
    {
        var material = new ShaderMaterial
        {
            Shader = new Shader
            {
                Code = """
                    shader_type spatial;
                    render_mode unshaded, shadows_disabled, cull_disabled;
                    uniform float water_time = 0.0;
                    varying vec3 world;
                    void vertex() {
                        float height = INSTANCE_CUSTOM.g * 128.0;
                        vec3 offset = vec3(sin(TIME + INSTANCE_CUSTOM.r * 6.28) * 0.35,
                            mod(TIME * (0.8 + INSTANCE_CUSTOM.b) + INSTANCE_CUSTOM.r * height, height),
                            cos(TIME * 0.8 + INSTANCE_CUSTOM.b * 5.0) * 0.2);
                        vec3 center = (MODEL_MATRIX * vec4(offset, 1.0)).xyz;
                        float scale = length(MODEL_MATRIX[0].xyz);
                        world = center + (INV_VIEW_MATRIX[0].xyz * VERTEX.x + INV_VIEW_MATRIX[1].xyz * VERTEX.y) * scale;
                        POSITION = PROJECTION_MATRIX * VIEW_MATRIX * vec4(world, 1.0);
                    }
                    void fragment() {
                        float line = 70.0 + 1.2 * sin(world.x * 0.13 + water_time * 0.9)
                            + 0.75 * cos(world.z * 0.17 - water_time * 0.8)
                            + 0.35 * sin((world.x + world.z) * 0.25 + water_time * 1.3);
                        if (world.y > line - 0.25) discard;
                        vec2 disk = UV * 2.0 - 1.0;
                        float radius = dot(disk, disk);
                        if (radius > 1.0) discard;
                        float rim = smoothstep(0.35, 1.0, radius);
                        ALBEDO = vec3(0.55, 0.95, 1.0);
                        EMISSION = vec3(0.12, 0.38, 0.42) * rim;
                        ALPHA = 0.12 + rim * 0.65;
                    }
                    """,
            },
        };
        var batch = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseCustomData = true,
            Mesh = new QuadMesh { Size = Vector2.One * .44f },
            InstanceCount = 1000,
        };
        for (int i = 0; i < batch.InstanceCount; i++)
        {
            int stream = i / 25;
            float angle = stream * 2.39996f;
            float distance = stream < 8 ? 8f + stream * 2f : rng.RandfRange(25f, radius - 28f);
            var at = new Vector3(Mathf.Cos(angle) * distance + rng.RandfRange(-1.3f, 1.3f),
                0f, Mathf.Sin(angle) * distance + rng.RandfRange(-1.3f, 1.3f));
            at.Y = UnderwaterTerrain.Height(at) + 0.4f;
            float height = Mathf.Max(3f, UnderwaterTerrain.SurfaceHeight - at.Y);
            float scale = rng.RandfRange(0.45f, 1.4f);
            batch.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(Vector3.One * scale), at));
            batch.SetInstanceCustomData(i, new Color(rng.Randf(), height / 128f, rng.Randf()));
        }
        var instance = new MultiMeshInstance3D
        {
            Name = "VisibleBubbleStreams", Multimesh = batch, MaterialOverride = material,
            ExtraCullMargin = 65f,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        instance.SetMeta("water_material", material);
        return instance;
    }
}
