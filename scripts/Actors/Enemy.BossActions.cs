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
    private bool IsWaterLeviathan => Game.Arena.Current.Underwater && _def?.Id == Visuals.ReefLeviathanModels.Id;
    private Vector3 BossAttackOrigin => IsWaterLeviathan ? AimPoint + Vector3.Up * .9f : GlobalPosition;
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
        if (move.Move is BossMove.Slam or BossMove.Charge or BossMove.Summon or BossMove.Riptide or BossMove.Surface or BossMove.Maelstrom)
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
                _tele?.SetDisc(BossAttackOrigin, m.Radius, t, EnemyAttacks.DangerColor);
                break;
            case BossMove.Charge:
                if (t < 0.5f) _actDir = heading;
                _actFrom = GlobalPosition;
                _actLaneLength = m.Radius;
                _tele?.SetLane(_actFrom, _actDir, m.Radius, LaneHalfWidth, t, LaneColor);
                break;
            case BossMove.Summon:
                _tele?.SetDisc(BossAttackOrigin, IsWaterLeviathan ? _builtRadius + 2 : 3.2f, t, SummonColor);
                break;
            default:
                if (!WindUpBossExpansion(m, t, heading)) _actDir = heading;
                break;
        }
    }

    private void ReleaseBoss(Vector3 playerFeet, Vector3 heading)
    {
        BossAttack m = _move!;
        float damage = _abilityDamage * m.DamageScale;
        Vector3 at = BossAttackOrigin;
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

            default:
                ReleaseBossExpansion(m, playerFeet, damage, tag);
                break;
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
            if (IsWaterLeviathan) aim = (playerFeet + Vector3.Up * .9f - BossAttackOrigin).Normalized();
            _actDir = aim;
            FireFan(aim, 5, 48f, m.Speed, damage, BossShotRadius);
            _volleyGap = 0.28f;
        }

        _volleyIndex++;
        if (--_volleysLeft <= 0) Recover(0.5f);
    }

    private void FireFan(Vector3 aim, int count, float spreadDegrees, float speed, float damage, float radius)
    {
        // Ordinary hovering shooters retain ground-height shots; the huge Reef boss fires from its body centre.
        float hover = _def?.Hover ?? 0f;
        Vector3 centre = IsWaterLeviathan ? BossAttackOrigin : GlobalPosition + new Vector3(0f, ShotHeight - hover, 0f);
        Vector3 muzzle = centre + aim * (_builtRadius + 0.2f);
        float spread = Mathf.DegToRad(spreadDegrees);
        for (int i = 0; i < count; i++)
        {
            float a = count == 1 ? 0f : -spread * 0.5f + spread * i / (count - 1);
            EnemyAttacks.Shoot(muzzle, aim.Rotated(Vector3.Up, a), speed, damage, radius, ShotRange, SourceTag);
        }
    }

    private void FireRing(int count, float offset, float speed, float damage)
    {
        Vector3 centre = IsWaterLeviathan ? BossAttackOrigin : GlobalPosition + new Vector3(0f, ShotHeight, 0f);
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
        if (_act == Act.None)
        {
            if (!_posed) return;
            _posed = false;
            WritePose(Vector3.One, 0f);
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

        if (_cute) CutePose(ref pose, ref glow);    // Enemy.Cute.cs
        WritePose(pose, glow);
    }

    /// <summary>Apply a pose to whichever body the enemy wears: the primitive
    /// (scale + material glow) or, when the visuals layer gave it a model, the
    /// rig (scale + the rig's own emission glow). An enraged boss keeps a low
    /// red glow between moves, so phase two is visible without the bar.</summary>
    private void WritePose(Vector3 pose, float glow)
    {
        _glow = glow;
        if (_bodyMesh is not null) _bodyMesh.Scale = _bodyScale * pose;
        ApplyFlash(_flashTimer > 0f ? _flashTimer / FlashDuration : 0f);

        if (_rig is null || !IsInstanceValid(_rig)) return;
        _rig.Scale = pose;
        if (glow > 0f) _rig.SetGlow(GlowColor, glow);
        else if (_statusTintAmount > 0f) _rig.SetGlow(_statusTint, _statusTintAmount * 1.4f);
        else _rig.SetGlow(EnrageColor, _phase >= 2 ? 0.3f : 0f);
    }

    private static Vector3 Flat(Vector3 v) => new(v.X, 0f, v.Z);

    private static Vector3 FlatDir(Vector3 v, Vector3 fallback)
    {
        v.Y = 0f;
        float l = v.Length();
        return l > 1e-4f ? v / l : fallback;
    }
}
