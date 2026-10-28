using Godot;
using Wavebreak.Visuals;

namespace Wavebreak.Combat.Weapons;

internal sealed class ShotgunBubbles : SignatureFx.IFx
{
    private static ArrayMesh? _mesh;
    private readonly MeshInstance3D _visual;
    private readonly Vector3 _heading;
    private float _age;
    private ShotgunBubbles(MeshInstance3D visual, Vector3 heading) { _visual = visual; _heading = heading; }
    internal static void Spawn(Vector3 at, Vector3 heading)
    {
        if (_mesh is null)
        {
            var kit = new MeshKit();
            for (int i = 0; i < 8; i++)
                kit.Sphere(new Vector3(Mathf.Sin(i * 2.4f) * .2f, i * .04f, Mathf.Cos(i * 2.4f) * .2f),
                    .035f + i * .005f, new Color(.55f, .9f, 1), .25f);
            _mesh = kit.ToMesh();
        }
        var visual = SignatureFx.NewMesh(_mesh, SignatureFx.Mat(new Color(.55f, .9f, 1)));
        if (visual is null) return;
        visual.GlobalPosition = at;
        if (!SignatureFx.Add(new ShotgunBubbles(visual, heading))) SignatureFx.FreeNode(visual);
    }
    public bool Step(float dt)
    {
        _age += dt;
        _visual.GlobalPosition += (_heading * 1.5f + Vector3.Up * .8f) * dt;
        _visual.Scale = Vector3.One * (1 + _age * 2);
        _visual.Transparency = Mathf.Clamp(_age / .5f, 0, 1);
        return _age < .5f;
    }
    public void Free() => SignatureFx.FreeNode(_visual);
}
