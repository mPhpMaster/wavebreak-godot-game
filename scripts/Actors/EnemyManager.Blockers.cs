using Godot;
using Wavebreak.Game;

namespace Wavebreak.Actors;

/// <summary>Test hook for the blocker check (StageDirector.Blockers.cs): run
/// the real spawn-point search many times and count the points that landed
/// inside a blocker (should be none).</summary>
public sealed partial class EnemyManager
{
    internal (int Found, int Inside) ProbeSpawnRing(Vector3 around, int tries)
    {
        int found = 0, inside = 0;
        for (int i = 0; i < tries; i++)
        {
            if (!TryFindSpawnPoint(around, out Vector3 p)) continue;
            found++;
            if (MapBlockers.Inside(p with { Y = -1f }, 0.5f)) inside++;
        }
        return (found, inside);
    }
}
