using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Data;
using Wavebreak.Stats;

namespace Wavebreak.Combat.Weapons;

internal sealed class ReefShotgun : IWeaponBehavior
{
    public void Fire(WeaponRuntime weapon, WeaponSystem system, StatSheet stats)
    {
        Vector3 origin = system.MuzzlePosition;
        Vector3 heading = system.AimAt(origin, weapon.Def.Range * (1 + weapon.Bonus.Range));
        if (system.Body is Player owner) origin = owner.PrepareShotgunMuzzle(heading);
        Vector3 right = heading.Cross(Vector3.Up).Normalized();
        if (right.LengthSquared() < .1f) right = Vector3.Right;
        Vector3 up = right.Cross(heading).Normalized();
        int count = Math.Clamp(weapon.BaseProjectiles + (int)stats.Get(Stat.ProjectileCount), 1, 32);
        float spread = Mathf.Tan(Mathf.DegToRad(weapon.Def.SpreadDegrees / 2));
        for (int i = 0; i < count; i++)
        {
            float angle = i * 2.39996f;
            float radius = i == 0 ? 0 : spread * Mathf.Sqrt(i / (float)Math.Max(1, count - 1));
            var spec = new ProjectileSpec
            {
                Mode = ProjectileMode.Straight, Origin = origin,
                Direction = (heading + right * (Mathf.Cos(angle) * radius) + up * (Mathf.Sin(angle) * radius)).Normalized(),
                Speed = weapon.Def.ProjectileSpeed * (1 + weapon.Bonus.ProjectileSpeed) * stats.Get(Stat.ProjectileSpeed),
                Range = weapon.Def.Range * (1 + weapon.Bonus.Range),
                HitRadius = weapon.Def.Radius * stats.Get(Stat.AreaSize),
                Pierce = weapon.Def.Pierce + weapon.Bonus.Pierce, Tint = weapon.Def.Tint,
            };
            Projectile.Spawn(system, spec, system.BuildDamage(weapon));
        }
        system.NoteShots(count);
        if (system.Body is Player player) player.ShotgunFeedback(heading);
    }
}

internal sealed class SkylineSwords : IWeaponBehavior
{
    private readonly List<IDamageable> _targets = new(32);
    public void Fire(WeaponRuntime weapon, WeaponSystem system, StatSheet stats)
    {
        float reach = weapon.Def.Range * (1 + weapon.Bonus.Range) * stats.Get(Stat.AreaSize) * (1 + weapon.Bonus.Area);
        Vector3 origin = system.MuzzlePosition, heading = system.AimAt(origin, reach);
        Targeting.Overlap(origin, reach, _targets);
        int swings = Math.Clamp(weapon.BaseProjectiles + (int)stats.Get(Stat.ProjectileCount), 2, 6);
        float arc = Mathf.Cos(Mathf.DegToRad(weapon.Def.SpreadDegrees / 2));
        for (int blade = 0; blade < swings; blade++)
        {
            Vector3 forward = heading.Rotated(Vector3.Up, blade % 2 == 0 ? -.1f : .1f);
            DamageInfo hit = system.BuildDamage(weapon);
            foreach (IDamageable target in _targets)
            {
                if (!target.IsAlive || (target.Position - origin).Normalized().Dot(forward) < arc) continue;
                if (system.Body is { } body && target is Node3D { } targetBody && targetBody.IsInsideTree()
                    && !Game.WorldSight.Clear(body, origin, target.Position)) continue;
                DamageInfo damage = hit.Clone(); damage.Direction = (target.Position - origin).Normalized();
                target.TakeDamage(damage); system.ReportHit(target, damage);
            }
            Projectile.SpawnSwipe(origin + Vector3.Right * (blade % 2 == 0 ? -.2f : .2f), forward,
                reach, weapon.Def.SpreadDegrees / 2, weapon.Def.Tint);
        }
        system.NoteShots(swings);
    }
}
