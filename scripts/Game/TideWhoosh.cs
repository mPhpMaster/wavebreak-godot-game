using System;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Meta;

namespace Wavebreak.Game;

/// <summary>
/// The Tide Totem's "whoosh": two rings of water racing out along the ground
/// to 7 m and a twisting spout round the totem, 0.8 s in all. Small on
/// purpose: it stays on the ground and round the totem, never between the
/// camera and the fight, and every part fades through
/// <see cref="GraphicsSettings.FxFade"/> (the "Weapon effects opacity"
/// option), so turning effects down turns this down too.
/// </summary>
public sealed partial class TideWhoosh : Node3D
{
    public const float Life = 0.8f;
    public const float RingReach = 7f;

    private MeshInstance3D _ringA = null!, _ringB = null!, _spout = null!;
    private float _age;

    public static TideWhoosh Play(Node parent, Vector3 at)
    {
        var fx = new TideWhoosh { Name = "TideWhoosh" };
        parent.AddChild(fx);
        fx.GlobalPosition = at;
        return fx;
    }

    public override void _Ready()
    {
        var water = new StandardMaterial3D
        {
            AlbedoColor = TideTotem.TotemTint,
            EmissionEnabled = true,
            Emission = TideTotem.TotemTint,
            EmissionEnergyMultiplier = 1.8f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        var foam = (StandardMaterial3D)water.Duplicate();
        foam.AlbedoColor = new Color(0.85f, 1f, 1f);
        foam.Emission = new Color(0.85f, 1f, 1f);

        _ringA = Ring(water);
        _ringB = Ring(foam);
        _spout = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 1.3f, BottomRadius = 0.7f, Height = 3.6f, RadialSegments = 18, Rings = 1, CapTop = false, CapBottom = false },
            Position = new Vector3(0f, 1.8f, 0f),
            MaterialOverride = water,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_spout);
        Step(0f);
    }

    private MeshInstance3D Ring(Material mat)
    {
        var mi = new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 0.92f, OuterRadius = 1f, Rings = 40, RingSegments = 4 },
            Position = new Vector3(0f, 0.12f, 0f),
            MaterialOverride = mat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(mi);
        return mi;
    }

    public override void _Process(double delta)
    {
        _age += (float)delta;
        if (_age >= Life) { QueueFree(); return; }
        Step(_age / Life);
    }

    private void Step(float t)
    {
        // Ring A leads, ring B (foam) follows a beat later; both ease out.
        float a = 1f - MathF.Pow(1f - t, 3f);
        float tb = Mathf.Clamp((t - 0.15f) / 0.85f, 0f, 1f);
        float b = 1f - MathF.Pow(1f - tb, 3f);
        float ra = 1f + (RingReach - 1f) * a, rb = 1f + (RingReach * 0.8f - 1f) * b;
        _ringA.Scale = new Vector3(ra, 0.35f, ra);
        _ringB.Scale = new Vector3(rb, 0.25f, rb);
        _ringA.Transparency = GraphicsSettings.FxFade(0.35f + 0.65f * t);
        _ringB.Transparency = GraphicsSettings.FxFade(tb <= 0f ? 1f : 0.45f + 0.55f * tb);

        // Spout: rises and twists in the first half, then thins away.
        float rise = Mathf.Clamp(t / 0.4f, 0f, 1f);
        _spout.Scale = new Vector3(1f - 0.35f * t, Mathf.Max(0.05f, rise), 1f - 0.35f * t);
        _spout.Position = new Vector3(0f, 1.8f * rise, 0f);
        _spout.Rotation = new Vector3(0f, t * 9f, 0f);
        _spout.Transparency = GraphicsSettings.FxFade(0.45f + 0.55f * t * t);
    }
}
