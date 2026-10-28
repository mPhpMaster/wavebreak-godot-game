using System;
using Godot;

namespace Wavebreak.Visuals;

/// <summary>One fixed particle batch per swimmer; positions remain in world space.</summary>
public sealed partial class SwimmerWake : Node3D
{
    private const int Count = 64;
    private readonly Node3D _target;
    private readonly Func<Vector3> _velocity;
    private readonly Func<bool> _active;
    private readonly Vector3[] _positions = new Vector3[Count];
    private readonly Vector3[] _drift = new Vector3[Count];
    private readonly float[] _life = new float[Count];
    private MultiMesh _batch = null!;
    private float _emit;
    private int _next;
    private int _live;
    public int Emitted { get; private set; }

    public SwimmerWake(Node3D target, Func<Vector3> velocity, Func<bool> active)
    { _target = target; _velocity = velocity; _active = active; }

    public override void _Ready()
    {
        TopLevel = true;
        GlobalPosition = Vector3.Zero;
        Name = "SwimmingWake";
        _batch = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 6, Rings = 3 }, InstanceCount = Count,
        };
        var material = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.6f, 0.94f, 1f, 0.45f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        AddChild(new MultiMeshInstance3D { Multimesh = _batch, MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        for (int i = 0; i < Count; i++) _batch.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(Vector3.One * 0.001f), Vector3.Down * 1000f));
    }

    public override void _Process(double delta)
    {
        float dt = Mathf.Min((float)delta, 0.1f);
        bool active = _active();
        if (!active && _live == 0) { Visible = false; return; }
        Visible = true;
        Vector3 velocity = _velocity();
        float speed = velocity.Length();
        _emit -= dt;
        if (active && speed > 0.6f && _emit <= 0f)
        {
            _emit = speed > 10f ? 0.025f : 0.09f;
            int i = _next++ % Count;
            _life[i] = 1.6f;
            _positions[i] = _target.GlobalPosition + Vector3.Up * 0.9f - velocity.Normalized() * 0.65f
                + new Vector3(Mathf.Sin(_next * 2.4f), 0f, Mathf.Cos(_next * 1.7f)) * 0.25f;
            _drift[i] = -velocity * 0.1f + Vector3.Up * 0.65f;
            Emitted++;
        }
        _live = 0;
        for (int i = 0; i < Count; i++)
        {
            _life[i] = Mathf.Max(0f, _life[i] - dt);
            if (_positions[i].Y + 0.2f >= Game.WaterSurfaceHeight.At(_positions[i])) _life[i] = 0f;
            if (_life[i] <= 0f)
            { _batch.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(Vector3.One * 0.001f), Vector3.Down * 1000f)); continue; }
            _positions[i] += _drift[i] * dt;
            _live++;
            float scale = Mathf.Sin(_life[i] / 1.6f * Mathf.Pi) * 0.12f + 0.015f;
            _batch.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(new Vector3(scale, scale * 1.3f, scale)), _positions[i]));
        }
    }
}
