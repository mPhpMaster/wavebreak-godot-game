using System;
using System.Reflection;
using Godot;
using Wavebreak.Combat;
using Wavebreak.Core;
using Wavebreak.Data;
using Wavebreak.Stats;

namespace Wavebreak.Actors;

public sealed partial class Enemy
{
    public void Step(float dt, Vector3 playerPosition, IDamageable? player, Vector3 separation)
    {
        if (_dead) return;
        UpdateMarinePose(Velocity);

        UpdateFlash(dt);
        _aliveSeconds += dt;

        StepStatus(dt);                                 // burn/poison may kill
        if (_dead) return;

        if (StepCityGiant(dt, playerPosition, player)) return;

        // Mid-leap the body is on a scripted arc, not under steering or physics.
        if (_act == Act.Leap)
        {
            StepLeap(dt);
            return;
        }

        Vector3 position = GlobalPosition;
        Vector3 toPlayer = playerPosition - position;
        toPlayer.Y = 0f;

        float distance = toPlayer.Length();
        Vector3 heading = distance > 0.001f ? toPlayer / distance : Vector3.Zero;
        heading = SteerAroundBlockers(position, heading, playerPosition, distance);   // Enemy.Blockers.cs

        // Ease off the throttle inside striking distance. Enemies that keep
        // driving forward at full speed shove each other through the player and
        // produce a jittering pile instead of a ring.
        bool hasTarget = player is not null;

        // Behaviours replace the throttle, never the physics below it: a
        // charging or winding-up enemy is still shoved, still falls, still
        // slides along walls.
        Vector3 desired;
        float acceleration = Acceleration;
        if (_act == Act.Dash)
        {
            desired = _actDir * _dashSpeed;
            acceleration = DashAcceleration;
        }
        else if (StatusHeld)
        {
            desired = Vector3.Zero;                     // frozen or stunned
        }
        else if (_act != Act.None)
        {
            // Planted while telegraphing: a wind-up that drifts toward the
            // player makes the telegraph lie about where the attack will be.
            desired = separation * (SeparationWeight * 0.25f * _speed);
        }
        else
        {
            desired = Locomotion(heading, distance, hasTarget, separation) * (_speed * _speedScale * StatusSpeedScale);
        }

        Vector3 velocity = Velocity;
        Vector3 horizontal = new(velocity.X, 0f, velocity.Z);
        horizontal -= _knockback;                       // steer the self-driven part only
        horizontal = horizontal.MoveToward(desired, acceleration * dt);

        _knockback = _knockback.MoveToward(Vector3.Zero, KnockbackDamping * dt);

        float verticalSpeed = velocity.Y;
        bool swimming = Game.Arena.Current.Underwater;
        if (swimming)
            verticalSpeed = StatusHeld ? 0f : Mathf.Clamp((playerPosition.Y + (_def?.Hover ?? 0f) - position.Y) * 2f, -3.5f, 3.5f);
        else if (IsOnFloor()) { if (verticalSpeed < 0f) verticalSpeed = 0f; }
        else verticalSpeed -= Gravity * dt;

        Velocity = new Vector3(horizontal.X + _knockback.X, verticalSpeed, horizontal.Z + _knockback.Z);
        if (!swimming && CoastThisTick(distance))
        {
            // Far from the player and walking on flat ground: a plain step is
            // indistinguishable at this range, and the next real slide resolves
            // any wall it nudged into. MoveAndSlide is ~50us an enemy, which
            // was most of the frame once the crowd filled up.
            Vector3 v = Velocity;
            GlobalPosition += new Vector3(v.X, 0f, v.Z) * dt;
        }
        else
        {
            using (Wavebreak.Game.Prof.Scope("Enemy.MoveAndSlide")) MoveAndSlide();
        }

        // A level without a floor collider would otherwise drop every enemy out
        // of the world. Spawn height is the floor of last resort.
        Vector3 after = GlobalPosition;
        if (swimming)
        {
            float surface = Game.WaterSurfaceHeight.At(after) - _halfHeight * 2f - 0.3f;
            if (after.Y > surface)
            { after.Y = surface; GlobalPosition = after; Velocity = Velocity with { Y = Mathf.Min(0, Velocity.Y) }; }
        }
        UpdateMarinePose(Velocity);
        if (!swimming && after.Y < _groundY)
        {
            after.Y = _groundY;
            GlobalPosition = after;
            Velocity = new Vector3(Velocity.X, 0f, Velocity.Z);
        }
        StepExpansion(dt, playerPosition, distance);

        // Frozen or stunned: physics still applies, but no turning, no
        // behaviour clock and no swing.
        if (StatusHeld) return;

        // Face the committed direction during an action, so the body points
        // down the lane it is about to run rather than at where the player went.
        Vector3 face = _act == Act.None ? heading : _actDir;
        if (distance > 0.05f || _act != Act.None)
        {
            float target = MathF.Atan2(face.X, face.Z);
            Rotation = new Vector3(0f, Mathf.LerpAngle(Rotation.Y, target, MathF.Min(1f, TurnSpeed * dt)), 0f);
        }

        StepBehaviour(dt, playerPosition, player, distance, heading);
        if (_dead) return;                              // an exploder can end itself above

        _attackTimer -= dt;
        if (player is not null
            && _act == Act.None
            && _attackTimer <= 0f
            && player.IsAlive
            && distance <= ContactDistance
            && WithinReachVertically(playerPosition.Y))
        {
            Strike(player, heading);
        }
    }

    /// <summary>Contact range was measured flat, so a player jumping or gliding
    /// metres above a crowd was still "touched" by everything under them. The
    /// player's feet must be no higher than just over this body's top, and no
    /// lower than a little under its feet.</summary>
    private bool WithinReachVertically(float playerFeetY)
    {
        float feet = GlobalPosition.Y - (_def?.Hover ?? 0f);
        float top = GlobalPosition.Y + _halfHeight * 2f;
        return playerFeetY <= top + 0.6f && playerFeetY >= feet - 1.0f;
    }

    private void Strike(IDamageable player, Vector3 heading)
    {
        _attackTimer = _attackCooldown;

        // Fresh instance per swing rather than a reused buffer: PlayerDamaged
        // carries this reference, and anything that keeps a short damage log
        // would find its history rewritten underneath it. Contact attacks are
        // gated by a cooldown, so this is a handful of small objects a second —
        // not the per-frame allocation the pooling rule is aimed at.
        DamageInfo hit = new()
        {
            Amount = _contactDamage,
            Element = Element.Physical,
            CanCrit = false,
            Knockback = IsBoss ? 1.6f : 0.7f,
            Direction = heading,
            ProcCoefficient = 1f,
            Source = _def?.Id ?? "enemy",
        };
        player.TakeDamage(hit);
        _rig?.PlayAttack();

        // Recoil, so a hit reads as a lunge and the attacker briefly breaks
        // contact instead of grinding against the player at zero distance.
        AddKnockback(-heading, 0.55f);

        // Thorns answer the swing, not the damage: a hit swallowed by the
        // player's invulnerability window still touched the barbs. Without that,
        // the window would cap thorns at one enemy per half-second and the stat
        // could never do what it promises — punish being surrounded.
        float thorns = PlayerRef.Thorns;
        if (thorns > 0f) Retaliate(thorns, heading);
    }

    /// <summary>Total thorns damage dealt this process, for verification logs.</summary>
    public static float ThornsDealt { get; private set; }

    /// <summary>Thorns retaliations that landed this process.</summary>
    public static int ThornsHits { get; private set; }

    private static readonly Color ThornsColor = new(0.80f, 0.92f, 0.70f);

    private void Retaliate(float amount, Vector3 heading)
    {
        DamageInfo back = new()
        {
            Amount = amount,
            Element = Element.Physical,
            CanCrit = false,
            Knockback = 0f,
            Direction = -heading,
            // Zero: a retaliation must not set off on-hit effects, or thorns
            // would feed lifesteal and the two would loop into immortality.
            ProcCoefficient = 0f,
            Source = "thorns",
        };

        TakeDamage(back);
        if (back.Nullified || back.Amount <= 0f) return;

        ThornsHits++;
        ThornsDealt += back.Amount;

        if (back.Amount >= 0.5f)
            EventBus.Publish(new PopupRequested(AimPoint + Vector3.Up * 0.35f,
                Mathf.RoundToInt(back.Amount).ToString(), ThornsColor, 0.8f));
    }

}
