using Godot;
using Wavebreak.Data;
using Wavebreak.Game;

namespace Wavebreak.Visuals;

public sealed partial class ReefCurrentVisuals : Node3D
{
    private readonly MultiMesh[] _batches = new MultiMesh[ReefCurrents.All.Length];
    private float _time;

    public override void _Ready()
    {
        for (int f = 0; f < _batches.Length; f++)
        {
            var batch = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                Mesh = new CylinderMesh { TopRadius = 0f, BottomRadius = 0.18f, Height = 0.8f, RadialSegments = 5 },
                InstanceCount = 64,
            };
            _batches[f] = batch;
            AddChild(new MultiMeshInstance3D { Multimesh = batch, MaterialOverride = Arena.Flat(new Color(0.2f, 0.85f, 1f), 0.8f) });
        }
    }

    public override void _Process(double delta)
    {
        _time += (float)delta;
        for (int f = 0; f < _batches.Length; f++)
        {
            ReefCurrentDef flow = ReefCurrents.All[f];
            Vector2 direction = (flow.To - flow.From).Normalized();
            Basis basis = new(new Quaternion(Vector3.Up, new Vector3(direction.X, 0f, direction.Y)));
            for (int i = 0; i < 64; i++)
            {
                float t = Mathf.PosMod(i / 64f + _time * flow.Speed / flow.From.DistanceTo(flow.To), 1f);
                Vector2 p = flow.From.Lerp(flow.To, t) + new Vector2(-direction.Y, direction.X) * Mathf.Sin(i * 2.4f) * 5f;
                var at = new Vector3(p.X, 0f, p.Y);
                at.Y = UnderwaterTerrain.Height(at) + 7f + Mathf.Sin(i * 1.7f) * 3f;
                _batches[f].SetInstanceTransform(i, new Transform3D(basis, at));
            }
        }
    }
}
