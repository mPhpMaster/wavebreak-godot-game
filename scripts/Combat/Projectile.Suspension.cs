using Godot;

namespace Wavebreak.Combat;

public sealed partial class Projectile
{
    internal static void ClearLiveCombat()
    {
        for (int i = Live.Count - 1; i >= 0; i--)
        {
            Projectile projectile = Live[i];
            if (!GodotObject.IsInstanceValid(projectile)) continue;
            projectile.Retire();
            _pool?.Release(projectile);
        }
        Live.Clear();
        ExplosionFx.ClearLiveCombat();
        Weapons.SignatureFx.Clear();
    }
}

internal sealed partial class ExplosionFx
{
    internal static void ClearLiveCombat()
    {
        foreach (ExplosionFx effect in Live)
        {
            if (!GodotObject.IsInstanceValid(effect)) continue;
            effect._retired = true;
            effect.Visible = false;
            _pool?.Release(effect);
        }
        Live.Clear();
    }
}
