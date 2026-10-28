using System;
using Godot;
using Wavebreak.Combat;
using Wavebreak.Core;
using Wavebreak.Data;

namespace Wavebreak.Actors;

public sealed partial class Enemy
{
    // ---------------------------------------------------------- behaviours

    private void BeginExpansion(EnemyDef def, float distance, Vector3 heading, Vector3 playerFeet)
    {
        switch (def.Behavior)
        {
            case EnemyBehavior.Burrower:
                if (_burrowed && distance <= MathF.Max(1.5f, def.AbilityRange))
                {
                    BeginWindUp(heading, def.WindUp);
                    _tele = EnemyAttacks.AcquireDecal();
                }
                break;

            case EnemyBehavior.Healer:
                if (distance > MathF.Max(3f, def.AbilityRange) * 1.6f) break;
                if (!HasInjuredAlly(def.AbilityRadius))
                {
                    _abilityTimer = 1f;
                    break;
                }
                BeginWindUp(heading, def.WindUp);
                _tele = EnemyAttacks.AcquireDecal();
                break;

            case EnemyBehavior.Artillery:
                if (distance <= MathF.Max(3f, def.AbilityRange) * 1.3f)
                    BeginWindUp(heading, def.WindUp);
                break;

            case EnemyBehavior.Blinker:
                if (distance >= 4f && distance <= def.AbilityRange)
                {
                    BeginWindUp(heading, def.WindUp);
                    // Locked now: beside or behind the player, never on them.
                    Vector3 off = Dice.OnUnitCircleXZ() * 2.6f;
                    _actTo = new Vector3(playerFeet.X + off.X, playerFeet.Y, playerFeet.Z + off.Z);
                    _tele = EnemyAttacks.AcquireDecal();
                }
                break;

            default:
                BeginCute(def, distance, heading);      // Enemy.Cute.cs
                break;
        }
    }

    private void WindUpExpansion(float t, Vector3 heading)
    {
        EnemyDef def = _def!;
        switch (def.Behavior)
        {
            case EnemyBehavior.Burrower:
                _tele?.SetDisc(GlobalPosition, def.AbilityRadius, t, EnemyAttacks.DangerColor);
                _trail?.SetDisc(GlobalPosition, MathF.Max(0.6f, _bodyRadius * 1.5f), 0f, TrailColor);
                break;
            case EnemyBehavior.Healer:
                _tele?.SetDisc(GlobalPosition with { Y = GlobalPosition.Y - def.Hover }, def.AbilityRadius, t, HealColor);
                break;
            case EnemyBehavior.Artillery:
                _actDir = heading;
                break;
            case EnemyBehavior.Blinker:
                _tele?.SetDisc(_actTo, MathF.Max(0.8f, _bodyRadius * 1.8f), t, BlinkColor);
                break;
            default:
                WindUpCute(t, heading);                 // Enemy.Cute.cs
                break;
        }
    }

    private void ReleaseExpansion(Vector3 playerFeet)
    {
        EnemyDef def = _def!;
        float cooldown = def.AbilityCooldown * Dice.Range(0.85f, 1.2f);
        switch (def.Behavior)
        {
            case EnemyBehavior.Burrower:
            {
                Vector3 at = GlobalPosition;
                Emerge();
                EnemyAttacks.Blast(at, def.AbilityRadius, 0f, _abilityDamage, EnemyAttacks.DangerColor, SourceTag);
                EnemyAttacks.Flash(at, def.AbilityRadius, TrailColor);
                ContentAudit.NoteAbility(def, "Erupt");
                Recover(RecoverTime);
                break;
            }
            case EnemyBehavior.Healer:
            {
                int healed = HealAllies(def.AbilityRadius, def.SupportAmount);
                EnemyAttacks.Flash(GlobalPosition with { Y = GlobalPosition.Y - def.Hover }, def.AbilityRadius, HealColor);
                if (healed > 0)
                    EventBus.Publish(new PopupRequested(AimPoint + Vector3.Up * (_halfHeight + 0.4f), "MEND", HealColor, 0.9f));
                _abilityTimer = cooldown;
                Recover(0.4f);
                break;
            }
            case EnemyBehavior.Artillery:
            {
                int shells = Math.Max(1, def.AbilityCount);
                for (int i = 0; i < shells; i++)
                {
                    // The first lands where the player stands; the rest close
                    // off the nearby escapes.
                    Vector3 spot = i == 0 ? playerFeet
                        : playerFeet + Dice.OnUnitCircleXZ() * Dice.Range(def.AbilityRadius * 1.1f, def.AbilityRadius * 2.8f);
                    spot.Y = playerFeet.Y;
                    EnemyAttacks.Blast(spot, def.AbilityRadius, def.ShellDelay + i * 0.12f, _abilityDamage, ShellColor, SourceTag);
                }
                _abilityTimer = cooldown;
                Recover(0.4f);
                break;
            }
            case EnemyBehavior.Blinker:
            {
                EnemyAttacks.Flash(GlobalPosition, 1.2f, BlinkColor);
                Vector3 to = _actTo;
                if (def.Hover > 0f) to.Y += def.Hover;
                GlobalPosition = to;
                Velocity = Vector3.Zero;
                _knockback = Vector3.Zero;
                _groundY = MathF.Min(_groundY, _actTo.Y);
                EnemyAttacks.Flash(_actTo, 1.4f, BlinkColor);
                // Arrives swinging: the punish for ignoring the marker.
                _attackTimer = MathF.Min(_attackTimer, 0.3f);
                _abilityTimer = cooldown;
                Recover(0.35f);
                break;
            }
            default:
                if (!ReleaseCute(playerFeet)) Recover(RecoverTime);   // Enemy.Cute.cs
                break;
        }
    }

    private bool HasInjuredAlly(float radius)
    {
        if (EnemyManager.Instance is not { } em) return false;
        float r2 = radius * radius;
        var alive = em.AliveList;
        for (int i = 0; i < alive.Count; i++)
        {
            Enemy e = alive[i];
            if (e == this || !e.IsAlive || e.IsBoss || e._health >= e._maxHealth * 0.95f) continue;
            Vector3 d = e.GlobalPosition - GlobalPosition;
            d.Y = 0f;
            if (d.LengthSquared() <= r2) return true;
        }
        return false;
    }

    private int HealAllies(float radius, float fraction)
    {
        if (EnemyManager.Instance is not { } em) return 0;
        float r2 = radius * radius;
        int n = 0;
        var alive = em.AliveList;
        for (int i = 0; i < alive.Count; i++)
        {
            Enemy e = alive[i];
            // Bosses are not healed: a boss fight must always make progress.
            if (!e.IsAlive || e.IsBoss || e._health >= e._maxHealth) continue;
            Vector3 d = e.GlobalPosition - GlobalPosition;
            d.Y = 0f;
            if (d.LengthSquared() > r2) continue;
            e.Restore(e._maxHealth * fraction);
            n++;
        }
        ContentAudit.NoteAbility(_def, "Heal");
        return n;
    }

    // -------------------------------------------------------------- bosses

    private float RiptideLength(BossAttack m) => MathF.Max(8f, Math.Max(1, m.Count) * m.Radius * 1.5f);

    /// <summary>Telegraphs for the expansion moves. False for a move this
    /// file does not own.</summary>
    private bool WindUpBossExpansion(BossAttack m, float t, Vector3 heading)
    {
        switch (m.Move)
        {
            case BossMove.Riptide:
                if (t < 0.5f) _actDir = heading;   // then the lane is locked
                _actFrom = GlobalPosition;
                _actLaneLength = RiptideLength(m);
                _tele?.SetLane(_actFrom, _actDir, _actLaneLength, m.Radius, t, LaneColor);
                return true;
            case BossMove.Surface:
                // _actTo was locked to the player's feet when the move began.
                _tele?.SetDisc(_actTo, m.Radius, t, EnemyAttacks.DangerColor);
                return true;
            case BossMove.Maelstrom:
                _tele?.SetDisc(_actTo, m.Radius, t, EnemyAttacks.DangerColor);
                return true;
        }
        return false;
    }

    private void ReleaseBossExpansion(BossAttack m, Vector3 playerFeet, float damage, string tag)
    {
        switch (m.Move)
        {
            case BossMove.Riptide:
            {
                int n = Math.Max(1, m.Count) + (_phase >= 2 ? 2 : 0);
                float length = RiptideLength(m) * (_phase >= 2 ? 1.25f : 1f);
                float step = length / n;
                for (int i = 0; i < n; i++)
                {
                    Vector3 spot = _actFrom + _actDir * (step * (i + 0.5f));
                    EnemyAttacks.Blast(spot, m.Radius, 0.05f + i * 0.09f, damage, EnemyAttacks.DangerColor, tag);
                }
                Recover(0.7f);
                break;
            }
            case BossMove.Surface:
            {
                EnemyAttacks.Flash(GlobalPosition, _builtRadius + 1.5f, SummonColor);
                GlobalPosition = _actTo;
                Velocity = Vector3.Zero;
                _knockback = Vector3.Zero;
                _groundY = MathF.Min(_groundY, _actTo.Y);
                EnemyAttacks.Blast(_actTo, m.Radius, 0f, damage, EnemyAttacks.DangerColor, tag);
                EnemyAttacks.Flash(_actTo, m.Radius, EnemyAttacks.DangerColor);
                int rings = Math.Max(1, m.Count) + (_phase >= 2 ? 1 : 0);
                float reach = MathF.Min(24f, m.Radius * 4f);
                for (int i = 0; i < rings; i++)
                    EnemyAttacks.Shockwave(_actTo, reach, MathF.Max(6f, m.Speed), 0.9f, 0.15f + i * 0.7f, damage * 0.5f, tag);
                Recover(0.9f);
                break;
            }
            case BossMove.Maelstrom:
            {
                // The centre goes off now (it was the tell); the ring around it
                // fills after, so the player who ran far must come back in.
                EnemyAttacks.Blast(_actTo, m.Radius, 0f, damage, EnemyAttacks.DangerColor, tag);
                int n = Math.Max(4, m.Count) + (_phase >= 2 ? 2 : 0);
                float ring = m.Radius * 2.4f;
                float spin = Dice.Range(0f, MathF.Tau);
                for (int i = 0; i < n; i++)
                {
                    float a = spin + MathF.Tau * i / n;
                    Vector3 spot = _actTo + new Vector3(MathF.Cos(a), 0f, MathF.Sin(a)) * ring;
                    EnemyAttacks.Blast(spot, m.Radius, 0.9f, damage, EnemyAttacks.DangerColor, tag);
                }
                Recover(0.6f);
                break;
            }
            default:
                Recover(0.5f);
                break;
        }
    }
}
