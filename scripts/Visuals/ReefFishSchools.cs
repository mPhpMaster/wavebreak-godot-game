using Godot;
using Wavebreak.Actors;
using Wavebreak.Game;

namespace Wavebreak.Visuals;

/// <summary>144 ambient fish in one draw batch; school goals react to nearby swimmers.</summary>
public sealed partial class ReefFishSchools : Node3D
{
    public const int FishCount = 144;
    private MultiMesh _batch = null!;
    private readonly Vector3[] _positions = new Vector3[FishCount];
    private readonly Vector3[] _centres = new Vector3[6];
    private float _time;
    public int ReactingFish { get; private set; }
    public int TotalReactions { get; private set; }
    public Vector3 SchoolCentre(int index) => _centres[Mathf.Clamp(index, 0, _centres.Length - 1)];

    public override void _Ready()
    {
        Name = "ReactiveFishSchools";
        var kit = new MeshKit();
        kit.Sphere(Vector3.Zero, 0.2f, Colors.White, 0.15f, 6, 4);
        kit.Cone(new Vector3(0f, 0f, 0.15f), new Vector3(0f, 0f, 0.5f), 0.16f, Colors.White, 0.1f, 3);
        _batch = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true, Mesh = kit.ToMesh(), InstanceCount = FishCount };
        AddChild(new MultiMeshInstance3D { Multimesh = _batch, MaterialOverride = ToonMaterials.Prop,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        for (int s = 0; s < _centres.Length; s++)
        {
            float angle = s * 2.4f;
            var at = new Vector3(Mathf.Cos(angle) * (28f + s * 9f), 0f, Mathf.Sin(angle) * (28f + s * 9f));
            at.Y = UnderwaterTerrain.Height(at) + 14f + s % 3 * 9f;
            _centres[s] = at;
        }
        for (int i = 0; i < FishCount; i++)
        {
            _positions[i] = _centres[i / 24];
            _batch.SetInstanceColor(i, i % 3 == 0 ? new Color(1f, 0.65f, 0.25f) : new Color(0.35f, 0.85f, 1f));
        }
    }

    public override void _Process(double delta)
    {
        float dt = Mathf.Min((float)delta, 0.1f);
        _time += dt;
        ReactingFish = 0;
        Vector3 swimmer = Player.Instance?.CenterPosition ?? Vector3.Down * 1000f;
        for (int i = 0; i < FishCount; i++)
        {
            Vector3 centre = _centres[i / 24];
            float phase = _time * 0.65f + i * 2.39996f;
            Vector3 goal = centre + new Vector3(Mathf.Sin(phase) * (4f + i % 5), Mathf.Sin(phase * 0.7f) * 2f,
                Mathf.Cos(phase) * (4f + i % 4));
            Vector3 away = _positions[i] - swimmer;
            if (away.LengthSquared() < 81f)
            {
                goal += away.Normalized() * (9f - away.Length()) * 2f;
                ReactingFish++;
                TotalReactions++;
            }
            Vector3 before = _positions[i];
            _positions[i] = before.Lerp(goal, 1f - Mathf.Exp(-2.5f * dt));
            Vector3 direction = _positions[i] - before;
            if (direction.LengthSquared() < 0.0001f) direction = Vector3.Forward;
            Basis basis = new(new Quaternion(Vector3.Forward, direction.Normalized()));
            basis = basis.Scaled(new Vector3(0.75f, 0.55f, 1.6f));
            _batch.SetInstanceTransform(i, new Transform3D(basis, _positions[i]));
        }
    }
}
