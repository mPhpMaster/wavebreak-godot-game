using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Core;

namespace Wavebreak.Combat;

public sealed partial class Projectile
{
    // ------------------------------------------------------------------ step

    private void Step(float dt)
    {
        _age += dt;

        if (_motion != ProjectileMotion.None) { StepMotion(dt); return; }

        if (_mode == ProjectileMode.Cosmetic)
        {
            if (_age >= _life) { Retire(); return; }
            StepCosmetic();
            return;
        }

        if (_age >= _life)
        {
            // A grenade that fizzles out mid-air reads as a bug. Expiry detonates.
            if (_mode == ProjectileMode.Explosive) Detonate(GlobalPosition);
            else Retire();
            return;
        }

        Vector3 next = GlobalPosition + _velocity * dt;

        if (_mode == ProjectileMode.Homing)
        {
            Steer(dt);
            Place(next, _velocity);      // only a seeker's heading changes in flight
        }
        else
        {
            GlobalPosition = next;
        }

        Contact(next);
    }

    private void StepCosmetic()
    {
        float t = Mathf.Clamp(_age / _life, 0f, 1f);
        _mesh.Scale = _cosmeticScale * (0.7f + 0.3f * t);
        // Starts half see-through: a fast melee or breath weapon lays one of
        // these every few frames, and solid ones stacked into a white wall.
        _mesh.Transparency = Wavebreak.Meta.GraphicsSettings.FxFade(0.65f + 0.35f * t * t);
    }

    // Weapons/FxGuard: an effect on the camera's line to the player turns
    // see-through, and gets its own fade back when it moves off it.
    private bool _sightFaded;
    private float _restFade;

    private void KeepOutOfSight()
    {
        Vector3 s = _mesh.Scale;
        float radius = MathF.Max(MathF.Max(MathF.Abs(s.X), MathF.Abs(s.Z)), MathF.Abs(s.Y)) * (_mode == ProjectileMode.Cosmetic ? 0.5f : 1f);
        float block = Weapons.FxGuard.BlocksView(_mesh.GlobalPosition, radius);
        if (block > 0f)
        {
            if (!_sightFaded) { _restFade = _mesh.Transparency; _sightFaded = true; }
            _mesh.Transparency = MathF.Max(_mesh.Transparency, block);
        }
        else if (_sightFaded)
        {
            _sightFaded = false;
            _mesh.Transparency = _restFade;
        }
    }

    private void Contact(Vector3 at)
    {
        int count = Targeting.Overlap(at, _hitRadius, ContactBuffer);

        for (int i = 0; i < count; i++)
        {
            IDamageable target = ContactBuffer[i];
            if (_pierced.Contains(target)) continue;

            if (_mode == ProjectileMode.Explosive)
            {
                Detonate(at);
                return;
            }

            _pierced.Add(target);
            DamageInfo info = ApplyHit(target, at, 1f);

            // A corpse a pierce shot happened to fly through does not spend a
            // pierce charge. The target reports that by nullifying the hit.
            if (info.Nullified) continue;

            if (_pierceLeft-- <= 0)
            {
                Retire();
                return;
            }
        }
    }

    private void Steer(float dt)
    {
        if (_lock is null || !_lock.IsAlive)
            _lock = Targeting.Nearest(GlobalPosition, HomingSeekRange);

        if (_lock is null || !_lock.IsAlive) return;

        Vector3 desired = FlightDirection(_lock.Position - GlobalPosition, Vector3.Zero);
        if (desired.LengthSquared() < 1e-6f) return;

        float speed = _velocity.Length();
        if (speed < 0.01f) return;

        // Turning on the flat plane by angle rather than by Slerp: Slerp is
        // undefined for exactly opposing vectors, which is precisely the case a
        // seeker that overshot its target hits every time.
        float current = MathF.Atan2(_velocity.X, _velocity.Z);
        float target = MathF.Atan2(desired.X, desired.Z);
        float delta = Mathf.Wrap(target - current, -Mathf.Pi, Mathf.Pi);
        float turn = Mathf.Clamp(delta, -HomingTurnRate * dt, HomingTurnRate * dt);

        float heading = current + turn;
        _velocity = new Vector3(MathF.Sin(heading), 0f, MathF.Cos(heading)) * speed;
    }

    private void Detonate(Vector3 at)
    {
        float blast = _blastRadius > 0f ? _blastRadius : _hitRadius;
        int count = Targeting.Overlap(at, blast, BlastBuffer);

        for (int i = 0; i < count; i++)
        {
            IDamageable target = BlastBuffer[i];

            // Full damage at the centre, half at the rim. A flat blast makes
            // where you put it irrelevant; a steep one makes every explosion feel
            // like it missed.
            Vector3 offset = target.Position - at;
            offset.Y = 0f;
            float distance = offset.Length();
            float falloff = blast <= 0.001f ? 1f : Mathf.Lerp(1f, 0.5f, Mathf.Clamp(distance / blast, 0f, 1f));

            ApplyHit(target, at, falloff);
        }

        ExplosionFx.Play(at, blast, _tint);
        Retire();
    }

    private DamageInfo ApplyHit(IDamageable target, Vector3 from, float scale)
    {
        DamageInfo info = _template!.Clone();
        if (scale != 1f) info.Amount *= scale;

        Vector3 push = FlatDirection(target.Position - from, Vector3.Zero);
        info.Direction = push.LengthSquared() > 1e-6f
            ? push.Normalized()
            : FlatDirection(_velocity, Vector3.Forward);

        target.TakeDamage(info);
        _system?.ReportHit(target, info);
        return info;
    }

}
