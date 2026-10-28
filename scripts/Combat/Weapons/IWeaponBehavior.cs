using Wavebreak.Stats;

namespace Wavebreak.Combat.Weapons;

/// <summary>
/// How one expansion weapon delivers a volley. Stateless: everything per-shot
/// lives on the pooled projectiles it spawns, so one instance serves every run.
/// </summary>
internal interface IWeaponBehavior
{
    void Fire(WeaponRuntime weapon, WeaponSystem system, StatSheet stats);
}

