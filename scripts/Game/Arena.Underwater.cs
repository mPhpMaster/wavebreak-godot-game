using Godot;
using Wavebreak.Visuals;

namespace Wavebreak.Game;

public sealed partial class Arena
{
    private void BuildReefGround()
    {
        ArrayMesh mesh = UnderwaterTerrain.Build(_stage.InRadius);
        AddChild(new MeshInstance3D
        {
            Name = "ReefValleysAndCliffs", Mesh = mesh,
            MaterialOverride = UnderwaterMaterials.Seabed(),
        });
        _body.AddChild(new CollisionShape3D { Name = "ReefReliefCollision", Shape = mesh.CreateTrimeshShape() });
        foreach (Vector2 cave in ReefSites.Caves) _footprints.Add((cave, 24f));
        _footprints.Add((ReefSites.Wreck, 23f));
        AddChild(new ReefLandmarks());
        AddChild(new ReefCaveDiscovery());
        ReefPlacement.BuildLedges(this, _stage.InRadius);
        AddChild(new ReefHarvest());
        var rock = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 7, Rings = 4 };
        for (int i = 0; i < 44; i++)
        {
            float angle = i * Mathf.Tau / 44f;
            float radius = _stage.InRadius - _rng.RandfRange(20f, 25f);
            var at = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            if (new Vector2(at.X - ReefCavePath.Entrance.X, at.Z - ReefCavePath.Entrance.Z).Length() < 23f) continue;
            at.Y = UnderwaterTerrain.Height(at) + _rng.RandfRange(6f, 10f);
            var scale = new Vector3(_rng.RandfRange(5f, 9f), _rng.RandfRange(10f, 17f), _rng.RandfRange(5f, 8f));
            var basis = Basis.FromEuler(new Vector3(_rng.RandfRange(-0.25f, 0.25f), angle, 0.12f)).Scaled(scale);
            AddSolid(rock, new Transform3D(basis, at), Flat(new Color(0.18f, 0.29f, 0.32f).Lightened(_rng.RandfRange(0f, 0.12f))));
            _footprints.Add((new Vector2(at.X, at.Z), 10f));
        }
    }
}
