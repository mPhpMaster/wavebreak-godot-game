using Godot;
using Wavebreak.Data;
using Wavebreak.Game;

namespace Wavebreak.Actors;

public sealed partial class EnemyManager
{
    private bool TryLeviathanSpawn(EnemyDef definition, Vector3 around, out Vector3 point)
    {
        for (int attempt = 0; attempt < 32; attempt++)
        {
            Vector3 candidate = around + _rng.OnUnitCircleXZ() * _rng.Range(42, 56);
            candidate = Arena.ClampInside(candidate, definition.Radius + 8, out _);
            float floor = ReefCavePath.Floor(candidate), ceiling = WaterSurfaceHeight.At(candidate);
            for (int i = 0; i < 12; i++)
            {
                float angle = i * Mathf.Tau / 12;
                Vector3 edge = candidate + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * definition.Radius;
                floor = Mathf.Max(floor, ReefCavePath.Floor(edge));
                ceiling = Mathf.Min(ceiling, WaterSurfaceHeight.At(edge));
            }
            float bottom = floor + 2, top = ceiling - definition.Height - 2;
            if (bottom > top) continue;
            candidate.Y = Mathf.Clamp(around.Y - definition.Height * .5f, bottom, top);
            Vector3 centre = candidate + Vector3.Up * (definition.Height * .5f);
            if (centre.DistanceTo(around + Vector3.Up) < definition.Radius + 12 || !SpawnSpaceFree(candidate, definition.Radius)) continue;
            bool safe = true;
            foreach (var target in _targets)
                if (GodotObject.IsInstanceValid(target.Node) && target.Hit.IsAlive
                    && centre.DistanceTo(target.Node.GlobalPosition + Vector3.Up) < definition.Radius + 12) safe = false;
            if (!safe) continue;
            point = candidate; return true;
        }
        point = default; return false;
    }
}
