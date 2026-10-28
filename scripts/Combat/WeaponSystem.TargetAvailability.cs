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

    internal bool HasAttackTarget(float range)
    {
        IDamageable? target = Targeting.Nearest(MuzzlePosition, range);
        if (target is not { IsAlive: true }) return false;
        return Body is not { } body || !body.IsInsideTree()
            || Game.GrappleAnchors.Clear(body, MuzzlePosition, target.Position);
    }
}
