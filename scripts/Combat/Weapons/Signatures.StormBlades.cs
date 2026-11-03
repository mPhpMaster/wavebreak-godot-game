using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Stats;

namespace Wavebreak.Combat.Weapons;

public static partial class Signatures
{
    private static void FireStormBlades(WeaponRuntime weapon, WeaponSystem system, StatSheet stats)
    {
        Vector3 origin = system.MuzzlePosition;
        float range = Reach(weapon);
        IDamageable? target = null;
        float nearest = range * range;
        foreach (IDamageable candidate in Targeting.Live)
        {
            float distance = candidate.Position.DistanceSquaredTo(origin);
            if (!candidate.IsAlive || distance >= nearest) continue;
            nearest = distance;
            target = candidate;
        }
        Vector3 direction = target is null ? system.AimDirection : target.Position - origin;
        direction = direction.LengthSquared() > 0.001f ? direction.Normalized() : Vector3.Forward;
        Vector3 right = direction.Cross(Vector3.Up).Normalized();
        if (right.LengthSquared() < 0.5f) right = Vector3.Right;
        int count = Math.Clamp(Shots(weapon, stats) + (weapon.Level >= weapon.Def.MaxLevel ? 1 : 0), 2, 8);
        DamageInfo damage = system.BuildDamage(weapon);
        if (weapon.Level >= weapon.Def.MaxLevel) damage.Element = Element.Lightning;
        float movement = system.Body is Player player ? player.Velocity.Length() : 0f;
        damage.Amount *= 1f + Mathf.Min(movement / 100f, 0.25f);
        float speed = Mathf.Max(12f, weapon.Def.ProjectileSpeed * stats.Get(Stat.ProjectileSpeed));
        float radius = Mathf.Max(0.45f, weapon.Def.Radius * Area(weapon, stats));
        for (int index = 0; index < count; index++)
        {
            float spread = count == 1 ? 0f : (index / (float)(count - 1) - 0.5f) * 0.18f;
            Vector3 heading = (direction + right * spread).Normalized();
            SignatureFx.Add(new ReturningStormBlade(system, origin, heading, speed, range,
                radius, damage, weapon.Level >= MilestoneLevel, weapon.Def.Tint));
        }
        system.NoteShots(count);
        Cue(SignatureSound.KiteWhip, origin);
    }

    private sealed class ReturningStormBlade : SignatureFx.IFx
    {
        private readonly WeaponSystem _system;
        private readonly DamageInfo _damage;
        private readonly float _speed, _range, _radius;
        private readonly bool _crosswind;
        private readonly MeshInstance3D? _mesh, _trail;
        private readonly HashSet<IDamageable> _hit = new();
        private Vector3 _position, _direction;
        private Vector3 _tack, _line;
        private float _travelled, _age;
        private bool _returning;

        public ReturningStormBlade(WeaponSystem system, Vector3 origin, Vector3 direction,
            float speed, float range, float radius, DamageInfo damage, bool crosswind, Color tint)
        {
            _system = system; _position = origin; _direction = direction;
            _speed = speed; _range = range; _radius = radius; _damage = damage;
            _crosswind = crosswind;
            // Its own blade, not the Gullwing Chakram's disc (docs/weapon-audit.md).
            _mesh = SignatureFx.NewMesh(LookMeshes.Fang, SignatureFx.Mat(tint));
            _tack = direction;
            _line = origin;
            _trail = SignatureFx.NewMesh(CombatVisuals.Slab, SignatureFx.Line(tint.Lightened(0.25f)));
        }

        public bool Step(float delta)
        {
            if (!GodotObject.IsInstanceValid(_system.Body)
                || _system.Body is Player { IsAlive: false }) return false;
            if (delta <= 0f) return true;
            _age += delta;
            if (_age > 6f) return false;
            Vector3 previous = _position;
            if (_returning)
            {
                Vector3 home = _system.MuzzlePosition - previous;
                if (home.Length() <= Mathf.Max(0.65f, _speed * delta * 1.4f)) return false;
                _direction = home.Normalized();
            }
            float step = _speed * delta * (_returning ? 1.4f : 1f);
            // Outbound, a storm blade tacks like a kite: it zigzags up to 0.7 m
            // either side of its line (a triangle wave, 0.28 s a tack), cutting a
            // wider lane than a straight throw; the line itself still runs
            // through the aimed body (the Gullwing Chakram flies straight and hangs).
            if (!_returning)
            {
                _line += _direction * step;
                Vector3 side = _tack.Cross(Vector3.Up);
                side = side.LengthSquared() > 1e-4f ? side.Normalized() : Vector3.Right;
                float phase = _age / 0.28f + 0.25f;   // on the line at the start of each tack pair
                float tri = 1f - 4f * MathF.Abs(phase - MathF.Floor(phase + 0.5f));
                _position = _line + side * (0.7f * tri);
                if (_age < delta * 1.5f) DistinctStats.Note("fang_tack");
            }
            else _position += _direction * step;
            Sweep(previous, _position);
            _travelled += step;
            if (!_returning && _travelled >= _range)
            {
                _returning = true;
                _hit.Clear();
            }
            if (_mesh is { } mesh && GodotObject.IsInstanceValid(mesh))
            {
                // Points along its flight and rolls, instead of spinning flat like a disc.
                Vector3 h = _returning ? _direction : _tack;
                var face = new Basis(Vector3.Up, MathF.Atan2(-h.X, -h.Z)).Rotated(h.Normalized(), _age * 9f);
                mesh.GlobalTransform = new Transform3D(face.Scaled(Vector3.One * Mathf.Min(_radius * 1.3f, FxGuard.MaxBlade)), _position);
                mesh.Transparency = SignatureFx.Fade(0f);
            }
            if (_trail is { } trail && GodotObject.IsInstanceValid(trail))
            {
                SignatureFx.SetBar(trail, _position - _direction * 1.5f, _position, 0.055f);
                trail.Transparency = SignatureFx.Fade(0.35f);
            }
            return true;
        }

        private void Sweep(Vector3 from, Vector3 to)
        {
            Vector3 segment = to - from;
            float lengthSquared = segment.LengthSquared();
            float reach = _radius + Targeting.AssumedTargetRadius;
            IReadOnlyList<IDamageable> targets = Targeting.Live;
            for (int index = 0; index < targets.Count; index++)
            {
                IDamageable target = targets[index];
                if (!target.IsAlive || _hit.Contains(target)) continue;
                float along = lengthSquared > 0f
                    ? Mathf.Clamp((target.Position - from).Dot(segment) / lengthSquared, 0f, 1f) : 0f;
                if (target.Position.DistanceSquaredTo(from + segment * along) > reach * reach) continue;
                _hit.Add(target);
                DamageInfo damage = _damage.Clone();
                damage.Direction = _direction;
                damage.Amount *= _returning && _crosswind ? 1.5f : 1f;
                target.TakeDamage(damage);
                _system.ReportHit(target, damage);
                Count(damage);
            }
            Targeting.AreaProbe?.Invoke(to, _radius, Vector3.Zero, -1f);
        }

        public void Free()
        {
            SignatureFx.FreeNode(_mesh);
            SignatureFx.FreeNode(_trail);
            _hit.Clear();
        }
    }
}
