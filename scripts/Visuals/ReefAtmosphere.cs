using Godot;
using Wavebreak.Game;

namespace Wavebreak.Visuals;

public sealed partial class ReefAtmosphere : Node
{
    private readonly WorldEnvironment _world;
    private readonly DirectionalLight3D _sun;
    private readonly ShaderMaterial _bubbles;
    private float _air;

    public ReefAtmosphere(WorldEnvironment world, DirectionalLight3D sun, ShaderMaterial bubbles)
    { _world = world; _sun = sun; _bubbles = bubbles; }

    public override void _Ready()
    {
        _sun.SkyMode = DirectionalLight3D.SkyModeEnum.LightOnly;
        AddChild(new DirectionalLight3D { Name = "ReefSkySun", Rotation = _sun.Rotation,
            SkyMode = DirectionalLight3D.SkyModeEnum.SkyOnly, LightAngularDistance = 1.2f,
            ShadowEnabled = false, LightEnergy = 1.2f, LightColor = new Color(1, 0.95f, 0.82f) });
        _world.Environment.Sky = new Sky { SkyMaterial = new ProceduralSkyMaterial {
            SkyTopColor = new Color(0.14f, 0.4f, 0.72f), SkyHorizonColor = new Color(0.72f, 0.88f, 0.95f),
            GroundHorizonColor = new Color(0.4f, 0.65f, 0.72f), SunAngleMax = 6f } };
    }

    public override void _Process(double delta)
    {
        float time = WaterSurfaceHeight.Seconds;
        _bubbles.SetShaderParameter("water_time", time);
        if (GetViewport().GetCamera3D() is not { } camera) return;
        float depth = WaterSurfaceHeight.At(camera.GlobalPosition) - camera.GlobalPosition.Y;
        _air = Mathf.Lerp(_air, Mathf.SmoothStep(-0.8f, 0.8f, -depth), 1f - Mathf.Exp(-(float)delta * 5f));
        float shallow = Mathf.Exp(-Mathf.Max(0, depth) / 28f);
        Godot.Environment env = _world.Environment;
        env.BackgroundMode = Godot.Environment.BGMode.Sky;
        env.FogEnabled = _air < 0.995f;
        env.FogDensity = Mathf.Lerp(0.024f - shallow * 0.012f, 0.0006f, _air);
        env.FogLightColor = new Color(0.025f, 0.19f, 0.26f).Lerp(new Color(0.7f, 0.85f, 0.95f), _air);
        env.AmbientLightColor = new Color(0.06f, 0.3f, 0.38f).Lerp(new Color(0.75f, 0.86f, 1), _air);
        env.AmbientLightEnergy = Mathf.Lerp(0.3f + shallow * 0.45f, 0.85f, _air);
        _sun.LightEnergy = Mathf.Lerp(0.28f + shallow * 0.6f, 1.2f, _air);
        _sun.LightColor = new Color(0.5f, 0.85f, 0.95f).Lerp(new Color(1, 0.94f, 0.8f), _air);
    }
}
