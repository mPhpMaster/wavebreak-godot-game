using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Combat.Weapons;

namespace Wavebreak.Combat;

/// <summary>
/// The look-alike fix's half of <see cref="Projectile"/>: a shot's own mesh and
/// its flight twist (<see cref="ShotStyle"/>, rows in <see cref="WeaponLooks"/>).
/// Same pool, same one-loop update, same hit reporting as every other shot;
/// a shot launched without a style is exactly the old shot.
///
/// The twists are small on purpose and each is visible: a lob that only lands
/// where it was aimed, a seeker that stops steering, a needle stream, a pod that
/// splits. Cosmetic wobble moves the mesh, never the hit point.
/// </summary>
public sealed partial class Projectile
{
    private static readonly List<IDamageable> StyleBuffer = new(64);

    private ShotStyle _style;
    private ImpactKind _impact;
    private int _styleIndex;
    private Vector3 _from;       // where it left (lobs, distance travelled)
    private Vector3 _target;     // where a lob lands
    private float _flight = 1f;  // a lob's time in the air
    private float _styleRange = 1f;
    private bool _styleFlag;     // lance: has hit; wisp: committed

    private void BeginStyle(in ProjectileSpec spec)
    {
        _style = spec.Style;
        _impact = spec.Impact;
        _styleIndex = spec.StyleIndex;
        _from = spec.Origin;
        _target = spec.Target;
        _styleFlag = false;
        _styleRange = MathF.Max(1f, _velocity.Length() * _life);
        if (_style == ShotStyle.None) return;

        if (spec.Mesh is not null)
        {
            _mesh.Mesh = spec.Mesh;
            _mesh.RotationDegrees = Vector3.Zero;
            _mesh.Position = Vector3.Zero;
            _mesh.Scale = Vector3.One * MathF.Max(0.05f, spec.MeshScale);
            _mesh.MaterialOverride = ExpansionVisuals.Glow(_tint);
        }

        switch (_style)
        {
            case ShotStyle.Lob:
            case ShotStyle.Seedling:
            case ShotStyle.Hail:
            case ShotStyle.ShellBit:
                Vector3 d = _target - _from;
                d.Y = 0f;
                float speed = MathF.Max(1f, _velocity.Length());
                _flight = _style switch
                {
                    ShotStyle.Seedling => 0.38f,
                    ShotStyle.ShellBit => 0.45f,
                    ShotStyle.Lob => Mathf.Clamp(d.Length() / speed * 1.3f, 0.55f, 1.6f),
                    _ => Mathf.Clamp(d.Length() / speed, 0.35f, 1.1f),
                };
                _life = _flight + 0.05f;
                if (_style == ShotStyle.Lob) DistinctFx.Telegraph(_target, _blastRadius, _tint, _flight);
                break;
            case ShotStyle.Loop:
                // Glass Gale: leave at right angles so the loop's far side runs
                // through the aim point (the loop turns the other way).
                _velocity = _velocity.Rotated(Vector3.Up, (_styleIndex % 2 == 0 ? -1f : 1f) * Mathf.Pi * 0.5f);
                break;
            case ShotStyle.Tracer:
                if (_trail.A <= 0f && WeaponLooks.Extras) _trail = new Color(1f, 0.62f, 0.32f, 0.55f);
                break;
        }
    }

    private void EndStyle() => _style = ShotStyle.None;

    private bool IsLobbed => _style is ShotStyle.Lob or ShotStyle.Seedling or ShotStyle.Hail or ShotStyle.ShellBit;

    /// <summary>A lob flies its own arc and ignores bodies on the way; it
    /// blows up where it was aimed.</summary>
    private void StepLob()
    {
        float t = Mathf.Clamp(_age / MathF.Max(0.05f, _flight), 0f, 1f);
        Vector3 flat = _from.Lerp(_target, t);
        float h;
        if (_style == ShotStyle.Hail)
        {
            // Thrown low: it skips once at about 60% of the way, then lands.
            const float split = 0.6f;
            h = t < split ? 1.3f * 4f * (t / split) * (1f - t / split)
                          : 0.5f * 4f * ((t - split) / (1f - split)) * (1f - (t - split) / (1f - split));
            if (!_styleFlag && t >= split) { _styleFlag = true; DistinctStats.Note("hail_skip"); DistinctFx.Impact(ImpactKind.Frost, flat, 0.5f, _tint); }
        }
        else
        {
            Vector3 span = _target - _from;
            span.Y = 0f;
            float apex = _style switch
            {
                ShotStyle.Lob => 2.6f + 0.18f * span.Length(),
                ShotStyle.Seedling => 0.9f,
                _ => 1.4f,
            };
            h = apex * 4f * t * (1f - t);
        }
        GlobalPosition = flat + Vector3.Up * h;
        _mesh.Rotation = new Vector3(_age * 5f, _age * 3f, 0f);
        if (t >= 1f)
        {
            if (_style == ShotStyle.Lob) DistinctStats.Note("lob_landed");
            Detonate(_target);
        }
    }

    /// <summary>Cosmetic wobble after a straight or homing step: the mesh moves,
    /// the hit point does not.</summary>
    private void StyleAfterMove(float dt)
    {
        float a = _age, i = _styleIndex;
        switch (_style)
        {
            case ShotStyle.Spark:
                _mesh.Position = new Vector3(MathF.Sin(a * 38f + i * 1.7f) * 0.28f, MathF.Sin(a * 27f + i) * 0.08f, 0f);
                _mesh.Rotation = new Vector3(0f, 0f, a * 14f);
                break;
            case ShotStyle.Barb:
                _mesh.Rotation = new Vector3(0f, 0f, a * 10f);
                break;
            case ShotStyle.Shard:
                _mesh.Rotation = new Vector3(0f, 0f, a * 4f);
                break;
            case ShotStyle.Wisp:
                _mesh.Position = new Vector3(0f, MathF.Sin(a * 7f + i * 2f) * 0.2f, 0f);
                _mesh.Scale = Vector3.One * (_mesh.Scale.X * (1f + 0.02f * MathF.Sin(a * 11f)));
                break;
            case ShotStyle.Wasp:
            case ShotStyle.HiveWasp:
                _mesh.Position = new Vector3(MathF.Sin(a * 31f + i * 2f) * 0.22f, MathF.Sin(a * 23f + i) * 0.12f, 0f);
                break;
            case ShotStyle.Spindle:
                _mesh.Position = new Vector3(MathF.Sin(a * 16f) * 0.3f, MathF.Cos(a * 16f) * 0.3f, 0f);
                _mesh.Rotation = new Vector3(0f, 0f, a * 25f);
                break;
            case ShotStyle.Mote:
                // Spirals outward: the heading keeps turning one way.
                _velocity = _velocity.Rotated(Vector3.Up, (i % 2 == 0 ? 1.8f : -1.8f) * dt);
                _mesh.Position = new Vector3(0f, MathF.Sin(a * 9f + i) * 0.15f, 0f);
                break;
        }
    }

    /// <summary>Turn-rate scale for a styled seeker.</summary>
    private float StyleTurn() => _style switch
    {
        ShotStyle.Wisp => _age < 0.3f ? WispDrift() : 0.5f,
        ShotStyle.Wasp or ShotStyle.HiveWasp => 1.25f,
        ShotStyle.Spindle => 1.4f,
        ShotStyle.Homeshard => 1.7f,
        // A lance that has struck barely steers: it holds a wide line.
        ShotStyle.Lance => _styleFlag ? 0.3f : 1f,
        _ => 1f,
    };

    private static float WispDrift()
    {
        DistinctStats.Note("wisp_drift");
        return 0.12f;
    }

    /// <summary>True when a styled seeker must not steer this frame.</summary>
    private static bool StyleHoldsCourse => false;

    /// <summary>A styled seeker's own target choice, or null for the nearest.</summary>
    private IDamageable? StyleLock()
    {
        switch (_style)
        {
            case ShotStyle.Wasp:
            case ShotStyle.HiveWasp:
            {
                // Each wasp of a volley takes the next-nearest body, so the swarm spreads.
                int n = Targeting.Overlap(GlobalPosition, HomingSeekRange, StyleBuffer);
                if (n == 0) return null;
                StyleBuffer.Sort((x, y) => DistSq(x).CompareTo(DistSq(y)));
                int pick = _styleIndex % n;
                if (pick > 0) DistinctStats.Note("wasp_spread");
                return StyleBuffer[pick];
            }
            case ShotStyle.Spindle:
            case ShotStyle.Homeshard:
                return NearestUnpierced(HomingSeekRange);
        }
        return null;
    }

    private float DistSq(IDamageable t)
    {
        Vector3 v = t.Position - GlobalPosition;
        v.Y = 0f;
        return v.LengthSquared();
    }

    private IDamageable? NearestUnpierced(float range)
    {
        int n = Targeting.Overlap(GlobalPosition, range, StyleBuffer);
        IDamageable? best = null;
        float bestSq = float.MaxValue;
        for (int k = 0; k < n; k++)
        {
            IDamageable t = StyleBuffer[k];
            if (_pierced.Contains(t) || !t.IsAlive) continue;
            float sq = DistSq(t);
            if (sq < bestSq) { bestSq = sq; best = t; }
        }
        return best;
    }

    /// <summary>Shape a hit before it lands (called from ApplyHit).</summary>
    private void StyleShapeHit(DamageInfo info, IDamageable target)
    {
        switch (_style)
        {
            case ShotStyle.Spark:
            {
                // Unfair up close, poor at the edge of its reach.
                Vector3 run = GlobalPosition - _from;
                run.Y = 0f;
                float t = Mathf.Clamp(run.Length() / _styleRange, 0f, 1f);
                info.Amount *= Mathf.Lerp(1.15f, 0.55f, t);
                if (t < 0.35f) DistinctStats.Note("spark_close");
                break;
            }
            case ShotStyle.Barb:
            {
                // Harder for every body already on the spike; shoves along the line.
                int before = Math.Max(0, _pierced.Count - 1);
                info.Amount *= 1f + 0.12f * before;
                Vector3 along = FlatDirection(_velocity, Vector3.Forward);
                info.Direction = along;
                if (before > 0) DistinctStats.Note("barb_ramp");
                break;
            }
            case ShotStyle.Spindle:
                // A thread that snaps on to the next body frays: 75% after the first.
                if (_pierced.Count > 1) info.Amount *= 0.75f;
                break;
            case ShotStyle.HiveWasp:
                info.Amount *= 0.85f;
                break;
            case ShotStyle.Harpoon:
            {
                // Reeled in: pushed toward the thrower, with a rope drawn back.
                Vector3 home = FlatDirection(Muzzle - target.Position, Vector3.Zero);
                if (home.LengthSquared() > 1e-4f)
                {
                    info.Direction = home;
                    info.Knockback = MathF.Max(info.Knockback, 3f);
                }
                if (WeaponLooks.Extras) SignatureFx.Add(new SignatureFx.Wire(target.Position + Vector3.Up * 0.5f, Muzzle, 0.05f, _tint, 0.25f));
                DistinctStats.Note("harpoon_pull");
                break;
            }
        }
    }

    /// <summary>After a contact hit that landed (Contact).</summary>
    private void OnStyleHit(IDamageable target, DamageInfo info, Vector3 at)
    {
        switch (_style)
        {
            case ShotStyle.Lance:
                if (!_styleFlag)
                {
                    _styleFlag = true;   // from here on it holds its line, and the
                    _hitRadius *= 3.6f;  // line it holds is wide: everything behind pays
                    DistinctStats.Note("lance_straight");
                    DistinctFx.Pillar(target.Position, _tint);
                }
                break;
            case ShotStyle.Spindle:
                if (_pierceLeft > 0 && NearestUnpierced(9f) is { } next)
                {
                    _lock = next;
                    DistinctStats.Note("spindle_snap");
                    DistinctFx.Jagged(at, next.Position + Vector3.Up * 0.5f, _tint, 0.05f, 0.16f);
                }
                break;
            case ShotStyle.Needle:
                if (info.Crit && WeaponLooks.Extras) SignatureFx.Add(new SignatureFx.Flash(at, 0.45f, _tint, 0.2f));
                break;
        }
        DistinctFx.HitSpark(_impact, at, _tint);
    }

    /// <summary>A contact shot that just spent its last pierce on a real body.</summary>
    private void StyleBreak(Vector3 at)
    {
        if (_style != ShotStyle.Shard || _system is null || _template is null) return;
        // Splinter Rail: two small splinters fly on at an angle.
        DamageInfo shard = _template.Clone();
        shard.Amount *= 0.15f;
        shard.ProcCoefficient *= 0.5f;
        shard.Evolution = null;
        Vector3 dir = FlatDirection(_velocity, Vector3.Forward);
        for (int s = -1; s <= 1; s += 2)
        {
            var spec = new ProjectileSpec
            {
                Mode = ProjectileMode.Straight, Origin = at, Direction = dir.Rotated(Vector3.Up, s * 0.5f),
                Speed = MathF.Max(8f, _velocity.Length() * 0.8f), Range = 5f, HitRadius = 0.25f, Tint = _tint,
                Mesh = LookMeshes.Shard, MeshScale = 0.14f, Style = ShotStyle.Plain, Impact = ImpactKind.Glass,
            };
            Spawn(_system, spec, shard.Clone());
        }
        DistinctStats.Note("shard_split");
        DistinctFx.Impact(ImpactKind.Glass, at, 0.6f, _tint);
    }

    /// <summary>A styled burst. True when the style drew its own flash.</summary>
    private bool StyleDetonate(Vector3 at, float blast)
    {
        switch (_style)
        {
            case ShotStyle.Pod:
                if (_system is not null && _template is not null)
                {
                    // Splits on contact: two seedlings hop sideways and pop.
                    DamageInfo seed = _template.Clone();
                    seed.Amount *= 0.45f;
                    seed.ProcCoefficient *= 0.5f;
                    seed.Evolution = null;
                    Vector3 side = FlatDirection(_velocity, Vector3.Forward).Cross(Vector3.Up);
                    for (int s = -1; s <= 1; s += 2)
                    {
                        Vector3 land = at + side * (s * 2.2f);
                        var spec = new ProjectileSpec
                        {
                            Mode = ProjectileMode.Explosive, Origin = at, Direction = side * s, Speed = 6f, Range = 3f,
                            HitRadius = 0.3f, BlastRadius = MathF.Max(0.6f, blast * 0.6f), Tint = _tint,
                            Mesh = LookMeshes.Seedling, MeshScale = 0.2f, Style = ShotStyle.Seedling, Impact = ImpactKind.Spore,
                            Target = land,
                        };
                        Spawn(_system, spec, seed.Clone());
                    }
                    DistinctStats.Note("pod_split");
                }
                DistinctFx.Impact(ImpactKind.Spore, at, blast, _tint);
                return true;
            case ShotStyle.Seedling:
                DistinctStats.Note("seedling_pop");
                DistinctFx.Impact(ImpactKind.Spore, at, blast, _tint);
                return true;
            case ShotStyle.Hail:
            case ShotStyle.Drift:
                DistinctFx.Impact(ImpactKind.Frost, at, blast, _tint);
                return true;
            case ShotStyle.Bolt:
                DistinctFx.Jagged(at + Vector3.Up * 9f, at, _tint, 0.1f, 0.22f, 5);
                DistinctStats.Note("bolt_strike");
                return false;
            case ShotStyle.Lob:
            case ShotStyle.Cannon:
            case ShotStyle.ShellBit:
                DistinctFx.Impact(ImpactKind.Debris, at, blast, _tint);
                return false;
            case ShotStyle.Meteor:
            case ShotStyle.Erupt:
                DistinctFx.Impact(ImpactKind.Fire, at, blast, _tint);
                return false;
        }
        return false;
    }

    // ------------------------------------------------- borrowed deliveries

    /// <summary>Strike motion variants: where a falling shot is at time <paramref name="t"/>
    /// (0..1). Null keeps the plain vertical drop.</summary>
    private Vector3? StyleStrike(float t)
    {
        switch (_style)
        {
            case ShotStyle.Cannon:
            {
                // In at a slant from the left of the aim, like a ship's broadside.
                Vector3 from = _anchor + new Vector3(-7f, 6f, 2f);
                Vector3 p = from.Lerp(_anchor, t);
                return p + Vector3.Up * (2f * t * (1f - t));
            }
            case ShotStyle.Meteor:
                if (t <= 0.05f) DistinctStats.Note("meteor_slant");
                return _anchor.Lerp(_anchor + new Vector3(3.5f, 9f, -3.5f), 1f - t * t);
            case ShotStyle.Bolt:
                return _anchor + Vector3.Up * 0.05f;   // only the mark; the bolt comes at the end
            case ShotStyle.Erupt:
                // Rises out of the ground, fast then slow.
                if (t <= 0.05f) DistinctStats.Note("erupt_spike");
                return _anchor + Vector3.Up * (-1.7f + 1.7f * MathF.Sqrt(t));
        }
        return null;
    }
}
