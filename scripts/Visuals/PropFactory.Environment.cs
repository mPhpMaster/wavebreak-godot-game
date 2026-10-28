using System.Collections.Generic;
using Godot;

namespace Wavebreak.Visuals;

public static partial class PropFactory
{
    // ================================================================ floor

    /// <summary>
    /// A visual floor disc with the theme's ground shader. No collider: the map
    /// keeps its own (it already has one, and two would fight).
    /// </summary>
    public static MeshInstance3D BuildFloor(float radius, PropTheme theme)
    {
        var mesh = new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = 0.2f, RadialSegments = 64, Rings = 1 };
        return new MeshInstance3D
        {
            Name = "Floor",
            Mesh = mesh,
            Position = new Vector3(0f, -0.1f + 0.003f, 0f),
            MaterialOverride = FloorMaterial(theme),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    private static readonly Dictionary<string, ShaderMaterial> Floors = new();

    /// <summary>The theme's floor material, for maps that build their own ground
    /// geometry (ramps, platforms) and want it to match. World-space, so it
    /// tiles seamlessly across separate meshes.</summary>
    public static ShaderMaterial FloorMaterial(PropTheme t)
    {
        if (Floors.TryGetValue(t.Id, out ShaderMaterial? m) && GodotObject.IsInstanceValid(m)) return m;
        m = new ShaderMaterial { Shader = ToonMaterials.FloorShader };
        m.SetShaderParameter("color_a", t.FloorA);
        m.SetShaderParameter("color_b", t.FloorB);
        m.SetShaderParameter("color_patch", t.FloorPatch);
        m.SetShaderParameter("color_line", t.FloorLine);
        m.SetShaderParameter("patch_amount", t.PatchAmount);
        m.SetShaderParameter("line_strength", t.LineStrength);
        m.SetShaderParameter("slab_size", t.SlabSize);
        // Ember cracks glow; stone joints do not.
        m.SetShaderParameter("line_glow", t.Id == "ashlands" ? 0.9f : 0f);
        Floors[t.Id] = m;
        return m;
    }

    // ============================================================ sky & light

    /// <summary>Sky, ambient, fog, glow and tonemapping for the theme.</summary>
    public static WorldEnvironment MakeWorldEnvironment(PropTheme t) => new()
    {
        Name = "Environment",
        Environment = MakeEnvironment(t),
    };

    public static Godot.Environment MakeEnvironment(PropTheme t) => new()
    {
        BackgroundMode = Godot.Environment.BGMode.Sky,
        Sky = new Sky
        {
            SkyMaterial = new ProceduralSkyMaterial
            {
                SkyTopColor = t.SkyTop,
                SkyHorizonColor = t.SkyHorizon,
                GroundHorizonColor = t.GroundHorizon,
                GroundBottomColor = t.GroundHorizon.Darkened(0.4f),
                SunAngleMax = 25f,
                SkyCurve = 0.12f,
            },
        },
        AmbientLightSource = Godot.Environment.AmbientSource.Color,
        AmbientLightColor = t.Ambient,
        AmbientLightEnergy = t.AmbientEnergy,
        TonemapMode = Godot.Environment.ToneMapper.Filmic,
        TonemapWhite = 6f,
        FogEnabled = t.FogDensity > 0f,
        FogLightColor = t.Fog,
        FogDensity = t.FogDensity,
        FogSkyAffect = 0.4f,
        FogAerialPerspective = 0.3f,
        GlowEnabled = true,
        GlowIntensity = t.Glow,
        GlowBloom = 0.05f,
        GlowHdrThreshold = 1.1f,
        AdjustmentEnabled = true,
        AdjustmentSaturation = 1.12f,
        AdjustmentContrast = 1.05f,
    };

    public static DirectionalLight3D MakeSun(PropTheme t) => new()
    {
        Name = "Sun",
        Rotation = new Vector3(Mathf.DegToRad(t.SunPitchDegrees), Mathf.DegToRad(t.SunYawDegrees), 0f),
        LightColor = t.Sun,
        LightEnergy = t.SunEnergy,
        ShadowEnabled = true,
        ShadowBlur = 1.5f,
        DirectionalShadowMaxDistance = 60f,
    };
}

