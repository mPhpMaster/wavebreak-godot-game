using System;
using Godot;
using Wavebreak.Combat;
using Wavebreak.Core;
using Wavebreak.Data;

namespace Wavebreak.Actors;

/// <summary>
/// What an enemy does besides walk at the player: the <see cref="EnemyBehavior"/>
/// strategies and the boss moveset runner.
///
/// Everything here is one small state machine — idle, wind-up, act, recover —
/// shared by every behaviour, because the shape of a fair attack is the same
/// whoever makes it: a tell the player can read, a commitment the attacker
/// cannot take back, and a recovery that is the player's window to punish.
/// Behaviours differ only in what they draw during the wind-up and what they do
/// when it ends.
///
/// Two rules hold throughout:
///
/// A wind-up never tracks the player to the end. Shots aim at the moment they
/// fire, but a charge locks its lane part-way through the tell and a leap locks
/// its landing at the start. A telegraph that keeps following you is not a
/// telegraph; it is a delay.
///
/// Damage leaves through <see cref="EnemyAttacks"/> or the player's own
/// <see cref="IDamageable.TakeDamage"/>, never around them, so i-frames from a
/// dash, evasion and armour answer every attack in this file.
/// </summary>
public sealed partial class Enemy
{
    private enum Act { None, WindUp, Dash, Leap, Volley, Recover }

    /// <summary>Recovery after an attack, seconds. The punish window.</summary>
    private const float RecoverTime = 0.55f;
    private const float LeapHeight = 3.2f;
    private const float ShotHeight = 1.0f;
    private const float ShotRadius = 0.3f;
    private const float BossShotRadius = 0.42f;
    private const float ShotRange = 34f;

    private static readonly Color LaneColor = new(1.00f, 0.42f, 0.22f, 0.8f);
    private static readonly Color LeapColor = new(1.00f, 0.55f, 0.25f, 0.8f);
    private static readonly Color SummonColor = new(0.72f, 0.40f, 1.00f, 0.75f);
    private static readonly Color EnrageColor = new(1.00f, 0.30f, 0.20f);

    private Act _act;
    private float _actTime;
    private float _actLength;
    private Vector3 _actDir = Vector3.Forward;
    private Vector3 _actFrom, _actTo;
    private float _actLaneLength;
    private bool _actHit;
    private float _abilityTimer;
    private float _abilityDamage;
    private float _dashSpeed;
    private int _volleysLeft;
    private float _volleyGap;
    private int _volleyIndex;
    private GroundDecal? _tele;
    private float _strafeSign = 1f;
    private float _aliveSeconds;

    private int _phase = 1;
    private float _speedScale = 1f;
    private BossAttack? _move;
    private BossAttack? _lastMove;
    private int _chainLeft;
    private int _forcedMove = -1;
    private float _summonReadyAt;

    /// <summary>Seconds between a boss's summons, whatever its weights say.</summary>
    private const float SummonCooldown = 15f;

    /// <summary>Distance beyond which a boss starts to hurry, and the most it
    /// hurries. A boss slower than the player that never reaches them is not a
    /// fight; measured, a kiting player kept Thresher Prime out of weapon range
    /// for most of an eight-minute "fight", and a 0.9 boost still left the
    /// slowest boss trailing at the edge of weapon range. At full catch-up a
    /// boss is a little faster than a walking player — it closes the gap, and
    /// the answer to it is a dash, not a straight line.</summary>
    private const float CatchUpFrom = 8f;
    private const float CatchUpBoost = 1.4f;

    private float _glow;
    private bool _posed;
    private Vector3 _bodyScale = Vector3.One;

    /// <summary>Random source for move choice and strafing. Set by the manager
    /// from the run's seeded stream so replays hold; a stand-alone enemy (the
    /// audit, the showcase) falls back to one seeded from its id.</summary>
    internal Rng? Random;

    /// <summary>1, or 2 once a boss has dropped below half health.</summary>
    public int Phase => _phase;

    /// <summary>True while winding up, acting or recovering.</summary>
    public bool Busy => _act != Act.None;

    private bool HasMoves => _def is { IsBoss: true } d && d.BossMoves is { Count: > 0 };
    private EnemyBehavior Behavior => _def?.Behavior ?? EnemyBehavior.Chaser;
    private Rng Dice => Random ??= new Rng(GetInstanceId());

    /// <summary>Make the next boss move this index into its moveset, bypassing
    /// weights, range and cooldown. For the audit and the showcase.</summary>
    internal void ForceMove(int index, float delay = 0f)
    {
        _forcedMove = index;
        _abilityTimer = delay;
    }

    // ----------------------------------------------------------- lifecycle

    private void ResetBehaviour()
    {
        EndBehaviour();
        _phase = 1;
        _speedScale = 1f;
        _aliveSeconds = 0f;
        _lastMove = null;
        _forcedMove = -1;
        _summonReadyAt = 0f;
        _abilityTimer = 2.5f;

        // Plain chasers draw nothing from the run's random stream. That keeps a
        // seeded run identical to the pre-behaviour game until the first
        // behaviour monster appears, which is what makes "the opening is no
        // harder" a measurement instead of a claim.
        if (HasMoves || Behavior == EnemyBehavior.Chaser) return;

        _strafeSign = Dice.Chance(0.5f) ? 1f : -1f;
        // Staggered like the contact swing, so a pack that arrives together
        // does not fire one synchronised volley.
        _abilityTimer = (_def?.AbilityCooldown ?? 3f) * Dice.Range(0.35f, 0.9f);
    }

    /// <summary>Drop whatever the enemy was doing and hand back its decal.
    /// Runs on every death and despawn, so a telegraph never outlives its owner.</summary>
    private void EndBehaviour()
    {
        _act = Act.None;
        _move = null;
        _chainLeft = 0;
        _volleysLeft = 0;
        EnemyAttacks.ReleaseDecal(_tele);
        _tele = null;
        _glow = 0f;
        if (_posed) WritePose(Vector3.One, 0f);
        _posed = false;
        EndExpansion();
    }

    // ---------------------------------------------------------- locomotion

    /// <summary>Unit-scale steering for the idle state.</summary>
    private Vector3 Locomotion(Vector3 heading, float distance, bool hasTarget, Vector3 separation)
    {
        if (_cute && CuteLocomotion(heading, distance, hasTarget, separation, out Vector3 cuteWish)) return cuteWish;
        float seek;
        Vector3 strafe = Vector3.Zero;

        if (!hasTarget)
        {
            seek = 0f;
        }
        else if (!HasMoves && HoldsRange)
        {
            // Hold a band, not a point: a shooter that parks on an exact radius
            // is a turret, and one that circles inside the band makes the
            // player move to line it up. It never backs away, though: most
            // weapons reach 2-6m, and a shooter that retreats from a player who
            // walks at it cannot be killed by half the arsenal (measured: about
            // one in five died while they backpedalled).
            float preferred = MathF.Max(3f, _def!.AbilityRange);
            seek = distance > preferred * 1.15f ? 1f : 0f;
            if (seek == 0f) strafe = new Vector3(heading.Z, 0f, -heading.X) * (_strafeSign * 0.55f);
        }
        else
        {
            // Ease off inside striking distance (see Step). Edge-to-edge, the
            // same rule the swing itself uses.
            seek = distance > ContactDistance * 0.95f ? 1f : 0.2f;
            if (HasMoves) seek *= 1f + CatchUpBoost * Mathf.Clamp((distance - CatchUpFrom) / CatchUpFrom, 0f, 1f);
        }

        Vector3 desired = heading * seek + strafe + separation * SeparationWeight;
        // Clamp to the throttle actually asked for (above 1 for a hurrying boss),
        // so separation cannot add speed and catch-up is not clipped away.
        float cap = MathF.Max(1f, seek);
        if (desired.LengthSquared() > cap * cap) desired = desired.Normalized() * cap;
        return desired;
    }

    // ----------------------------------------------------------- decisions

    private void StepBehaviour(float dt, Vector3 playerFeet, IDamageable? player, float distance, Vector3 heading)
    {
        if (_def is null) return;
        if (_cute) StepCute(dt, playerFeet, player, distance, heading);   // Enemy.Cute.cs

        if (_act != Act.None)
        {
            _actTime += dt;
            Advance(dt, playerFeet, player, distance, heading);
            Pose();
            return;
        }

        _abilityTimer -= dt;
        Pose();
        if (player is null || !player.IsAlive || _abilityTimer > 0f) return;

        if (HasMoves)
        {
            BeginBossMove(distance, heading, playerFeet);
            return;
        }

        EnemyDef def = _def;
        switch (def.Behavior)
        {
            case EnemyBehavior.Shooter:
                if (distance <= MathF.Max(3f, def.AbilityRange) * 1.45f)
                    BeginWindUp(heading, def.WindUp);
                break;

            case EnemyBehavior.Charger:
                if (distance <= def.AbilityRange && distance >= 2f)
                {
                    BeginWindUp(heading, def.WindUp);
                    _actLaneLength = Mathf.Clamp(distance + 3.5f, 6f, def.AbilityRange + 3.5f);
                    _dashSpeed = MathF.Max(def.AbilitySpeed, _speed * 2f);
                    _tele = EnemyAttacks.AcquireDecal();
                }
                break;

            case EnemyBehavior.Exploder:
                if (distance <= def.AbilityRange)
                {
                    BeginWindUp(heading, def.WindUp);
                    _tele = EnemyAttacks.AcquireDecal();
                }
                break;

            case EnemyBehavior.Leaper:
                if (distance <= def.AbilityRange && distance >= 2.5f)
                {
                    BeginWindUp(heading, def.WindUp);
                    // Locked now. The marker is a promise about where it lands.
                    _actTo = new Vector3(playerFeet.X, GlobalPosition.Y, playerFeet.Z);
                    _tele = EnemyAttacks.AcquireDecal();
                }
                break;

            default:
                BeginExpansion(def, distance, heading, playerFeet);
                break;
        }
    }

    private void BeginWindUp(Vector3 heading, float seconds)
    {
        _act = Act.WindUp;
        _actTime = 0f;
        _actLength = MathF.Max(0.05f, seconds);
        _actDir = heading.LengthSquared() > 1e-6f ? heading : _actDir;
        _actHit = false;
        _tag = _move is null ? (_def?.Id ?? "enemy") : MoveTag(_def!.Id, _move.Move);
        _rig?.PlayAttackWindup(_actLength);
        if (ContentAudit.Logging)
            ContentAudit.NoteAbility(_def, _move is null ? _def!.Behavior.ToString() : _move.Move.ToString());
    }

    private void Recover(float seconds)
    {
        EnemyAttacks.ReleaseDecal(_tele);
        _tele = null;
        _act = Act.Recover;
        _actTime = 0f;
        _actLength = seconds;
    }

    // ------------------------------------------------------------ the acts

    private void Advance(float dt, Vector3 playerFeet, IDamageable? player, float distance, Vector3 heading)
    {
        float t = _actTime / _actLength;
        switch (_act)
        {
            case Act.WindUp:
                if (_move is not null) WindUpBoss(t, heading);
                else WindUpBasic(t, heading);
                if (t >= 1f)
                {
                    _rig?.PlayAttack();
                    if (_move is not null) ReleaseBoss(playerFeet, heading);
                    else ReleaseBasic(playerFeet);
                }
                break;

            case Act.Dash:
                _tele?.SetLane(_actFrom, _actDir, _actLaneLength, LaneHalfWidth, 1f, LaneColor);
                if (!_actHit && player is not null && distance <= ContactDistance + 0.2f)
                {
                    _actHit = true;
                    float scale = _move?.DamageScale ?? 1f;
                    EnemyAttacks.Hit(player, _abilityDamage * scale, _actDir, IsBoss ? 3f : 1.6f, SourceTag);
                }
                if (t >= 1f) EndDash(heading);
                break;

            case Act.Volley:
                _volleyGap -= dt;
                if (_volleyGap <= 0f) FireVolley(playerFeet);
                break;

            case Act.Recover:
                if (t >= 1f) _act = Act.None;
                break;
        }
    }

    private float LaneHalfWidth => MathF.Max(_builtRadius * 1.1f, _def?.AbilityRadius ?? 1f);

    /// <summary>DamageInfo.Source for the current attack: "id" or "id:Move".
    /// Built once per move from a cache, because a 24-shot ring would otherwise
    /// format the same string 24 times.</summary>
    private string SourceTag => _tag;
    private string _tag = "enemy";

    private static readonly System.Collections.Generic.Dictionary<(string, BossMove), string> MoveTags = new();

    private static string MoveTag(string id, BossMove move)
    {
        if (!MoveTags.TryGetValue((id, move), out string? tag))
            MoveTags[(id, move)] = tag = $"{id}:{move}";
        return tag;
    }

    private void WindUpBasic(float t, Vector3 heading)
    {
        EnemyDef def = _def!;
        switch (def.Behavior)
        {
            case EnemyBehavior.Shooter:
                _actDir = heading;            // aims until it fires; the tell is the glow
                break;
            case EnemyBehavior.Charger:
                if (t < 0.55f) _actDir = heading;   // then the lane is locked
                _actFrom = GlobalPosition;
                _tele?.SetLane(_actFrom, _actDir, _actLaneLength, LaneHalfWidth, t, LaneColor);
                break;
            case EnemyBehavior.Exploder:
                _tele?.SetDisc(GlobalPosition, def.AbilityRadius, t, EnemyAttacks.DangerColor);
                break;
            case EnemyBehavior.Leaper:
                _actDir = FlatDir(_actTo - GlobalPosition, _actDir);
                _tele?.SetDisc(_actTo, def.AbilityRadius, t * 0.5f, LeapColor);
                break;
            default:
                WindUpExpansion(t, heading);
                break;
        }
    }

    private void ReleaseBasic(Vector3 playerFeet)
    {
        EnemyDef def = _def!;
        float cooldown = def.AbilityCooldown * Dice.Range(0.85f, 1.2f);
        switch (def.Behavior)
        {
            case EnemyBehavior.Shooter:
            {
                Vector3 aim = FlatDir(playerFeet - GlobalPosition, _actDir);
                FireFan(aim, Math.Max(1, def.AbilityCount), def.AbilitySpread, def.AbilitySpeed, _abilityDamage, ShotRadius);
                _abilityTimer = cooldown;
                if (Dice.Chance(0.4f)) _strafeSign = -_strafeSign;
                Recover(0.3f);
                break;
            }
            case EnemyBehavior.Charger:
                StartDash(_actLaneLength, _dashSpeed);
                _abilityTimer = cooldown;
                break;
            case EnemyBehavior.Exploder:
                EnemyAttacks.Blast(GlobalPosition, def.AbilityRadius, 0f, _abilityDamage, EnemyAttacks.DangerColor, SourceTag);
                EnemyAttacks.Flash(GlobalPosition, def.AbilityRadius, EnemyAttacks.DangerColor);
                ContentAudit.NoteAbility(def, "Detonate");
                // Spent, not killed: no xp for letting one go off in your face.
                Despawn();
                break;
            case EnemyBehavior.Leaper:
                _actFrom = GlobalPosition;
                _act = Act.Leap;
                _actTime = 0f;
                _actLength = 0.5f + Flat(_actTo - _actFrom).Length() * 0.025f;
                _abilityTimer = cooldown;
                // Off the ground: no weapon knockback or steering mid-arc.
                _knockback = Vector3.Zero;
                break;
            default:
                ReleaseExpansion(playerFeet);
                break;
        }
    }

    private void StartDash(float length, float speed)
    {
        _actFrom = GlobalPosition;
        _act = Act.Dash;
        _actTime = 0f;
        _actLength = length / MathF.Max(1f, speed);
        _actHit = false;
        _dashSpeed = speed;
        _knockback = Vector3.Zero;
    }

    private void EndDash(Vector3 heading)
    {
        if (_chainLeft > 0 && _move is not null)
        {
            // Phase-two follow-up: a shorter tell, from wherever the first
            // charge ended, at wherever the player went.
            _chainLeft--;
            BeginWindUp(heading, 0.45f);
            ContentAudit.NoteAbility(_def, "ChargeChain");
            return;
        }
        Recover(_move is null ? 0.7f : 0.9f);
    }

    /// <summary>The scripted arc from <c>_actFrom</c> to <c>_actTo</c>.</summary>
    private void StepLeap(float dt)
    {
        _actTime += dt;
        float t = Mathf.Clamp(_actTime / _actLength, 0f, 1f);
        Vector3 p = _actFrom.Lerp(_actTo, t);
        p.Y += MathF.Sin(t * MathF.PI) * LeapHeight;
        GlobalPosition = p;
        Velocity = Vector3.Zero;
        float radius = _def?.AbilityRadius ?? 2f;
        _tele?.SetDisc(_actTo, radius, 0.5f + t * 0.5f, LeapColor);
        Pose();

        if (t < 1f) return;
        GlobalPosition = _actTo;
        EnemyAttacks.Blast(_actTo, radius, 0f, _abilityDamage, LeapColor, SourceTag);
        EnemyAttacks.Flash(_actTo, radius, LeapColor);
        Recover(RecoverTime);
    }

}
