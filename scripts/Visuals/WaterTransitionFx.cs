using Godot;

namespace Wavebreak.Visuals;

public static class WaterTransitionFx
{
    public static void Spawn(Node parent, Vector3 at, bool entry, float speed)
    {
        if (DisplayServer.GetName() == "headless") return;
        var root = new Node3D { Name = entry ? "WaterEntrySplash" : "WaterExitSplash", Position = at };
        parent.AddChild(root);
        var material = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.7f, 0.95f, 1f, 0.8f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        var ripple = new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 0.6f, OuterRadius = 0.75f, Rings = 32, RingSegments = 6 },
            Scale = new Vector3(1f, 0.15f, 1f), MaterialOverride = material,
        };
        root.AddChild(ripple);
        root.AddChild(new GpuParticles3D
        {
            Amount = Mathf.Clamp((int)speed + 10, 14, 40), Lifetime = 1.2, OneShot = true,
            Explosiveness = 1f, Emitting = true,
            DrawPass1 = new SphereMesh { Radius = 0.09f, Height = 0.18f, RadialSegments = 6, Rings = 3, Material = material },
            ProcessMaterial = new ParticleProcessMaterial
            {
                Direction = Vector3.Up, Spread = 42f, InitialVelocityMin = 2f,
                InitialVelocityMax = entry ? 5f : 3.5f, Gravity = Vector3.Down * 8f,
            },
        });
        Tween tween = root.CreateTween();
        tween.SetParallel();
        tween.TweenProperty(ripple, "scale", new Vector3(4f, 0.15f, 4f), 1.2);
        tween.TweenProperty(material, "albedo_color:a", 0f, 1.2);
        tween.Chain().TweenCallback(Callable.From(root.QueueFree));
    }
}
