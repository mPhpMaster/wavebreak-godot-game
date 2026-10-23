using System;
using System.Collections.Generic;
using Godot;

namespace Wavebreak.Combat;

/// <summary>Custom flight programs for the expansion weapons. <see cref="None"/>
/// leaves a projectile to the original <see cref="ProjectileMode"/> code.</summary>
internal enum ProjectileMotion : byte
{
    None,
    /// <summary>Out along the heading, then back to the muzzle; hits everything once per pass.</summary>
    Boomerang,
    /// <summary>Circles the muzzle; re-hits each target every <c>_tick</c> seconds.</summary>
    Orbit,
    /// <summary>Stationary hazard on the ground that ticks everything standing in it.</summary>
    Puddle,
    /// <summary>Flies to a spot, arms, and blows up on contact or at end of life.</summary>
    Mine,
    /// <summary>Stationary emplacement that fires pooled bolts at the nearest target.</summary>
    Turret,
    /// <summary>Flies to a spot, then pulls and grinds what is near, then collapses.</summary>
    Vortex,
    /// <summary>Straight shot that redirects to the next nearest target on each hit.</summary>
    Ricochet,
    /// <summary>Falls from the sky onto a marked point and detonates.</summary>
    Strike,
    /// <summary>A ring expanding along the ground from where it was cast.</summary>
    Ring,
}

/// <summary>
/// The expansion's half of <see cref="Projectile"/>: extra flight programs for
/// the weapons in <c>Combat/Weapons</c>, on the same pooled instances, the same
/// one-loop update and the same hit reporting as every other shot.
///
/// A projectile with a <see cref="ProjectileMotion"/> other than None is stepped
/// here instead of by the mode switch; <see cref="ClearMotion"/> is called on
/// launch and on retire, so a recycled instance can never inherit a program.
/// </summary>
public sealed partial class Projectile
{
    private static readonly List<IDamageable> MotionBuffer = new(64);

    private ProjectileMotion _motion;
    private Vector3 _anchor;          // orbit centre offset / target point / ring centre
    private float _p0, _p1, _p2;      // per-motion parameters, see SpawnMotion callers
    private float _tick;              // seconds between ticks (puddle, orbit re-hit, turret, vortex)
    private float _tickTimer;
    private int _counter;             // turret bolts per burst
    private bool _phase;              // boomerang returning / mine landed / vortex holding
    private float _spin;

    private void ClearMotion()
    {
        _motion = ProjectileMotion.None;
        _phase = false;
        _tickTimer = 0f;
        _counter = 0;
        _spin = 0f;
    }

    /// <summary>Parameters for <see cref="SpawnMotion"/>. A class-free bag so a
    /// call site names what each number means.</summary>
    internal struct MotionSpec
    {
        public ProjectileMotion Motion;
        public Mesh Mesh;
        public float MeshScale;
        public Vector3 Anchor;
        public float P0, P1, P2;
        public float Tick;
        public int Counter;
    }

    /// <summary>Put a programmed shot in the air through the ordinary pool.
    /// Null when the pool is full, exactly like <see cref="Spawn"/>.</summary>
    internal static Projectile? SpawnMotion(WeaponSystem system, in ProjectileSpec spec, DamageInfo template, in MotionSpec m)
    {
        Projectile? p = Spawn(system, spec, template);
        if (p is null) return null;

        p._motion = m.Motion;
        p._anchor = m.Anchor;
        p._p0 = m.P0; p._p1 = m.P1; p._p2 = m.P2;
        p._tick = MathF.Max(0.05f, m.Tick);
        p._counter = m.Counter;
        if (spec.Lifetime > 0f) p._life = MathF.Min(spec.Lifetime, 20f);

        p._mesh.Mesh = m.Mesh;
        p._mesh.RotationDegrees = Vector3.Zero;
        p._mesh.Position = Vector3.Zero;
        p._mesh.Scale = Vector3.One * MathF.Max(0.05f, m.MeshScale);
        p._mesh.MaterialOverride = Weapons.ExpansionVisuals.Glow(spec.Tint);
        return p;
    }

    /// <summary>A straight glowing bar from <paramref name="from"/> to <paramref name="to"/>
    /// that fades — chain links, beams. Cosmetic only; damage is the caller's.</summary>
    internal static void SpawnStreak(Vector3 from, Vector3 to, float width, Color tint, float life = 0.14f)
    {
        Vector3 d = to - from;
        d.Y = 0f;
        float length = d.Length();
        if (length < 0.05f) return;

        Projectile? p = _pool?.Get();
        if (p is null) return;

        ProjectileSpec spec = new()
        {
            Mode = ProjectileMode.Cosmetic,
            Origin = from,
            Direction = d,
            Tint = tint,
            Lifetime = life,
        };
        p._cosmeticScale = new Vector3(width, width * 0.6f, length);
        p.Launch(null, spec, null);
        p._mesh.Position = new Vector3(0f, 0f, -length * 0.5f);
        Live.Add(p);
    }

    // ----------------------------------------------------------------- step

    private void StepMotion(float dt)
    {
        if (_age >= _life)
        {
            if (_motion is ProjectileMotion.Mine or ProjectileMotion.Strike or ProjectileMotion.Vortex) Detonate(GlobalPosition);
            else Retire();
            return;
        }

        switch (_motion)
        {
            case ProjectileMotion.Boomerang: StepBoomerang(dt); break;
            case ProjectileMotion.Orbit: StepOrbit(dt); break;
            case ProjectileMotion.Puddle: StepPuddle(dt); break;
            case ProjectileMotion.Mine: StepMine(dt); break;
            case ProjectileMotion.Turret: StepTurret(dt); break;
            case ProjectileMotion.Vortex: StepVortex(dt); break;
            case ProjectileMotion.Ricochet: StepRicochet(dt); break;
            case ProjectileMotion.Strike: StepStrike(); break;
            case ProjectileMotion.Ring: StepRing(); break;
        }
    }

    private Vector3 Muzzle => _system?.MuzzlePosition ?? GlobalPosition;

    private void Spin(float dt, float rate)
    {
        _spin += rate * dt;
        _mesh.Rotation = new Vector3(0f, _spin, 0f);
    }

    /// <summary>Hit everything in <paramref name="radius"/> not already in the
    /// pierced list, and remember it. The pass-through contact every
    /// non-stopping motion uses.</summary>
    private void SweepOnce(Vector3 at, float radius, float scale = 1f)
    {
        int n = Targeting.Overlap(at, radius, MotionBuffer);
        for (int i = 0; i < n; i++)
        {
            IDamageable t = MotionBuffer[i];
            if (_pierced.Contains(t)) continue;
            _pierced.Add(t);
            ApplyHit(t, at, scale);
        }
    }

    // _p0 = outward seconds, _p1 = return speed multiplier
    private void StepBoomerang(float dt)
    {
        Spin(dt, 18f);
        if (!_phase && _age >= _p0)
        {
            _phase = true;
            _pierced.Clear();          // a fresh pass on the way home
        }

        Vector3 pos = GlobalPosition;
        if (_phase)
        {
            Vector3 home = Muzzle - pos;
            home.Y = 0f;
            if (home.Length() < 0.9f) { Retire(); return; }
            _velocity = home.Normalized() * _velocity.Length();
        }
        else
        {
            // Ease off toward the turn, so it hangs at the far end before returning.
            float t = Mathf.Clamp(_age / MathF.Max(0.01f, _p0), 0f, 1f);
            pos += _velocity * dt * Mathf.Lerp(1.15f, 0.35f, t);
            GlobalPosition = pos;
            SweepOnce(pos, _hitRadius);
            return;
        }

        pos += _velocity * dt * _p1;
        GlobalPosition = pos;
        SweepOnce(pos, _hitRadius);
    }

    // _p0 = start angle, _p1 = angular speed (rad/s), _p2 = orbit radius
    private void StepOrbit(float dt)
    {
        float a = _p0 + _p1 * _age;
        Vector3 c = Muzzle;
        Vector3 at = new Vector3(c.X + MathF.Sin(a) * _p2, c.Y - 0.15f, c.Z + MathF.Cos(a) * _p2);
        // Point the fin along its direction of travel.
        Place(at, new Vector3(MathF.Cos(a), 0f, -MathF.Sin(a)) * MathF.Sign(_p1 == 0f ? 1f : _p1));

        _tickTimer += dt;
        if (_tickTimer >= _tick) { _tickTimer = 0f; _pierced.Clear(); }
        SweepOnce(at, _hitRadius);

        // Shrink away over the last fifth of its life so the set ending is visible.
        float left = _life - _age;
        if (left < _life * 0.2f) _mesh.Transparency = 1f - left / (_life * 0.2f);
    }

    private void StepPuddle(float dt)
    {
        _tickTimer -= dt;
        float left = _life - _age;
        _mesh.Transparency = left < 0.5f ? 1f - left / 0.5f : 0.15f;
        if (_tickTimer > 0f) return;
        _tickTimer = _tick;

        int n = Targeting.Overlap(GlobalPosition, _hitRadius, MotionBuffer);
        for (int i = 0; i < n; i++) ApplyHit(MotionBuffer[i], GlobalPosition, 1f);
    }

    // _anchor = landing point; _p0 = arm delay
    private void StepMine(float dt)
    {
        Spin(dt, 2f);
        if (!_phase)
        {
            Vector3 pos = GlobalPosition;
            Vector3 to = _anchor - pos;
            to.Y = 0f;
            float step = _velocity.Length() * dt;
            if (to.Length() <= step)
            {
                GlobalPosition = _anchor;
                _phase = true;
                _tickTimer = _p0;
            }
            else GlobalPosition = pos + to.Normalized() * step;
            return;
        }

        if (_tickTimer > 0f) { _tickTimer -= dt; return; }
        // Armed: a slow pulse so it reads as live.
        _mesh.Scale = Vector3.One * (_p1 * (1f + 0.12f * MathF.Sin(_age * 9f)));
        if (Targeting.Overlap(GlobalPosition, _hitRadius, MotionBuffer) > 0) Detonate(GlobalPosition);
    }

    // _p0 = bolt range, _p1 = bolt speed, _counter = bolts per burst
    private void StepTurret(float dt)
    {
        _tickTimer -= dt;
        IDamageable? target = Targeting.Nearest(GlobalPosition, _p0);
        if (target is not null)
        {
            Vector3 to = target.Position - GlobalPosition;
            to.Y = 0f;
            if (to.LengthSquared() > 1e-4f) Place(GlobalPosition, to);
        }

        if (_tickTimer > 0f || target is null || _system is null || _template is null) return;
        _tickTimer = _tick;

        Vector3 dir = FlatDirection(target.Position - GlobalPosition, Vector3.Forward);
        int bolts = Math.Max(1, _counter);
        for (int i = 0; i < bolts; i++)
        {
            float off = bolts <= 1 ? 0f : 14f * (i / (float)(bolts - 1) - 0.5f);
            ProjectileSpec bolt = new()
            {
                Mode = ProjectileMode.Straight,
                Origin = GlobalPosition + Vector3.Up * 0.1f,
                Direction = dir.Rotated(Vector3.Up, Mathf.DegToRad(off)),
                Speed = _p1,
                Range = _p0 + 2f,
                HitRadius = 0.28f,
                Pierce = 0,
                Tint = _tint,
            };
            Spawn(_system, bolt, _template.Clone());
        }
    }

    // _anchor = hold point; _p0 = pull radius; _p1 = pull strength
    private void StepVortex(float dt)
    {
        Spin(dt, 9f);
        if (!_phase)
        {
            Vector3 pos = GlobalPosition;
            Vector3 to = _anchor - pos;
            to.Y = 0f;
            float step = _velocity.Length() * dt;
            if (to.Length() <= step) { GlobalPosition = _anchor; _phase = true; }
            else GlobalPosition = pos + to.Normalized() * step;
            return;
        }

        _mesh.Scale = Vector3.One * (_p2 * (1f + 0.1f * MathF.Sin(_age * 14f)));
        _tickTimer -= dt;
        if (_tickTimer > 0f || _template is null) return;
        _tickTimer = _tick;

        Vector3 c = GlobalPosition;
        int n = Targeting.Overlap(c, _p0, MotionBuffer);
        for (int i = 0; i < n; i++)
        {
            IDamageable t = MotionBuffer[i];
            DamageInfo info = _template.Clone();
            info.Amount *= 0.06f;                    // grinding ticks; the collapse is the real hit
            info.Knockback = _p1;
            info.ProcCoefficient *= 0.25f;
            info.Direction = FlatDirection(c - t.Position, Vector3.Zero);   // inward: a pull
            t.TakeDamage(info);
            _system?.ReportHit(t, info);
        }
    }

    // _pierceLeft = bounces; _p0 = bounce search radius
    private void StepRicochet(float dt)
    {
        Spin(dt, 20f);
        Vector3 next = GlobalPosition + _velocity * dt;
        GlobalPosition = next;

        int n = Targeting.Overlap(next, _hitRadius, MotionBuffer);
        for (int i = 0; i < n; i++)
        {
            IDamageable t = MotionBuffer[i];
            if (_pierced.Contains(t)) continue;
            _pierced.Add(t);
            DamageInfo info = ApplyHit(t, next, 1f);
            if (info.Nullified) continue;
            if (_pierceLeft-- <= 0) { Retire(); return; }

            IDamageable? bounce = NearestExcept(next, _p0);
            if (bounce is null) { Retire(); return; }
            _velocity = FlatDirection(bounce.Position - next, _velocity) * _velocity.Length();
            _age = 0f;   // each leg gets a fresh range
            Place(next, _velocity);
            return;
        }
    }

    private IDamageable? NearestExcept(Vector3 from, float radius)
    {
        int n = Targeting.Overlap(from, radius, MotionBuffer);
        IDamageable? best = null;
        float bestSq = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            IDamageable t = MotionBuffer[i];
            if (_pierced.Contains(t)) continue;
            Vector3 d = t.Position - from;
            d.Y = 0f;
            float sq = d.LengthSquared();
            if (sq < bestSq) { bestSq = sq; best = t; }
        }
        return best;
    }

    // _anchor = ground point; _p0 = drop height
    private void StepStrike()
    {
        float t = Mathf.Clamp(_age / MathF.Max(0.01f, _life), 0f, 1f);
        GlobalPosition = _anchor + Vector3.Up * (_p0 * (1f - t * t));
        _mesh.Rotation = new Vector3(_age * 5f, _age * 3f, 0f);
    }

    // _anchor = centre; _p0 = expansion speed; _p1 = mesh height scale
    private void StepRing()
    {
        float r = MathF.Max(0.3f, _p0 * _age);
        GlobalPosition = _anchor;
        _mesh.Scale = new Vector3(r, _p1, r);
        float t = _age / MathF.Max(0.01f, _life);
        _mesh.Transparency = t * t;
        SweepOnce(_anchor, r);
    }
}
