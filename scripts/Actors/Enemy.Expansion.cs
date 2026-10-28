using System;
using Godot;
using Wavebreak.Combat;
using Wavebreak.Core;
using Wavebreak.Data;

namespace Wavebreak.Actors;

/// <summary>
/// The expansion's enemy mechanics, kept in their own partial so the core
/// enemy files only carry one-line hooks:
///
///   * traits any behaviour can have: <see cref="EnemyDef.Hover"/> (flies over
///     terrain), <see cref="EnemyDef.ShieldArc"/> (frontal block) and
///     <see cref="EnemyDef.Aura"/> (a ring that buffs allies inside it);
///   * four behaviours: <see cref="EnemyBehavior.Burrower"/>,
///     <see cref="EnemyBehavior.Healer"/>, <see cref="EnemyBehavior.Artillery"/>
///     and <see cref="EnemyBehavior.Blinker"/>;
///   * three boss moves: <see cref="BossMove.Riptide"/>,
///     <see cref="BossMove.Surface"/> and <see cref="BossMove.Maelstrom"/>.
///
/// Every one of them follows the file-wide rule of <c>Enemy.Behaviors</c>: a
/// tell the player can read before anything lands. Buffs are timers refreshed
/// by the aura's pulse, so an aura that dies takes its effect with it within
/// half a second, with nothing to clean up on the allies.
/// </summary>
public sealed partial class Enemy
{
    private static readonly Color HealColor = new(0.40f, 1.00f, 0.55f, 0.75f);
    private static readonly Color BlinkColor = new(0.72f, 0.45f, 1.00f, 0.8f);
    private static readonly Color ShellColor = new(1.00f, 0.55f, 0.20f, 0.85f);
    private static readonly Color TrailColor = new(0.62f, 0.50f, 0.34f, 0.55f);
    private static readonly Color BlockColor = new(0.80f, 0.85f, 0.95f);

    private const float AuraPulse = 0.25f;
    private const float BuffHold = 0.6f;
    private const float BurrowSpeed = 1.7f;
    private const float ReburrowDistance = 7f;

    private bool _xTraits;          // this def has a per-frame expansion trait
    private bool _buffed;           // some aura buff is running
    private bool _burrowed;
    private float _surfacedFor;
    private float _auraTimer;
    private float _blockPopupAt;
    private float _hoverPhase;
    private GroundDecal? _trail;
    private GroundDecal? _auraDecal;

    private float _hasteLeft, _hasteStrength;
    private float _wardLeft, _wardStrength;
    private float _frenzyLeft, _frenzyStrength;

    /// <summary>Underground: untouchable and unseen. For tests and UI.</summary>
    public bool IsBurrowed => _burrowed;

    /// <summary>Shooters, healers and artillery hold a band instead of closing in.</summary>
    private bool HoldsRange => Behavior is EnemyBehavior.Shooter or EnemyBehavior.Healer or EnemyBehavior.Artillery
                                       or EnemyBehavior.Bubbler;

    // ------------------------------------------------------------ lifecycle

    private void SpawnExpansion()
    {
        _buffed = false;
        _hasteLeft = _wardLeft = _frenzyLeft = 0f;
        _burrowed = false;
        _surfacedFor = 0f;
        _auraTimer = 0f;
        _blockPopupAt = 0f;
        if (_def is not { } def) { _xTraits = false; return; }

        _xTraits = def.Hover > 0f || def.Aura != EnemyAura.None || def.Behavior == EnemyBehavior.Burrower;
        _hoverPhase = (GetInstanceId() % 628) * 0.01f;
        _hoverFloor = float.NaN;   // pooled: re-probe the ground at the new spot

        if (def.Hover > 0f)
        {
            // Flyers ignore the world: no floor, no walls, no hills.
            CollisionMask = 0;
            GlobalPosition += new Vector3(0f, def.Hover, 0f);
        }
        // Burrowers arrive above ground, ready to dig: out on the spawn ring
        // they go under at their first step, and anything that hits them on
        // the spawn frame (the content audit) meets an ordinary body.
        if (def.Behavior == EnemyBehavior.Burrower) _surfacedFor = def.AbilityCooldown;
        if (def.Aura != EnemyAura.None) _auraDecal = EnemyAttacks.AcquireDecal();
        SpawnCute();                                // Enemy.Cute.cs
    }

    private void EndExpansion()
    {
        EnemyAttacks.ReleaseDecal(_trail);
        _trail = null;
        EnemyAttacks.ReleaseDecal(_auraDecal);
        _auraDecal = null;
        EndCute();                                  // Enemy.Cute.cs
    }

    private void Burrow()
    {
        _burrowed = true;
        _surfacedFor = 0f;
        _abilityTimer = 0f;
        CollisionLayer = 0;
        Visible = false;
        _trail ??= EnemyAttacks.AcquireDecal();
        ContentAudit.NoteAbility(_def, "Burrow");
    }

    private void Emerge()
    {
        _burrowed = false;
        _surfacedFor = 0f;
        CollisionLayer = EnemyLayer;
        Visible = true;
        EnemyAttacks.ReleaseDecal(_trail);
        _trail = null;
    }

    // ----------------------------------------------------------- per frame

    private float _hoverFloor = float.NaN;
    private float _hoverProbe;

    /// <summary>Height of the ground under a flyer, re-probed with one ray
    /// every 0.3 s (staggered by instance). Falls back to the player's height
    /// until the first probe lands or when nothing is below.</summary>
    private float HoverFloor(float dt, float fallback)
    {
        _hoverProbe -= dt;
        if (_hoverProbe <= 0f || float.IsNaN(_hoverFloor))
        {
            _hoverProbe = 0.3f + _hoverPhase * 0.05f;
            Vector3 at = GlobalPosition;
            var query = PhysicsRayQueryParameters3D.Create(at + Vector3.Up * 2f, at + Vector3.Down * 80f, WorldLayer);
            var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
            if (hit.Count > 0) _hoverFloor = ((Vector3)hit["position"]).Y;
            else if (float.IsNaN(_hoverFloor)) return fallback;
        }
        return _hoverFloor;
    }

    /// <summary>After the body has moved: hover height, burrow upkeep, auras
    /// given and buffs received.</summary>
    private void StepExpansion(float dt, Vector3 playerPosition, float distance)
    {
        if (!_xTraits && !_buffed) return;
        EnemyDef? def = _def;
        if (def is null) return;

        if (def.Hover > 0f && !Game.Arena.Current.Underwater)
        {
            // Hold altitude over the ground below, dipping to head height to
            // strike. Measured from the floor, not the player: tracking the
            // player's feet made every flyer leap whenever the player jumped.
            float dip = Mathf.Clamp((distance - 1f) / 4f, 0.35f, 1f);
            float floor = HoverFloor(dt, playerPosition.Y);
            float target = floor + def.Hover * dip + MathF.Sin(_aliveSeconds * 2.1f + _hoverPhase) * 0.18f;
            Vector3 p = GlobalPosition;
            p.Y = Mathf.Lerp(p.Y, target, MathF.Min(1f, 4f * dt));
            GlobalPosition = p;
            Velocity = new Vector3(Velocity.X, 0f, Velocity.Z);
            _groundY = float.NegativeInfinity;
        }

        if (def.Behavior == EnemyBehavior.Burrower)
        {
            if (_burrowed)
            {
                // No contact swings from under the sand.
                _attackTimer = MathF.Max(_attackTimer, 0.25f);
                _trail?.SetDisc(GlobalPosition, MathF.Max(0.6f, _bodyRadius * 1.5f), 0f, TrailColor);
            }
            else if (_act == Act.None)
            {
                _surfacedFor += dt;
                if (_surfacedFor >= def.AbilityCooldown && distance > ReburrowDistance) Burrow();
            }
        }

        if (def.Aura != EnemyAura.None) StepAura(dt, def);

        // ---- buffs received
        if (_buffed)
        {
            _hasteLeft -= dt;
            _wardLeft -= dt;
            _frenzyLeft -= dt;
            if (_frenzyLeft > 0f)
            {
                _attackTimer -= dt * _frenzyStrength;
                if (_act == Act.None) _abilityTimer -= dt * _frenzyStrength;
            }
            if (_hasteLeft <= 0f && _wardLeft <= 0f && _frenzyLeft <= 0f) _buffed = false;
        }

        // Bosses own _speedScale (phase two); everything else uses it for the
        // burrow sprint and haste.
        if (!IsBoss)
            _speedScale = (_burrowed ? BurrowSpeed : 1f) * (_hasteLeft > 0f ? 1f + _hasteStrength : 1f);
    }

    private void StepAura(float dt, EnemyDef def)
    {
        Color tint = def.Aura switch
        {
            EnemyAura.Haste => new Color(0.55f, 0.85f, 1.00f, 0.45f),
            EnemyAura.Ward => new Color(0.85f, 0.85f, 0.95f, 0.45f),
            EnemyAura.Mend => new Color(0.40f, 1.00f, 0.55f, 0.45f),
            _ => new Color(1.00f, 0.35f, 0.30f, 0.45f),
        };
        Vector3 at = GlobalPosition;
        if (def.Hover > 0f) at.Y -= def.Hover;   // the ring sits on the floor
        _auraDecal?.SetBand(at, def.AuraRadius, 0.22f, tint);

        _auraTimer -= dt;
        if (_auraTimer > 0f) return;
        _auraTimer = AuraPulse;
        if (EnemyManager.Instance is not { } em) return;

        float r2 = def.AuraRadius * def.AuraRadius;
        var alive = em.AliveList;
        for (int i = 0; i < alive.Count; i++)
        {
            Enemy e = alive[i];
            if (!e.IsAlive) continue;
            Vector3 d = e.GlobalPosition - GlobalPosition;
            d.Y = 0f;
            if (d.LengthSquared() > r2) continue;
            e.ReceiveAura(def.Aura, def.AuraStrength);
        }
    }

    private void ReceiveAura(EnemyAura kind, float strength)
    {
        switch (kind)
        {
            case EnemyAura.Haste:
                if (IsBoss) return;
                _hasteLeft = BuffHold;
                _hasteStrength = strength;
                break;
            case EnemyAura.Ward:
                _wardLeft = BuffHold;
                _wardStrength = Mathf.Clamp(strength, 0f, 0.8f);
                break;
            case EnemyAura.Frenzy:
                _frenzyLeft = BuffHold;
                _frenzyStrength = MathF.Max(0f, strength);
                break;
            case EnemyAura.Mend:
                Restore(_maxHealth * strength * AuraPulse);
                return;
        }
        _buffed = true;
    }

    private void Restore(float amount)
    {
        if (_dead || amount <= 0f || _health >= _maxHealth) return;
        _health = MathF.Min(_maxHealth, _health + amount);
    }

    // ------------------------------------------------------------- damage

    /// <summary>Burrowed: the hit never happened. Shield and ward shrink it.
    /// Returns true when the hit is fully handled (nullified).</summary>
    private bool InterceptHit(DamageInfo info)
    {
        if (_burrowed)
        {
            info.Nullified = true;
            info.Amount = 0f;
            return true;
        }
        if (_cute) CuteIntercept(info);             // Enemy.Cute.cs
        if (_def is { ShieldArc: > 0f } def)
        {
            Vector3 dir = new(info.Direction.X, 0f, info.Direction.Z);
            if (dir.LengthSquared() > 1e-4f)
            {
                dir = dir.Normalized();
                Vector3 forward = new(MathF.Sin(Rotation.Y), 0f, MathF.Cos(Rotation.Y));
                // The hit travels along dir; it came from the front if it
                // travels against the way the body faces.
                if ((-dir).Dot(forward) >= MathF.Cos(Mathf.DegToRad(def.ShieldArc * 0.5f)))
                {
                    info.Amount *= 1f - Mathf.Clamp(def.ShieldBlock, 0f, 1f);
                    info.Knockback *= 0.3f;
                    if (_aliveSeconds >= _blockPopupAt)
                    {
                        _blockPopupAt = _aliveSeconds + 0.8f;
                        EventBus.Publish(new PopupRequested(AimPoint + Vector3.Up * (_halfHeight + 0.3f), "BLOCK", BlockColor, 0.8f));
                    }
                }
            }
        }
        if (_wardLeft > 0f) info.Amount *= 1f - _wardStrength;
        return false;
    }

}
