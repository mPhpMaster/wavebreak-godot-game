using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Data;
using Wavebreak.Stats;

namespace Wavebreak.Combat.Weapons;

/// <summary>
/// The expansion's firing strategies, keyed by weapon id.
///
/// <see cref="AttackShape"/> stays the category the UI, audio and codex read;
/// a weapon listed here is simply delivered by its behaviour instead of the
/// shape switch in <see cref="WeaponRuntime"/>. Every number still comes from
/// the def, its level and the stat sheet, so these weapons scale with exactly
/// the same items as the original eighteen, plus <see cref="Stat.Duration"/>
/// for the ones that linger.
/// </summary>
internal static class WeaponBehaviors
{
    private static readonly Dictionary<string, IWeaponBehavior> ById = new()
    {
        ["reef_shotgun"] = new ReefShotgun(),
        ["skyline_swords"] = new SkylineSwords(),
        ["gullwing_chakram"] = new Boomerang(),
        ["circling_shoal"] = new Orbiters(),
        ["brine_wake"] = new Wake(),
        ["forked_squall"] = new Chain(),
        ["lighthouse_beam"] = new Beam(),
        ["barnacle_mines"] = new Mines(),
        ["driftwood_sentry"] = new Sentry(),
        ["maelstrom_seed"] = new Vortex(),
        ["skimstone_sling"] = new Skimstone(),
        ["sleetbreath_horn"] = new Breath(),
        ["thunderhead_censer"] = new Strikes(),
        ["breaker_swell"] = new Swell(),
    };

    /// <summary>Ids with a behaviour. The content audit checks that every
    /// expansion weapon is in here and nothing here is missing from the catalogue.</summary>
    public static IEnumerable<string> Ids => ById.Keys;

    public static bool Has(string id) => ById.ContainsKey(id);

    /// <summary>Called first thing in <see cref="WeaponRuntime"/>'s fire; false
    /// leaves the weapon to the shape switch.</summary>
    public static bool TryFire(WeaponRuntime weapon, WeaponSystem system, StatSheet stats)
    {
        if (Signatures.TryFire(weapon, system, stats)) return true;   // Combat/Weapons/Signatures.cs
        if (!ById.TryGetValue(weapon.Def.Id, out IWeaponBehavior? b)) return false;
        b.Fire(weapon, system, stats);
        return true;
    }

    // ------------------------------------------------------------- helpers

    private static readonly List<IDamageable> Buffer = new(64);
    private static readonly List<IDamageable> Hit = new(16);

    internal static int Shots(WeaponRuntime w, StatSheet s)
        => Math.Max(1, w.Def.ProjectilesAtLevel(w.Level) + (int)s.Get(Stat.ProjectileCount));

    internal static float Area(StatSheet s) => s.Get(Stat.AreaSize);

    internal static float Duration(StatSheet s) => MathF.Max(0.25f, s.Get(Stat.Duration));

    internal static float Speed(WeaponDef d, StatSheet s) => MathF.Max(0.5f, d.ProjectileSpeed * s.Get(Stat.ProjectileSpeed));

    internal static float Spread(WeaponDef d, int i, int count)
        => count <= 1 || d.SpreadDegrees <= 0f ? 0f : d.SpreadDegrees * (i / (float)(count - 1) - 0.5f);

    internal static Vector3 Ground(Vector3 muzzle) => muzzle - Vector3.Up * 0.85f;

    private static void Strike(WeaponSystem sys, IDamageable target, DamageInfo template, Vector3 from, float scale = 1f)
    {
        DamageInfo info = template.Clone();
        info.Amount *= scale;
        info.Direction = Projectile.FlatDirection(target.Position - from, Vector3.Forward);
        target.TakeDamage(info);
        sys.ReportHit(target, info);
    }

    private static ProjectileSpec Spec(WeaponSystem sys, WeaponDef d, Vector3 dir, float speed, float range, float hitRadius)
        => new()
        {
            Mode = ProjectileMode.Straight,
            Origin = sys.MuzzlePosition,
            Direction = dir,
            Speed = speed,
            Range = range,
            HitRadius = hitRadius,
            Pierce = d.Pierce,
            Tint = d.Tint,
        };

    // ============================================================ the twelve

    /// <summary>Out and back, hitting everything twice.</summary>
    private sealed class Boomerang : IWeaponBehavior
    {
        public void Fire(WeaponRuntime w, WeaponSystem sys, StatSheet s)
        {
            WeaponDef d = w.Def;
            int n = Shots(w, s);
            float speed = Speed(d, s);
            float radius = MathF.Max(0.3f, d.Radius * Area(s));
            Vector3 aim = sys.AimAt(sys.MuzzlePosition, d.Range);
            float outward = d.Range / speed;
            for (int i = 0; i < n; i++)
            {
                ProjectileSpec spec = Spec(sys, d, aim.Rotated(Vector3.Up, Mathf.DegToRad(Spread(d, i, n))), speed, d.Range, radius);
                spec.Lifetime = outward * 3.5f + 1f;
                Projectile.SpawnMotion(sys, spec, sys.BuildDamage(w), new Projectile.MotionSpec
                {
                    Motion = ProjectileMotion.Boomerang, Mesh = ExpansionVisuals.Chakram, MeshScale = radius * 0.9f,
                    P0 = outward, P1 = 1.25f,
                });
            }
            sys.NoteShots(n);
        }
    }

    /// <summary>Blades that circle the player for a while.</summary>
    private sealed class Orbiters : IWeaponBehavior
    {
        public void Fire(WeaponRuntime w, WeaponSystem sys, StatSheet s)
        {
            WeaponDef d = w.Def;
            int n = Shots(w, s);
            float area = Area(s);
            float orbit = MathF.Max(1.2f, d.Range * area);
            float life = MathF.Max(0.5f, w.Interval * 0.85f) * Duration(s);
            float angular = MathF.Max(1f, d.ProjectileSpeed * s.Get(Stat.ProjectileSpeed) / orbit);
            float radius = MathF.Max(0.3f, d.Radius * area);
            for (int i = 0; i < n; i++)
            {
                ProjectileSpec spec = Spec(sys, d, Vector3.Forward, 0f, 1f, radius);
                spec.Lifetime = life;
                Projectile.SpawnMotion(sys, spec, sys.BuildDamage(w), new Projectile.MotionSpec
                {
                    Motion = ProjectileMotion.Orbit, Mesh = ExpansionVisuals.Fin, MeshScale = radius * 1.1f,
                    P0 = i * Mathf.Tau / n, P1 = angular, P2 = orbit, Tick = 0.45f,
                });
            }
            sys.NoteShots(n);
        }
    }

    /// <summary>Drops lingering brine pools where the player stands.</summary>
    private sealed class Wake : IWeaponBehavior
    {
        public void Fire(WeaponRuntime w, WeaponSystem sys, StatSheet s)
        {
            WeaponDef d = w.Def;
            int shots = Shots(w, s);
            // At most four pools: a max build laid a dozen overlapping ones that
            // stacked into one opaque sheet around the player. Extra
            // projectiles fold into the pools' damage instead.
            int n = Math.Min(shots, 4);
            float radius = MathF.Max(0.6f, d.Radius * Area(s));
            Vector3 feet = Ground(sys.MuzzlePosition);
            for (int i = 0; i < n; i++)
            {
                // The first pool sits under the player; extras ring it.
                Vector3 at = i == 0 ? feet
                    : feet + new Vector3(MathF.Sin(i * 2.4f), 0f, MathF.Cos(i * 2.4f)) * radius * 1.3f;
                ProjectileSpec spec = Spec(sys, d, Vector3.Forward, 0f, 1f, radius);
                spec.Origin = at;
                spec.Lifetime = d.Range * Duration(s);   // Range is the pool's lifetime in seconds
                DamageInfo t = sys.BuildDamage(w);
                t.Knockback = 0f;
                if (shots > n) t.Amount *= (float)shots / n;
                Projectile.SpawnMotion(sys, spec, t, new Projectile.MotionSpec
                {
                    Motion = ProjectileMotion.Puddle, Mesh = ExpansionVisuals.Pool, MeshScale = radius, Tick = 0.4f,
                });
            }
            sys.NoteShots(n);
        }
    }

    /// <summary>Instant arc that jumps from body to body.</summary>
    private sealed class Chain : IWeaponBehavior
    {
        public void Fire(WeaponRuntime w, WeaponSystem sys, StatSheet s)
        {
            WeaponDef d = w.Def;
            Vector3 origin = sys.MuzzlePosition;
            IDamageable? current = Targeting.Nearest(origin, d.Range);
            if (current is null) { sys.NoteShots(1); return; }

            int jumps = 2 + Shots(w, s);                  // projectiles become extra links
            float hop = MathF.Max(2f, d.Radius * Area(s));
            DamageInfo template = sys.BuildDamage(w);
            Hit.Clear();
            Vector3 from = origin;
            float scale = 1f;
            for (int j = 0; j < jumps && current is not null; j++)
            {
                Hit.Add(current);
                Vector3 to = current.Position + Vector3.Up * 0.6f;
                Projectile.SpawnStreak(from, to, 0.16f, d.Tint, 0.16f);
                Strike(sys, current, template, from, scale);
                scale *= 0.88f;
                from = to;
                current = NearestUnhit(current.Position, hop);
            }
            sys.NoteShots(1);
        }

        private static IDamageable? NearestUnhit(Vector3 from, float radius)
        {
            int n = Targeting.Overlap(from, radius, Buffer);
            IDamageable? best = null;
            float bestSq = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                IDamageable t = Buffer[i];
                if (Hit.Contains(t)) continue;
                Vector3 v = t.Position - from;
                v.Y = 0f;
                if (v.LengthSquared() < bestSq) { bestSq = v.LengthSquared(); best = t; }
            }
            return best;
        }
    }

    /// <summary>A long instant line that hits everything along it.</summary>
    private sealed class Beam : IWeaponBehavior
    {
        public void Fire(WeaponRuntime w, WeaponSystem sys, StatSheet s)
        {
            WeaponDef d = w.Def;
            int n = Shots(w, s);
            float width = MathF.Max(0.3f, d.Radius * Area(s));
            Vector3 origin = sys.MuzzlePosition;
            Vector3 aim = sys.AimAt(origin, d.Range);
            DamageInfo template = sys.BuildDamage(w);
            for (int b = 0; b < n; b++)
            {
                Vector3 dir = aim.Rotated(Vector3.Up, Mathf.DegToRad(Spread(d, b, n)));
                Vector3 mid = origin + dir * (d.Range * 0.5f);
                int count = Targeting.Overlap(mid, d.Range * 0.5f + width, Buffer);
                for (int i = 0; i < count; i++)
                {
                    IDamageable t = Buffer[i];
                    Vector3 rel = t.Position - origin;
                    rel.Y = 0f;
                    float along = rel.Dot(dir);
                    if (along < -0.5f || along > d.Range) continue;
                    float side = (rel - dir * along).Length();
                    if (side > width + Targeting.AssumedTargetRadius) continue;
                    Strike(sys, t, template, origin);
                }
                Projectile.SpawnStreak(origin, origin + dir * d.Range, width * 1.4f, d.Tint, 0.2f);
            }
            sys.NoteShots(n);
        }
    }

    /// <summary>Lobs mines at where enemies are; they sit until stepped on.</summary>
    private sealed class Mines : IWeaponBehavior
    {
        public void Fire(WeaponRuntime w, WeaponSystem sys, StatSheet s)
        {
            WeaponDef d = w.Def;
            int n = Shots(w, s);
            float blast = MathF.Max(0.6f, d.Radius * Area(s));
            Vector3 origin = sys.MuzzlePosition;
            IDamageable? target = Targeting.Nearest(origin, d.Range);
            Vector3 aimAt = target?.Position ?? origin + sys.AimDirection * (d.Range * 0.5f);
            for (int i = 0; i < n; i++)
            {
                Vector3 jitter = i == 0 ? Vector3.Zero : new Vector3(MathF.Sin(i * 2.1f), 0f, MathF.Cos(i * 2.1f)) * 1.6f;
                Vector3 land = new(aimAt.X + jitter.X, Ground(origin).Y + 0.25f, aimAt.Z + jitter.Z);
                ProjectileSpec spec = Spec(sys, d, land - origin, Speed(d, s), d.Range, 0.9f);
                spec.Mode = ProjectileMode.Explosive;
                spec.BlastRadius = blast;
                spec.Lifetime = 6f * Duration(s);
                Projectile.SpawnMotion(sys, spec, sys.BuildDamage(w), new Projectile.MotionSpec
                {
                    Motion = ProjectileMotion.Mine, Mesh = ExpansionVisuals.SpikedShell, MeshScale = 0.32f,
                    Anchor = land, P0 = 0.3f, P1 = 0.32f,
                });
            }
            sys.NoteShots(n);
        }
    }

    /// <summary>Plants a sentry that shoots the nearest target for a while.</summary>
    private sealed class Sentry : IWeaponBehavior
    {
        public void Fire(WeaponRuntime w, WeaponSystem sys, StatSheet s)
        {
            WeaponDef d = w.Def;
            Vector3 origin = sys.MuzzlePosition;
            Vector3 side = sys.AimDirection.Cross(Vector3.Up).Normalized();
            if (side.LengthSquared() < 0.5f) side = Vector3.Right;
            ProjectileSpec spec = Spec(sys, d, sys.AimDirection, 0f, 1f, 0.4f);
            spec.Origin = origin + side * 1.2f + Vector3.Down * 0.2f;
            spec.Lifetime = MathF.Max(1f, w.Interval * 0.9f) * Duration(s);
            DamageInfo t = sys.BuildDamage(w);
            Projectile.SpawnMotion(sys, spec, t, new Projectile.MotionSpec
            {
                Motion = ProjectileMotion.Turret, Mesh = ExpansionVisuals.Sentry, MeshScale = 0.55f,
                P0 = d.Range, P1 = Speed(d, s),
                // Radius doubles as the sentry's own fire interval; attack speed shortens it.
                Tick = d.Radius / MathF.Max(0.1f, s.Get(Stat.AttackSpeed)),
                Counter = Shots(w, s),
            });
            sys.NoteShots(1);
        }
    }

    /// <summary>A slow seed that stops, drags everything in, and collapses.</summary>
    private sealed class Vortex : IWeaponBehavior
    {
        public void Fire(WeaponRuntime w, WeaponSystem sys, StatSheet s)
        {
            WeaponDef d = w.Def;
            int n = Shots(w, s);
            float area = Area(s);
            float pull = MathF.Max(1.5f, d.Radius * area);
            Vector3 origin = sys.MuzzlePosition;
            Vector3 aim = sys.AimAt(origin, d.Range);
            IDamageable? target = Targeting.Nearest(origin, d.Range);
            float dist = d.Range * 0.6f;
            if (target is not null)
            {
                Vector3 v = target.Position - origin;
                v.Y = 0f;
                dist = Mathf.Clamp(v.Length(), 1f, d.Range);
            }
            for (int i = 0; i < n; i++)
            {
                Vector3 dir = aim.Rotated(Vector3.Up, Mathf.DegToRad(Spread(d, i, n)));
                Vector3 hold = origin + dir * dist;
                // Low over the ground, not at chest height (FxGuard).
                hold.Y = Ground(origin).Y + 0.3f;
                ProjectileSpec spec = Spec(sys, d, dir, Speed(d, s), d.Range, 0.5f);
                spec.Origin = spec.Origin with { Y = hold.Y };
                spec.BlastRadius = pull * 0.8f;
                spec.Lifetime = dist / spec.Speed + 2.2f * Duration(s);
                Projectile.SpawnMotion(sys, spec, sys.BuildDamage(w), new Projectile.MotionSpec
                {
                    Motion = ProjectileMotion.Vortex, Mesh = ExpansionVisuals.Whorl, MeshScale = pull * 0.45f,
                    Anchor = hold, P0 = pull, P1 = 2.6f, P2 = pull * 0.45f, Tick = 0.25f,
                });
            }
            sys.NoteShots(n);
        }
    }

    /// <summary>Stones that skip from one target to the next.</summary>
    private sealed class Skimstone : IWeaponBehavior
    {
        public void Fire(WeaponRuntime w, WeaponSystem sys, StatSheet s)
        {
            WeaponDef d = w.Def;
            int n = Shots(w, s);
            float speed = Speed(d, s);
            Vector3 aim = sys.AimAt(sys.MuzzlePosition, d.Range);
            float radius = MathF.Max(0.25f, d.Radius * Area(s));
            for (int i = 0; i < n; i++)
            {
                ProjectileSpec spec = Spec(sys, d, aim.Rotated(Vector3.Up, Mathf.DegToRad(Spread(d, i, n))), speed, d.Range, radius);
                spec.Lifetime = d.Range / speed;
                Projectile.SpawnMotion(sys, spec, sys.BuildDamage(w), new Projectile.MotionSpec
                {
                    Motion = ProjectileMotion.Ricochet, Mesh = ExpansionVisuals.Pebble, MeshScale = radius * 0.9f,
                    P0 = 9f * Area(s),
                });
            }
            sys.NoteShots(n);
        }
    }

    /// <summary>A narrow, long, rapid cone of freezing spray.</summary>
    private sealed class Breath : IWeaponBehavior
    {
        public void Fire(WeaponRuntime w, WeaponSystem sys, StatSheet s)
        {
            WeaponDef d = w.Def;
            float reach = MathF.Max(1f, d.Range * Area(s));
            int extra = Shots(w, s) - d.Projectiles;
            float half = MathF.Min(80f, d.SpreadDegrees * 0.5f + MathF.Max(0, extra) * 5f);
            Vector3 origin = sys.MuzzlePosition;
            Vector3 aim = sys.AimAt(origin, reach);
            DamageInfo template = sys.BuildDamage(w);
            int count = Targeting.Cone(origin, aim, reach, half, Buffer);
            for (int i = 0; i < count; i++) Strike(sys, Buffer[i], template, origin);
            Projectile.SpawnSwipe(origin, aim, reach, half, d.Tint);
            sys.NoteShots(1);
        }
    }

    /// <summary>Calls hailstones down on random enemies nearby.</summary>
    private sealed class Strikes : IWeaponBehavior
    {
        private static readonly Core.Rng Rng = new(0x5717_CE45UL);

        public void Fire(WeaponRuntime w, WeaponSystem sys, StatSheet s)
        {
            WeaponDef d = w.Def;
            int n = Shots(w, s);
            float blast = MathF.Max(0.6f, d.Radius * Area(s));
            Vector3 origin = sys.MuzzlePosition;
            int found = Targeting.Overlap(origin, d.Range, Buffer);
            for (int i = 0; i < n; i++)
            {
                Vector3 at = found > 0
                    ? Buffer[Rng.RangeInt(0, found)].Position
                    : origin + Rng.OnUnitCircleXZ() * Rng.Range(2f, d.Range);
                Vector3 ground = new(at.X, Ground(origin).Y, at.Z);
                ProjectileSpec spec = Spec(sys, d, Vector3.Forward, 0f, 1f, 0.4f);
                spec.Mode = ProjectileMode.Explosive;
                spec.Origin = ground + Vector3.Up * 9f;
                spec.BlastRadius = blast;
                spec.Lifetime = 0.55f + 0.08f * i;      // staggered so a volley rattles down
                Projectile.SpawnMotion(sys, spec, sys.BuildDamage(w), new Projectile.MotionSpec
                {
                    Motion = ProjectileMotion.Strike, Mesh = ExpansionVisuals.Stone, MeshScale = 0.35f + blast * 0.08f,
                    Anchor = ground, P0 = 9f,
                });
            }
            sys.NoteShots(n);
        }
    }

    /// <summary>An expanding ring of water along the ground.</summary>
    private sealed class Swell : IWeaponBehavior
    {
        public void Fire(WeaponRuntime w, WeaponSystem sys, StatSheet s)
        {
            WeaponDef d = w.Def;
            int shots = Shots(w, s);
            // At most four rings: past that they tiled the screen into terraces.
            // Extra projectiles fold into the rings' damage instead.
            int n = Math.Min(shots, 4);
            float reach = MathF.Max(2f, d.Range * MathF.Min(Area(s), 2.5f));
            Vector3 centre = Ground(sys.MuzzlePosition);
            DamageInfo hit = sys.BuildDamage(w);
            if (shots > n) hit.Amount *= (float)shots / n;
            for (int i = 0; i < n; i++)
            {
                // Extra rings follow at 80% of the speed of the one before.
                float speed = Speed(d, s) * MathF.Pow(0.8f, i);
                ProjectileSpec spec = Spec(sys, d, Vector3.Forward, 0f, 1f, 0.5f);
                spec.Origin = centre;
                spec.Lifetime = reach / speed;
                Projectile.SpawnMotion(sys, spec, hit.Clone(), new Projectile.MotionSpec
                {
                    Motion = ProjectileMotion.Ring, Mesh = ExpansionVisuals.Crest, MeshScale = 1f,
                    Anchor = centre, P0 = speed, P1 = 1f + d.Radius,
                });
            }
            sys.NoteShots(n);
        }
    }
}
