using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Data;
using Wavebreak.Stats;

namespace Wavebreak.Combat.Weapons;

/// <summary>
/// What an evolved weapon does on top of its delivery
/// (<see cref="EvolutionDef.Rider"/>). Two entry points: <see cref="OnVolley"/>
/// after a volley and <see cref="OnHit"/> after a hit landed.
///
/// A rider's own hits carry no <see cref="DamageInfo.Evolution"/>, so a rider can
/// never trigger itself. They use the ordinary pool and the ordinary hit path:
/// a client's riders reach the host's enemies from inside TakeDamage like any
/// other shot (see "Who owns what"), and what a teammate sees of them is the
/// flash sent through <see cref="Mirror"/>.
/// </summary>
internal static class EvolutionRiders
{
    private static readonly List<IDamageable> Buffer = new(64);
    private static readonly Dictionary<EvolutionDef, int> Counts = new();
    private static readonly Core.Rng FxRng = new(0xE70_1FE5UL);

    /// <summary>Rider effects this run (the check and the co-op test read it).</summary>
    public static int Fired { get; private set; }

    public static void Reset() => Fired = 0;

    /// <summary>A rider hit: scaled, weaker on procs, and unable to chain.</summary>
    private static DamageInfo Derived(DamageInfo from, float scale)
    {
        DamageInfo d = from.Clone();
        d.Amount *= scale;
        d.Knockback *= 0.4f;
        d.ProcCoefficient *= 0.35f;
        d.Evolution = null;
        return d;
    }

    private static bool Turn(EvolutionDef e)
    {
        if (e.RiderEvery <= 1) return true;
        int n = Counts.GetValueOrDefault(e) + 1;
        Counts[e] = n >= e.RiderEvery ? 0 : n;
        return n >= e.RiderEvery;
    }

    private static void Mirror(Vector3 at, float radius, Color tint)
    {
        Fired++;
        if (Net.Coop.InRun) Net.NetSession.Instance?.SendEvolutionFx(at, radius, tint);
    }

    // -------------------------------------------------------------- volleys

    public static void OnVolley(WeaponRuntime w, WeaponSystem sys, StatSheet s)
    {
        if (w.Evolution is not { } e || w.Echoing) return;
        if (e.Rider is not (EvolutionRider.Wave or EvolutionRider.Meteor or EvolutionRider.Nova or EvolutionRider.Quake)) return;
        if (!Turn(e)) return;

        DamageInfo template = sys.BuildDamage(w);
        template.Evolution = null;
        template.Amount *= e.RiderPower;
        template.ProcCoefficient *= 0.35f;
        float area = WeaponBehaviors.Area(s);
        switch (e.Rider)
        {
            case EvolutionRider.Wave: Wave(e, sys, template, area); break;
            case EvolutionRider.Meteor: Meteor(e, w, sys, template, area); break;
            case EvolutionRider.Nova: Nova(e, sys, template); break;
            case EvolutionRider.Quake: Quake(e, sys, template, area); break;
        }
        Mirror(sys.MuzzlePosition, e.RiderRadius * 0.4f, e.Tint);
    }

    private static void Wave(EvolutionDef e, WeaponSystem sys, DamageInfo template, float area)
    {
        float reach = MathF.Max(2f, e.RiderRadius * MathF.Min(area, 2.5f));
        const float speed = 14f;
        Vector3 centre = WeaponBehaviors.Ground(sys.MuzzlePosition);
        Look? look = WeaponLooks.OfEvolution(e);
        var spec = new ProjectileSpec
        {
            Mode = ProjectileMode.Straight, Origin = centre, Direction = sys.AimAt(sys.MuzzlePosition, reach),
            Range = 1f, HitRadius = 0.5f, Tint = e.Tint, Lifetime = reach / speed,
            Style = look?.Style ?? ShotStyle.None,
        };
        if (spec.Style == ShotStyle.Front) template.Amount *= 1.6f;   // a third of the ring, so each cut is harder
        Projectile.SpawnMotion(sys, spec, template, new Projectile.MotionSpec
        {
            Motion = ProjectileMotion.Ring, Mesh = look?.Mesh ?? ExpansionVisuals.Crest, MeshScale = 1f,
            Anchor = centre, P0 = speed, P1 = 1.6f,
        });
    }

    private static void Meteor(EvolutionDef e, WeaponRuntime w, WeaponSystem sys, DamageInfo template, float area)
    {
        Vector3 origin = sys.MuzzlePosition;
        float reach = MathF.Max(8f, w.Def.Range);
        int found = Targeting.Overlap(origin, reach, Buffer);
        float blast = MathF.Max(0.6f, e.RiderRadius * area);
        for (int i = 0; i < e.RiderCount; i++)
        {
            Vector3 at = found > 0 ? Buffer[FxRng.RangeInt(0, found)].Position
                : origin + FxRng.OnUnitCircleXZ() * FxRng.Range(2f, reach);
            Drop(e, sys, template.Clone(), new Vector3(at.X, WeaponBehaviors.Ground(origin).Y, at.Z), blast, 9f, 0.5f + 0.1f * i);
        }
    }

    private static void Quake(EvolutionDef e, WeaponSystem sys, DamageInfo template, float area)
    {
        Vector3 origin = sys.MuzzlePosition;
        float reach = MathF.Max(3f, e.RiderRadius * MathF.Min(area, 2f));
        Vector3 aim = sys.AimAt(origin, reach);
        aim.Y = 0f;
        aim = aim.LengthSquared() > 1e-4f ? aim.Normalized() : Vector3.Forward;
        Vector3 ground = WeaponBehaviors.Ground(origin);
        float blast = MathF.Max(1.2f, 2f * area);
        for (int i = 0; i < e.RiderCount; i++)
            Drop(e, sys, template.Clone(), ground + aim * (reach * (i + 1) / e.RiderCount), blast, 0.7f, 0.12f + 0.09f * i);
    }

    /// <summary>Same falling blast the Thunderhead Censer drops.</summary>
    private static void Drop(EvolutionDef e, WeaponSystem sys, DamageInfo template, Vector3 ground, float blast, float height, float life)
    {
        // Each evolution drops its own thing: a burning chunk at a slant (Sunken
        // Sun), a bolt straight down (Hundred Forks), a spike out of the ground
        // (Worldforge Maul). WeaponLooks.cs, Projectile.Style.cs.
        Look? look = WeaponLooks.OfEvolution(e);
        var spec = new ProjectileSpec
        {
            Mode = ProjectileMode.Explosive, Origin = ground + Vector3.Up * height, Direction = Vector3.Forward,
            Range = 1f, HitRadius = 0.4f, BlastRadius = blast, Tint = e.Tint, Lifetime = life,
            Style = look?.Style ?? ShotStyle.None, Impact = look?.Impact ?? ImpactKind.Blast,
        };
        Projectile.SpawnMotion(sys, spec, template, new Projectile.MotionSpec
        {
            Motion = ProjectileMotion.Strike, Mesh = look?.Mesh ?? ExpansionVisuals.Stone,
            MeshScale = look is { } l ? l.Scale + blast * 0.05f : 0.35f + blast * 0.08f,
            Anchor = ground, P0 = height,
        });
    }

    private static void Nova(EvolutionDef e, WeaponSystem sys, DamageInfo template)
    {
        if (e.Id == "evo_stormcage")
        {
            template.Amount *= 0.85f;
            // Stormcage spits lightning, not bolts: instant forks to the nearest
            // bodies in reach, drawn as jagged arcs from you.
            Vector3 origin = sys.MuzzlePosition;
            int n = Targeting.Overlap(origin, e.RiderRadius, Buffer);
            Buffer.Sort((a, b) => (a.Position - origin).LengthSquared().CompareTo((b.Position - origin).LengthSquared()));
            // A thin crowd (under four): each body takes up to two forks.
            for (int i = 0; n > 0 && i < Math.Min(n < 4 ? n * 2 : n, e.RiderCount); i++)
            {
                IDamageable t = Buffer[i % n];
                DistinctFx.Jagged(origin, t.Position + Vector3.Up * 0.5f, e.Tint, 0.07f, 0.16f, 4);
                Strike(sys, t, template, origin);
            }
            if (n > 0) DistinctStats.Note("stormcage_fork");   // (template is 85%: forks never miss)
            return;
        }
        if (e.Id == "evo_aurora_choir")
        {
            // Aurora Choir: slow motes of cold light that spiral outward.
            Look look = WeaponLooks.OfEvolution(e)!.Value;
            float turn = FxRng.Range(0f, Mathf.Tau);
            for (int i = 0; i < e.RiderCount; i++)
            {
                float a = turn + i * Mathf.Tau / e.RiderCount;
                var spec = new ProjectileSpec
                {
                    Mode = ProjectileMode.Straight, Origin = sys.MuzzlePosition, Direction = new Vector3(MathF.Sin(a), 0f, MathF.Cos(a)),
                    Speed = 11f, Range = e.RiderRadius, HitRadius = 0.4f, Tint = e.Tint,
                };
                WeaponLooks.Dress(ref spec, look, i);
                Projectile.Spawn(sys, spec, template.Clone());
            }
            DistinctStats.Note("aurora_spiral");
            return;
        }
        Ring(sys, sys.MuzzlePosition, template, e.RiderCount, e.RiderRadius, e.Tint);
    }

    private static void Ring(WeaponSystem sys, Vector3 origin, DamageInfo template, int count, float reach, Color tint)
    {
        float turn = FxRng.Range(0f, Mathf.Tau);
        for (int i = 0; i < count; i++)
        {
            float a = turn + i * Mathf.Tau / count;
            var spec = new ProjectileSpec
            {
                Mode = ProjectileMode.Straight, Origin = origin, Direction = new Vector3(MathF.Sin(a), 0f, MathF.Cos(a)),
                Speed = 22f, Range = reach, HitRadius = 0.3f, Tint = tint,
            };
            Projectile.Spawn(sys, spec, template.Clone());
        }
    }

    // ----------------------------------------------------------------- hits

    /// <summary>Called for every landed hit that still carries its evolution.</summary>
    public static void OnHit(WeaponSystem sys, IDamageable target, DamageInfo info)
    {
        if (info.Evolution is not { } e) return;
        switch (e.Rider)
        {
            case EvolutionRider.Arc:
                if (Turn(e)) Arc(e, sys, target, info);
                break;
            case EvolutionRider.Blast:
                if (Turn(e)) Blast(e, sys, target, info);
                break;
            case EvolutionRider.Pool:
                if (Turn(e)) Pool(e, sys, target, info);
                break;
            case EvolutionRider.Shards:
                if (target.IsAlive) break;
                Shards(e, sys, target, info);
                Mirror(target.Position, 1.2f, e.Tint);
                break;
        }
    }

    /// <summary>A kill flings pieces: whalebone in a fan along the blow
    /// (Stormwing), glass that turns on the nearest body (Knife Reef), shell bits
    /// lobbed round the corpse that burst where they land (Reef of Teeth).</summary>
    private static void Shards(EvolutionDef e, WeaponSystem sys, IDamageable target, DamageInfo info)
    {
        DamageInfo shard = Derived(info, e.RiderPower);
        Look? look = WeaponLooks.OfEvolution(e);
        Vector3 at = target.Position;
        switch (e.Id)
        {
            case "evo_stormwing":
            {
                Vector3 along = Projectile.FlatDirection(info.Direction, Vector3.Forward);
                for (int i = 0; i < e.RiderCount; i++)
                {
                    float off = (i / (float)Math.Max(1, e.RiderCount - 1) - 0.5f) * 80f;
                    var spec = new ProjectileSpec
                    {
                        Mode = ProjectileMode.Straight, Origin = at, Direction = along.Rotated(Vector3.Up, Mathf.DegToRad(off)),
                        Speed = 22f, Range = e.RiderRadius, HitRadius = 0.3f, Tint = e.Tint,
                    };
                    if (look is { } l) WeaponLooks.Dress(ref spec, l, i);
                    Projectile.Spawn(sys, spec, shard.Clone());
                }
                DistinctStats.Note("stormwing_fan");
                return;
            }
            case "evo_knife_reef":
            {
                float turn = FxRng.Range(0f, Mathf.Tau);
                for (int i = 0; i < e.RiderCount; i++)
                {
                    float a = turn + i * Mathf.Tau / e.RiderCount;
                    var spec = new ProjectileSpec
                    {
                        Mode = ProjectileMode.Homing, Origin = at, Direction = new Vector3(MathF.Sin(a), 0f, MathF.Cos(a)),
                        Speed = 15f, Range = e.RiderRadius, HitRadius = 0.3f, Tint = e.Tint,
                    };
                    if (look is { } l) WeaponLooks.Dress(ref spec, l, i);
                    Projectile.Spawn(sys, spec, shard.Clone());
                }
                DistinctStats.Note("knife_home");
                return;
            }
            case "evo_reef_of_teeth":
            {
                Vector3 ground = new(at.X, WeaponBehaviors.Ground(sys.MuzzlePosition).Y + 0.3f, at.Z);
                shard.Amount *= 0.75f;   // each bit bursts on an area
                float turn = FxRng.Range(0f, Mathf.Tau);
                for (int i = 0; i < e.RiderCount; i++)
                {
                    float a = turn + i * Mathf.Tau / e.RiderCount;
                    float reach = 1.6f + 1.4f * (i % 2);
                    var spec = new ProjectileSpec
                    {
                        Mode = ProjectileMode.Explosive, Origin = at, Direction = new Vector3(MathF.Sin(a), 0f, MathF.Cos(a)),
                        Speed = 7f, Range = 3f, HitRadius = 0.3f, BlastRadius = 1.1f, Tint = e.Tint,
                        Target = ground + new Vector3(MathF.Sin(a), 0f, MathF.Cos(a)) * reach,
                    };
                    if (look is { } l) WeaponLooks.Dress(ref spec, l, i);
                    Projectile.Spawn(sys, spec, shard.Clone());
                }
                DistinctStats.Note("teeth_lob");
                return;
            }
        }
        Ring(sys, at, shard, e.RiderCount, e.RiderRadius, e.Tint);
    }

    private static void Strike(WeaponSystem sys, IDamageable t, DamageInfo template, Vector3 from)
    {
        DamageInfo hit = template.Clone();
        hit.Direction = Projectile.FlatDirection(t.Position - from, Vector3.Forward);
        t.TakeDamage(hit);
        sys.ReportHit(t, hit);
    }

    private static void Arc(EvolutionDef e, WeaponSystem sys, IDamageable from, DamageInfo info)
    {
        DamageInfo template = Derived(info, e.RiderPower);
        Vector3 at = from.Position;
        int n = Targeting.Overlap(at, e.RiderRadius, Buffer);
        int jumps = 0;
        for (int i = 0; i < n && jumps < e.RiderCount; i++)
        {
            IDamageable t = Buffer[i];
            if (ReferenceEquals(t, from) || !t.IsAlive) continue;
            // Living Stormfan forks as jagged lightning; Final Verdict's sentence
            // comes down on each neighbour as a pillar from above.
            if (e.Id == "evo_final_verdict") { DistinctFx.Pillar(t.Position, e.Tint); DistinctStats.Note("verdict_pillar"); }
            else { DistinctFx.Jagged(at + Vector3.Up * 0.6f, t.Position + Vector3.Up * 0.6f, e.Tint, 0.08f, 0.18f, 3); DistinctStats.Note("stormfan_fork"); }
            Strike(sys, t, template, at);
            jumps++;
        }
        if (jumps > 0) Mirror(at, 1.4f, e.Tint);
    }

    private static void Blast(EvolutionDef e, WeaponSystem sys, IDamageable from, DamageInfo info)
    {
        DamageInfo template = Derived(info, e.RiderPower);
        Vector3 at = from.Position;
        if (e.Id == "evo_breaching_gun")
        {
            // A shaped charge: the blast goes on through the body, in a cone
            // behind it along the shot, longer than a round burst.
            Vector3 along = Projectile.FlatDirection(info.Direction, Vector3.Forward);
            float reach = e.RiderRadius * 1.5f;
            int m = Targeting.Overlap(at, reach, Buffer);
            for (int i = 0; i < m; i++)
            {
                IDamageable t = Buffer[i];
                if (ReferenceEquals(t, from)) continue;
                Vector3 to = Projectile.FlatDirection(t.Position - at, along);
                if (to.Dot(along) < 0.5f) continue;
                Strike(sys, t, template, at);
            }
            DistinctFx.Charge(at, along, reach, e.Tint);
            DistinctStats.Note("breach_cone");
            Mirror(at, e.RiderRadius, e.Tint);
            return;
        }
        int n = Targeting.Overlap(at, e.RiderRadius, Buffer);
        for (int i = 0; i < n; i++)
            if (!ReferenceEquals(Buffer[i], from)) Strike(sys, Buffer[i], template, at);
        // Sunlance: a ring of flame, not a dome.
        if (e.Id == "evo_sunlance") { DistinctFx.Impact(ImpactKind.Fire, WeaponBehaviors.Ground(at + Vector3.Up * 0.85f), e.RiderRadius, e.Tint); DistinctStats.Note("sunlance_flare"); }
        else ExplosionFx.Play(at, e.RiderRadius, e.Tint);
        Mirror(at, e.RiderRadius, e.Tint);
    }

    private static void Pool(EvolutionDef e, WeaponSystem sys, IDamageable from, DamageInfo info)
    {
        DamageInfo template = Derived(info, e.RiderPower);
        template.Knockback = 0f;
        Vector3 ground = new(from.Position.X, WeaponBehaviors.Ground(sys.MuzzlePosition).Y, from.Position.Z);
        Look? look = WeaponLooks.OfEvolution(e);
        var spec = new ProjectileSpec
        {
            Mode = ProjectileMode.Straight, Origin = ground, Direction = Vector3.Forward,
            Range = 1f, HitRadius = e.RiderRadius, Tint = e.Tint, Lifetime = 3.2f,
            Style = look?.Style ?? ShotStyle.None,
        };
        Projectile.SpawnMotion(sys, spec, template, new Projectile.MotionSpec
        {
            Motion = ProjectileMotion.Puddle, Mesh = look?.Mesh ?? ExpansionVisuals.Pool, MeshScale = e.RiderRadius, Tick = 0.45f,
        });
        Mirror(ground, e.RiderRadius, e.Tint);
    }
}
