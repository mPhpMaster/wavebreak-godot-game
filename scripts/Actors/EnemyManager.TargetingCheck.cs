using Godot;
using Wavebreak.Combat;

namespace Wavebreak.Actors;

public sealed partial class EnemyManager
{
    internal Vector3 InspectTeamCenter(Vector3 fallback) { RefreshTargets(); return SpawnAnchor(fallback); }
    internal IDamageable? InspectNearestPlayer(Vector3 point)
    {
        RefreshTargets();
        NearestTarget(point, out _, out IDamageable? target);
        return target;
    }
}
