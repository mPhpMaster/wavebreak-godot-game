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
        if (_posed && _bodyMesh is not null) _bodyMesh.Scale = _bodyScale;
        _posed = false;
    }

    // ---------------------------------------------------------- locomotion

    /// <summary>Unit-scale steering for the idle state.</summary>
    private Vector3 Locomotion(Vector3 heading, float distance, bool hasTarget, Vector3 separation)
    {
        float seek;
        Vector3 strafe = Vector3.Zero;

        if (!hasTarget)
        {
            seek = 0f;
        }
        else if (!HasMoves && Behavior == EnemyBehavior.Shooter)
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
            // Ease off inside striking distance (see Step).
            seek = distance > _attackRange * 0.9f ? 1f : 0.2f;
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
                    if (_move is not null) ReleaseBoss(playerFeet, heading);
                    else ReleaseBasic(playerFeet);
                }
                break;

            case Act.Dash:
                _tele?.SetLane(_actFrom, _actDir, _actLaneLength, LaneHalfWidth, 1f, LaneColor);
                if (!_actHit && player is not null && distance <= _builtRadius + AssumedPlayerRadius + 0.35f)
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

    // -------------------------------------------------------------- bosses

    private void BeginBossMove(float distance, Vector3 heading, Vector3 playerFeet)
    {
        BossAttack? move = PickMove(distance);
        if (move is null)
        {
            _abilityTimer = 0.4f;
            return;
        }

        _move = move;
        _lastMove = move;
        if (move.Move == BossMove.Summon) _summonReadyAt = _aliveSeconds + SummonCooldown;
        _chainLeft = move.Move == BossMove.Charge && _phase >= 2 ? 1 : 0;
        _abilityTimer = _def!.AbilityCooldown * (_phase >= 2 ? 0.65f : 1f);
        _actTo = playerFeet;

        BeginWindUp(heading, move.WindUp * (_phase >= 2 ? 0.8f : 1f));
        if (move.Move is BossMove.Slam or BossMove.Charge or BossMove.Summon)
            _tele = EnemyAttacks.AcquireDecal();
    }

    private BossAttack? PickMove(float distance)
    {
        var moves = _def!.BossMoves;
        if (_forcedMove >= 0 && _forcedMove < moves.Count)
        {
            BossAttack forced = moves[_forcedMove];
            _forcedMove = -1;
            return forced;
        }

        float total = 0f;
        for (int pass = 0; pass < 2; pass++)
        {
            float roll = pass == 1 ? Dice.Range(0f, total) : 0f;
            for (int i = 0; i < moves.Count; i++)
            {
                BossAttack m = moves[i];
                float w = MoveWeight(m, distance);
                if (w <= 0f) continue;
                if (pass == 0) { total += w; continue; }
                roll -= w;
                if (roll <= 0f) return m;
            }
            if (total <= 0f) return null;
        }
        return null;
    }

    private float MoveWeight(BossAttack m, float distance)
    {
        if (m.MinPhase > _phase || distance < m.MinRange || distance > m.MaxRange) return 0f;
        float w = MathF.Max(0f, m.Weight);
        // Repeats are allowed but rare: the fight should feel like a moveset,
        // not one move on a timer.
        if (ReferenceEquals(m, _lastMove)) w *= 0.25f;
        // The new move should be what phase two is remembered for.
        if (_phase >= 2 && m.MinPhase >= 2) w *= 1.6f;
        if (m.Move == BossMove.Summon)
        {
            // Its own cooldown on top of the move gap: without it a phase-two
            // boss rolled Summon every other move and buried the arena in adds
            // (56 summons, ~330 polyps in one measured fight).
            if (_aliveSeconds < _summonReadyAt) w = 0f;
            if (EnemyManager.Instance is { } em && em.AliveCount >= em.MaxAlive - 10) w = 0f;
        }
        return w;
    }

    private void WindUpBoss(float t, Vector3 heading)
    {
        BossAttack m = _move!;
        switch (m.Move)
        {
            case BossMove.Slam:
                _tele?.SetDisc(GlobalPosition, m.Radius, t, EnemyAttacks.DangerColor);
                break;
            case BossMove.Charge:
                if (t < 0.5f) _actDir = heading;
                _actFrom = GlobalPosition;
                _actLaneLength = m.Radius;
                _tele?.SetLane(_actFrom, _actDir, m.Radius, LaneHalfWidth, t, LaneColor);
                break;
            case BossMove.Summon:
                _tele?.SetDisc(GlobalPosition, 3.2f, t, SummonColor);
                break;
            default:
                _actDir = heading;
                break;
        }
    }

    private void ReleaseBoss(Vector3 playerFeet, Vector3 heading)
    {
        BossAttack m = _move!;
        float damage = _abilityDamage * m.DamageScale;
        Vector3 at = GlobalPosition;
        string tag = SourceTag;

        switch (m.Move)
        {
            case BossMove.Slam:
            {
                EnemyAttacks.Blast(at, m.Radius, 0f, damage, EnemyAttacks.DangerColor, tag);
                EnemyAttacks.Flash(at, m.Radius, EnemyAttacks.DangerColor);
                // Rings reach far past the disc: stepping out of the circle is
                // not enough, the wave still has to be jumped.
                int rings = Math.Max(1, m.Count) + (_phase >= 2 ? 1 : 0);
                float reach = MathF.Min(28f, m.Radius * 5f);
                for (int i = 0; i < rings; i++)
                    EnemyAttacks.Shockwave(at, reach, m.Speed, 0.9f, i * 0.75f, damage * 0.6f, tag);
                Recover(0.8f);
                break;
            }

            case BossMove.RadialBurst:
            case BossMove.Barrage:
                _volleyIndex = 0;
                _volleysLeft = m.Move == BossMove.RadialBurst
                    ? (_phase >= 2 ? 2 : 1)
                    : Math.Max(1, m.Count) + (_phase >= 2 ? 2 : 0);
                _act = Act.Volley;
                _actTime = 0f;
                _actLength = 99f;
                FireVolley(playerFeet);
                break;

            case BossMove.Charge:
                StartDash(m.Radius, m.Speed);
                break;

            case BossMove.Summon:
            {
                int n = Math.Max(1, m.Count) + (_phase >= 2 ? Math.Max(1, m.Count / 2) : 0);
                if (Catalog.EnemyById(m.SummonId) is { } add && EnemyManager.Instance is { } em)
                {
                    float spin = Dice.Range(0f, MathF.Tau);
                    for (int i = 0; i < n; i++)
                    {
                        float a = spin + MathF.Tau * i / n;
                        em.SpawnAt(add, at + new Vector3(MathF.Cos(a), 0f, MathF.Sin(a)) * (_builtRadius + 2f));
                    }
                }
                EnemyAttacks.Flash(at, 3.2f, SummonColor);
                Recover(0.6f);
                break;
            }

            case BossMove.MeteorRain:
            {
                int n = Math.Max(1, m.Count) * (_phase >= 2 ? 3 : 2) / 2;
                for (int i = 0; i < n; i++)
                {
                    // The first always lands where the player stands, so moving
                    // is mandatory; the rest close off the easy escapes.
                    Vector3 spot = i == 0 ? playerFeet
                        : playerFeet + Dice.OnUnitCircleXZ() * Dice.Range(m.Radius * 1.2f, m.Radius * 3.5f);
                    spot.Y = at.Y;
                    EnemyAttacks.Blast(spot, m.Radius, m.Speed + i * 0.12f, damage, EnemyAttacks.DangerColor, tag);
                }
                Recover(0.5f);
                break;
            }
        }
    }

    private void FireVolley(Vector3 playerFeet)
    {
        BossAttack m = _move!;
        float damage = _abilityDamage * m.DamageScale;
        if (m.Move == BossMove.RadialBurst)
        {
            // Odd volleys rotate by half a gap, so standing in the hole of the
            // first ring is exactly where the second one goes.
            int count = Math.Max(3, m.Count);
            float offset = (_volleyIndex % 2) * MathF.PI / count + Rotation.Y;
            FireRing(count, offset, m.Speed, damage);
            _volleyGap = 0.45f;
        }
        else
        {
            Vector3 aim = FlatDir(playerFeet - GlobalPosition, _actDir);
            _actDir = aim;
            FireFan(aim, 5, 48f, m.Speed, damage, BossShotRadius);
            _volleyGap = 0.28f;
        }

        _volleyIndex++;
        if (--_volleysLeft <= 0) Recover(0.5f);
    }

    private void FireFan(Vector3 aim, int count, float spreadDegrees, float speed, float damage, float radius)
    {
        Vector3 muzzle = GlobalPosition + new Vector3(0f, ShotHeight, 0f) + aim * (_builtRadius + 0.2f);
        float spread = Mathf.DegToRad(spreadDegrees);
        for (int i = 0; i < count; i++)
        {
            float a = count == 1 ? 0f : -spread * 0.5f + spread * i / (count - 1);
            EnemyAttacks.Shoot(muzzle, aim.Rotated(Vector3.Up, a), speed, damage, radius, ShotRange, SourceTag);
        }
    }

    private void FireRing(int count, float offset, float speed, float damage)
    {
        Vector3 centre = GlobalPosition + new Vector3(0f, ShotHeight, 0f);
        for (int i = 0; i < count; i++)
        {
            float a = offset + MathF.Tau * i / count;
            Vector3 dir = new(MathF.Sin(a), 0f, MathF.Cos(a));
            EnemyAttacks.Shoot(centre + dir * (_builtRadius + 0.2f), dir, speed, damage, BossShotRadius, ShotRange, SourceTag);
        }
    }

    /// <summary>Below half health a boss enrages: new moves unlock (see
    /// <see cref="BossAttack.MinPhase"/>), tells shorten, gaps shrink, and it
    /// walks faster. Announced, because a rule change the player is not told
    /// about reads as the game cheating.</summary>
    private void CheckPhase()
    {
        if (_phase >= 2 || !HasMoves || _health > _maxHealth * 0.5f) return;
        _phase = 2;
        _speedScale = 1.15f;
        _abilityTimer = MathF.Min(_abilityTimer, 0.8f);
        ApplyFlash(1f);
        ContentAudit.NoteBossPhase(_def, _aliveSeconds);
        EventBus.Publish(new PopupRequested(AimPoint + Vector3.Up * (_halfHeight + 1.2f), "ENRAGED", EnrageColor, 1.5f));
    }

    // ---------------------------------------------------------------- death

    /// <summary>Splitters leave their brood where they fell. Through the
    /// manager, so the children are ordinary pooled, counted, targetable
    /// enemies and the live cap still holds.</summary>
    private void Split()
    {
        if (_def is not { SplitCount: > 0 } def || def.SplitInto.Length == 0) return;
        if (Catalog.EnemyById(def.SplitInto) is not { } child || EnemyManager.Instance is not { } em) return;

        Vector3 at = GlobalPosition;
        float spin = Dice.Range(0f, MathF.Tau);
        for (int i = 0; i < def.SplitCount; i++)
        {
            float a = spin + MathF.Tau * i / def.SplitCount;
            em.SpawnAt(child, at + new Vector3(MathF.Cos(a), 0f, MathF.Sin(a)) * 0.9f);
        }
        ContentAudit.NoteAbility(def, "Split");
    }

    // --------------------------------------------------------------- visuals

    /// <summary>Body language for the current act: exploders swell, leapers
    /// crouch, everything glows through its tell. Written only while something
    /// is happening, so idle enemies cost nothing here.</summary>
    private void Pose()
    {
        if (_bodyMesh is null) return;

        if (_act == Act.None)
        {
            if (!_posed) return;
            _posed = false;
            _glow = 0f;
            _bodyMesh.Scale = _bodyScale;
            ApplyFlash(_flashTimer > 0f ? _flashTimer / FlashDuration : 0f);
            return;
        }

        _posed = true;
        float t = Mathf.Clamp(_actTime / MathF.Max(0.01f, _actLength), 0f, 1f);
        Vector3 pose = Vector3.One;
        float glow = 0f;

        if (_act == Act.WindUp)
        {
            // A pulse that quickens toward the release: the rhythm is the timer.
            float pulse = 0.5f + 0.5f * MathF.Sin(_actTime * (10f + t * 22f));
            glow = 0.35f + 0.65f * t * pulse;
            if (_move is null)
            {
                pose = Behavior switch
                {
                    EnemyBehavior.Exploder => Vector3.One * (1f + 0.6f * t + 0.06f * pulse),
                    EnemyBehavior.Leaper => new Vector3(1f + 0.25f * t, 1f - 0.35f * t, 1f + 0.25f * t),
                    EnemyBehavior.Charger => new Vector3(1f, 1f - 0.12f * t, 1f + 0.12f * t),
                    _ => Vector3.One * (1f + 0.12f * t),
                };
            }
            else
            {
                pose = _move.Move == BossMove.Slam
                    ? new Vector3(1f + 0.1f * t, 1f + 0.18f * t, 1f + 0.1f * t)
                    : Vector3.One * (1f + 0.08f * t);
            }
        }
        else if (_act is Act.Dash or Act.Leap)
        {
            glow = 0.8f;
            pose = new Vector3(0.92f, 0.92f, 1.15f);
        }
        else if (_act == Act.Volley)
        {
            glow = 0.7f;
        }

        _glow = glow;
        _bodyMesh.Scale = _bodyScale * pose;
        ApplyFlash(_flashTimer > 0f ? _flashTimer / FlashDuration : 0f);
    }

    private static Vector3 Flat(Vector3 v) => new(v.X, 0f, v.Z);

    private static Vector3 FlatDir(Vector3 v, Vector3 fallback)
    {
        v.Y = 0f;
        float l = v.Length();
        return l > 1e-4f ? v / l : fallback;
    }
}
