using System;
using Godot;
using Wavebreak.Combat;
using Wavebreak.Core;

namespace Wavebreak.Actors;

/// <summary>
/// Status effects on an enemy: Burn, Poison, Chill/Freeze and Stun. The rules
/// that decide when and how strongly these are applied live in
/// <see cref="StatusEffects"/>; this is only the state, the ticking, and the
/// tint. Plain fields rather than a list of effect objects: there are four
/// statuses, hundreds of enemies, and an enemy with none costs one branch.
///
/// Bosses and elites resist crowd control (<see cref="CcResist"/>), and a boss
/// that has just been frozen or stunned is briefly immune, so no build can
/// lock a boss in place.
/// </summary>
public sealed partial class Enemy
{
    public const int MaxPoisonStacks = 8;
    private const float DotTick = 0.5f;
    private const float SlowPerChill = 0.10f;
    private const float MaxChillSlow = 0.45f;

    private static readonly Color FrozenTint = new(0.78f, 0.94f, 1.00f);
    private static readonly Color StunTint = new(1.00f, 0.95f, 0.45f);
    private static readonly Color BurnTint = new(1.00f, 0.45f, 0.12f);
    private static readonly Color PoisonTint = new(0.50f, 0.95f, 0.28f);
    private static readonly Color ChillTint = new(0.55f, 0.82f, 1.00f);

    private float _burnDps, _burnLeft;
    private float _poisonPerStack, _poisonLeft;
    private int _poisonStacks;
    private int _chill;
    private float _chillLeft;
    private float _freezeLeft, _stunLeft, _ccImmune;
    private float _dotTimer;
    private float _statusClock;

    private Color _statusTint = Colors.White;
    private float _statusTintAmount;

    // ------------------------------------------------------------- queries

    public bool IsBurning => _burnLeft > 0f;
    public bool IsPoisoned => _poisonStacks > 0;
    public int PoisonStacks => _poisonStacks;
    public int ChillStacks => _chill;
    public bool IsFrozen => _freezeLeft > 0f;
    public bool IsStunned => _stunLeft > 0f;

    /// <summary>Multiplier on freeze and stun time.</summary>
    public float CcResist => IsBoss ? 0.3f : IsElite ? 0.6f : 1f;

    /// <summary>Multiplier on chill slow.</summary>
    private float SlowResist => IsBoss ? 0.5f : IsElite ? 0.75f : 1f;

    private bool HasStatus => _burnLeft > 0f || _poisonLeft > 0f || _chillLeft > 0f
                              || _freezeLeft > 0f || _stunLeft > 0f || _ccImmune > 0f || _statusTintAmount > 0f;

    /// <summary>Movement multiplier from statuses: 0 while frozen or stunned.</summary>
    private float StatusSpeedScale
        => _freezeLeft > 0f || _stunLeft > 0f ? 0f : 1f - MathF.Min(MaxChillSlow, SlowPerChill * _chill) * SlowResist;

    /// <summary>Held in place this frame. A committed dash, leap or volley is
    /// not interrupted: stopping one half-way would leave its telegraph lying.</summary>
    private bool StatusHeld => (_freezeLeft > 0f || _stunLeft > 0f) && _act is not (Act.Dash or Act.Leap or Act.Volley);

    // ------------------------------------------------------------ apply API

    /// <summary>Set alight. Refreshes the timer and keeps the hotter flame:
    /// burns that stacked per hit would turn a fast weapon into a furnace.</summary>
    public void ApplyBurn(float dps, float seconds)
    {
        if (!IsAlive || dps <= 0f || seconds <= 0f) return;
        _burnDps = _burnLeft > 0f ? MathF.Max(_burnDps, dps) : dps;
        _burnLeft = MathF.Max(_burnLeft, seconds);
    }

    /// <summary>Add one poison stack (max <see cref="MaxPoisonStacks"/>) and
    /// refresh the timer. Every stack ticks at the strongest per-stack value seen.</summary>
    public void ApplyPoison(float dpsPerStack, float seconds)
    {
        if (!IsAlive || dpsPerStack <= 0f || seconds <= 0f) return;
        _poisonPerStack = _poisonStacks > 0 ? MathF.Max(_poisonPerStack, dpsPerStack) : dpsPerStack;
        _poisonStacks = Math.Min(MaxPoisonStacks, _poisonStacks + 1);
        _poisonLeft = MathF.Max(_poisonLeft, seconds);
    }

    /// <summary>Add a chill stack. At <paramref name="freezeAt"/> stacks the
    /// enemy freezes for <paramref name="freezeSeconds"/> (scaled by
    /// <see cref="CcResist"/>) and the stacks reset. True when it froze.</summary>
    public bool ApplyChill(float seconds, int freezeAt, float freezeSeconds)
    {
        if (!IsAlive || seconds <= 0f) return false;
        _chillLeft = MathF.Max(_chillLeft, seconds);
        if (_freezeLeft > 0f) return false;

        _chill++;
        int at = Math.Max(1, freezeAt);
        if (_chill < at) return false;
        if (_ccImmune > 0f)
        {
            // Immune: stay at the brink, slowed, until the window passes.
            _chill = Math.Max(0, at - 1);
            return false;
        }

        _chill = 0;
        _freezeLeft = MathF.Max(0.1f, freezeSeconds * CcResist);
        _ccImmune = _freezeLeft + (IsBoss ? 4f : IsElite ? 2f : 0.75f);
        if (IsBoss || IsElite)
            EventBus.Publish(new PopupRequested(AimPoint + Vector3.Up * 1.0f, "FROZEN", FrozenTint, 1.0f));
        return true;
    }

    /// <summary>Stun for <paramref name="seconds"/> scaled by <see cref="CcResist"/>.
    /// Bosses then shrug off crowd control for a few seconds.</summary>
    public void ApplyStun(float seconds)
    {
        if (!IsAlive || seconds <= 0f || _ccImmune > 0f) return;
        _stunLeft = MathF.Max(_stunLeft, seconds * CcResist);
        if (IsBoss) _ccImmune = _stunLeft + 2.5f;
        else if (IsElite) _ccImmune = _stunLeft + 1f;
    }

    /// <summary>Drop every status. Spawn and death call this.</summary>
    public void ClearStatuses()
    {
        _burnDps = _burnLeft = 0f;
        _poisonPerStack = _poisonLeft = 0f;
        _poisonStacks = 0;
        _chill = 0;
        _chillLeft = _freezeLeft = _stunLeft = _ccImmune = 0f;
        _dotTimer = 0f;
        if (_statusTintAmount > 0f) SetStatusTint(Colors.White, 0f);
    }

    // ----------------------------------------------------------------- tick

    /// <summary>Called first thing in <see cref="Step"/>. May kill the enemy.</summary>
    private void StepStatus(float dt)
    {
        if (!HasStatus) return;
        _statusClock += dt;

        if (_burnLeft > 0f || _poisonLeft > 0f)
        {
            _dotTimer += dt;
            if (_dotTimer >= DotTick)
            {
                float step = _dotTimer;
                _dotTimer = 0f;
                if (_burnLeft > 0f) DotDamage(_burnDps * MathF.Min(step, _burnLeft + dt), Element.Fire, "burn");
                if (_dead) return;
                if (_poisonLeft > 0f && _poisonStacks > 0)
                    DotDamage(_poisonPerStack * _poisonStacks * MathF.Min(step, _poisonLeft + dt), Element.Poison, "poison");
                if (_dead) return;
            }
        }
        else _dotTimer = 0f;

        if (_burnLeft > 0f && (_burnLeft -= dt) <= 0f) { _burnLeft = 0f; _burnDps = 0f; }
        if (_poisonLeft > 0f && (_poisonLeft -= dt) <= 0f) { _poisonLeft = 0f; _poisonStacks = 0; _poisonPerStack = 0f; }
        if (_chillLeft > 0f && (_chillLeft -= dt) <= 0f) { _chillLeft = 0f; _chill = 0; }
        if (_freezeLeft > 0f) _freezeLeft = MathF.Max(0f, _freezeLeft - dt);
        if (_stunLeft > 0f) _stunLeft = MathF.Max(0f, _stunLeft - dt);
        if (_ccImmune > 0f) _ccImmune = MathF.Max(0f, _ccImmune - dt);

        UpdateStatusTint();
    }

    private void DotDamage(float amount, Element element, string source)
    {
        if (amount <= 0f) return;
        var info = new DamageInfo
        {
            Amount = amount,
            Element = element,
            CanCrit = false,
            ProcCoefficient = 0f,   // a tick never procs anything
            Source = source,
        };
        Vector3 at = AimPoint;
        TakeDamage(info);
        if (info.Nullified || info.Amount <= 0f) return;

        if (WeaponSystem.PublishHitEvents) EventBus.Publish(new EnemyDamaged(this, info));
        if (WeaponSystem.PublishDamageNumbers && info.Amount >= 1f)
            EventBus.Publish(new PopupRequested(at + Vector3.Up * 0.5f,
                Mathf.RoundToInt(info.Amount).ToString(), WeaponSystem.ColorOf(element), 0.7f));
    }

    // -------------------------------------------------------------- visuals

    private void UpdateStatusTint()
    {
        Color c;
        float a;
        if (_freezeLeft > 0f) { c = FrozenTint; a = 0.8f; }
        else if (_stunLeft > 0f) { c = StunTint; a = 0.45f + 0.25f * MathF.Sin(_statusClock * 40f); }
        else if (_burnLeft > 0f) { c = BurnTint; a = 0.35f + 0.12f * MathF.Sin(_statusClock * 14f); }
        else if (_poisonStacks > 0) { c = PoisonTint; a = 0.25f + 0.03f * _poisonStacks; }
        else if (_chill > 0) { c = ChillTint; a = 0.12f * _chill; }
        else { c = Colors.White; a = 0f; }

        if (MathF.Abs(a - _statusTintAmount) < 0.03f && c.IsEqualApprox(_statusTint)) return;
        SetStatusTint(c, a);
    }

    private void SetStatusTint(Color c, float a)
    {
        _statusTint = c;
        _statusTintAmount = Mathf.Clamp(a, 0f, 1f);
        ApplyFlash(_flashTimer > 0f ? _flashTimer / FlashDuration : 0f);

        // The rig carries the wind-up glow; never overwrite a telegraph.
        if (_rig is null || !IsInstanceValid(_rig) || _glow > 0f) return;
        if (_statusTintAmount > 0f) _rig.SetGlow(_statusTint, _statusTintAmount * 1.4f);
        else _rig.SetGlow(EnrageColor, _phase >= 2 ? 0.3f : 0f);
    }
}
