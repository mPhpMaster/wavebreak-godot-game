using Godot;
using Wavebreak.Game;

namespace Wavebreak.Visuals;

public sealed partial class ReefWaterSurface : MeshInstance3D
{
    public ReefWaterSurface(float radius)
    {
        Name = "AnimatedWaterSurface";
        Mesh = new PlaneMesh { Size = Vector2.One * radius * 2.5f, SubdivideWidth = 128, SubdivideDepth = 128 };
        Position = Vector3.Up * WaterSurfaceHeight.BaseHeight;
        MaterialOverride = UnderwaterMaterials.Surface();
        ExtraCullMargin = 3f;
        CastShadow = ShadowCastingSetting.Off;
    }

    public override void _Process(double delta)
    {
        if (MaterialOverride is ShaderMaterial material)
            material.SetShaderParameter("water_time", WaterSurfaceHeight.Seconds);
    }
}
