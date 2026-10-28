using Godot;

namespace Wavebreak.Combat;

public sealed partial class WeaponSystem
{
    private bool _restSuspended;
    internal bool SuspendForRest()
    {
        if (Game.Main.Instance?.Stages?.InRestRoom != true) { _restSuspended = false; return false; }
        if (!_restSuspended)
        {
            Projectile.ClearLiveCombat();
            foreach (WeaponRuntime weapon in _weapons) weapon.HidePersistentVisuals();
            _restSuspended = true;
        }
        return true;
    }

    private readonly IDamageable?[] _sightCandidates = new IDamageable?[8];
    private readonly float[] _sightDistances = new float[8];
    private IDamageable? _cachedAttackTarget;
    private int _cachedRevision = -1;
    private float _cachedRange;
    private Vector3 _cachedOrigin;

    internal bool HasAttackTarget(float range) => FindAttackTarget(range) is not null;
    private IDamageable? FindAttackTarget(float range)
    {
        var live = Targeting.Live;
        Vector3 origin = MuzzlePosition;
        if (_cachedRevision == Targeting.Revision && _cachedRange == range && _cachedOrigin == origin
            && _cachedAttackTarget is { IsAlive: true }) return _cachedAttackTarget;
        int count = 0;
        float limit = (range + Targeting.AssumedTargetRadius) * (range + Targeting.AssumedTargetRadius);
        foreach (IDamageable target in live)
        {
            if (!target.IsAlive) continue;
            float distance = TargetGeometry.DistanceSquared(target, origin);
            if (distance > limit) continue;
            int index = count;
            if (index == _sightCandidates.Length)
            { if (distance >= _sightDistances[index - 1]) continue; index--; }
            else count++;
            while (index > 0 && distance < _sightDistances[index - 1])
            { _sightDistances[index] = _sightDistances[index - 1]; _sightCandidates[index] = _sightCandidates[index - 1]; index--; }
            _sightDistances[index] = distance; _sightCandidates[index] = target;
        }
        _cachedAttackTarget = null;
        for (int i = 0; i < count; i++)
        {
            IDamageable candidate = _sightCandidates[i]!;
            if (Body is { } body && body.IsInsideTree() && candidate is Node3D node && node.IsInsideTree()
                && !Game.WorldSight.Clear(body, origin, candidate.Position)) continue;
            _cachedAttackTarget = candidate; break;
        }
        _cachedRevision = Targeting.Revision; _cachedRange = range; _cachedOrigin = origin;
        return _cachedAttackTarget;
    }
}
