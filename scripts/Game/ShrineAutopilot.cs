using Godot;
using Wavebreak.Actors;
using Wavebreak.Data;

namespace Wavebreak.Game;

/// <summary>
/// The autopilot's use of charge shrines, kept out of <see cref="Autopilot"/> so
/// the bot's other goals can change around it.
///
/// It walks to a nearby charge ring when nothing is close and holds the middle
/// until it fills, so an unattended run exercises charging, draining and the
/// blessing (auto-picked: the panel never opens in --auto runs). "Sometimes":
/// it skips about a third of the rings, chosen per shrine so the choice is
/// stable rather than flickering frame to frame, and gives up on a ring when an
/// enemy is at touching distance — the drain path gets exercised that way too.
/// </summary>
public static class ShrineAutopilot
{
    private const float Detour = 30f;
    private const float SafeDistance = 4.5f;
    private const float PanicDistance = 1.8f;

    public static Vector3 Steer(Vector3 me, float nearestEnemy)
    {
        ShrineSpawner? spawner = ShrineSpawner.Instance;
        if (spawner is null) return Vector3.Zero;

        MapShrine? target = null;
        float best = Detour * Detour;
        foreach (MapShrine s in spawner.Shrines)
        {
            if (s.Spent || s.Kind != MapShrineKind.Charge || !GodotObject.IsInstanceValid(s)) continue;
            if (s.GetInstanceId() % 3 == 0) continue;
            Vector3 d = s.GlobalPosition - me; d.Y = 0f;
            float sq = d.LengthSquared();
            if (sq < best) { best = sq; target = s; }
        }
        if (target is null) return Vector3.Zero;

        bool inside = ShrineSpawner.InRing(target, me);
        if (nearestEnemy < (inside ? PanicDistance : SafeDistance)) return Vector3.Zero;

        Vector3 to = target.GlobalPosition - me; to.Y = 0f;
        float dist = to.Length();
        if (dist < 0.6f) return Vector3.Zero;
        // Inside: a firm pull to the centre outweighs the wander and the orb
        // pull, so the bot stays put. Outside: about as strong as a chest detour.
        return to / dist * (inside ? 3.5f : 2.4f);
    }
}
