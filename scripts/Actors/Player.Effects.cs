using System.Linq;
using Godot;
using Wavebreak.Combat;
using Wavebreak.Core;
using Wavebreak.Stats;

namespace Wavebreak.Actors;

public sealed partial class Player
{
    private ulong _arrivalSafeUntil;
    // ---------------------------------------------------------------- effects

    /// <summary>White-hot pop on the body.</summary>
    public void FlashHit(float strength = 1f) => _flash = Mathf.Max(_flash, Mathf.Clamp(strength, 0f, 1f));

    /// <summary>
    /// Shove the player. The impulse decays on its own and is kept separate from
    /// walking speed, so being hit displaces you without taking control away.
    /// </summary>
    public void ApplyKnockback(Vector3 impulse)
    {
        impulse.Y = 0f;
        if (impulse.LengthSquared() < 0.0001f) return;
        _externalVelocity = (_externalVelocity + impulse).LimitLength(14f);
    }

    /// <summary>Places the player and clears all momentum and movement state.</summary>
    public void Teleport(Vector3 position)
    {
        _swimWeaponRecoil = Vector3.Zero;
        _swimVerticalVelocity = 0;
        ReleaseHook();
        _cityBallistic = false;
        CoreStrikeFeedback = "";
        _coreFeedbackTime = 0f;
        GlobalPosition = position;
        _arrivalSafeUntil = Game.Arena.Current.Underwater && position.Y > Game.WaterSurfaceHeight.BaseHeight + 10f
            ? Time.GetTicksMsec() + 4000 : 0;
        Velocity = Vector3.Zero;
        _moveVelocity = Vector3.Zero;
        _externalVelocity = Vector3.Zero;
        _lastPlanar = Vector3.Zero;
        _dashTime = 0f;
        _sliding = false;
        _slamming = false;
        IsBreaching = false;
        _airJumpsLeft = AirJumps;
    }

    // ------------------------------------------------------------ IDamageable

    public void TakeDamage(DamageInfo info)
    {
        if (Health is null || Time.GetTicksMsec() < _arrivalSafeUntil)
        {
            info.Nullified = true;
            return;
        }
        Health.TakeDamage(info);
    }

    public bool IsAlive => Health is null || Health.IsAlive;

    // Explicit: Node3D.Position is the *local* one.
    Vector3 IDamageable.Position => CenterPosition;
}
