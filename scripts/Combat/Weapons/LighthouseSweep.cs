using System;
using System.Collections.Generic;
using Godot;

namespace Wavebreak.Combat.Weapons;

/// <summary>
/// Lighthouse Beam's lamp turning: the beam sweeps a 28 degree arc through the
/// aim in a third of a second and burns each body once as it passes. It used
/// to be the same instant line as Truthline Rail's; now one is a lamp and the
/// other is a line. Damage per body is 0.85 of the old line, because a sweep
/// reaches more bodies than a line.
/// </summary>
internal sealed class LighthouseSweep : SignatureFx.IFx
{
    private const float Arc = 28f, Time = 0.3f, Share = 0.85f;
    private static readonly List<IDamageable> Buffer = new(64);

    private readonly WeaponSystem _system;
    private readonly Vector3 _aim;
    private readonly float _range, _width, _sign;
    private readonly DamageInfo _template;
    private readonly HashSet<IDamageable> _hit = new();
    private readonly MeshInstance3D? _beam;
    private float _age;

    public LighthouseSweep(WeaponSystem system, Vector3 origin, Vector3 aim, float range, float width, DamageInfo template, Color tint, float sign)
    {
        _system = system;
        _aim = Projectile.FlatDirection(aim, Vector3.Forward);
        _range = range;
        _width = width;
        _sign = sign;
        _template = template.Clone();
        _template.Amount *= Share;
        _beam = SignatureFx.NewMesh(CombatVisuals.Slab, CombatVisuals.Glow(tint, additive: true));
        Sweep(origin, 0f);
    }

    private Vector3 DirAt(float t)
        => _aim.Rotated(Vector3.Up, Mathf.DegToRad(_sign * Arc * (t - 0.5f)));

    private void Sweep(Vector3 origin, float t)
    {
        Vector3 dir = DirAt(t);
        int n = Targeting.Overlap(origin + dir * (_range * 0.5f), _range * 0.5f + _width, Buffer);
        for (int i = 0; i < n; i++)
        {
            IDamageable target = Buffer[i];
            if (_hit.Contains(target) || !target.IsAlive) continue;
            Vector3 rel = target.Position - origin;
            rel.Y = 0f;
            float along = rel.Dot(dir);
            if (along < -0.5f || along > _range) continue;
            if ((rel - dir * along).Length() > _width + Targeting.AssumedTargetRadius) continue;
            _hit.Add(target);
            DamageInfo info = _template.Clone();
            info.Direction = Projectile.FlatDirection(rel, dir);
            target.TakeDamage(info);
            _system.ReportHit(target, info);
        }
        if (_beam is not null && GodotObject.IsInstanceValid(_beam))
        {
            // Low and flat, capped like every sheet (FxGuard).
            Vector3 a = origin - Vector3.Up * 0.5f;
            Vector3 b = a + dir * MathF.Min(_range, 18f);
            SignatureFx.SetBar(_beam, a, b, MathF.Min(_width * 1.4f, 0.9f));
            _beam.Transparency = SignatureFx.Fade(Meta.Accessibility.ReduceFlashes ? 0.7f : 0.25f + 0.5f * t * t);
        }
    }

    public bool Step(float dt)
    {
        _age += dt;
        float t = MathF.Min(1f, _age / Time);
        Vector3 origin = GodotObject.IsInstanceValid(_system) ? _system.MuzzlePosition : Vector3.Zero;
        Sweep(origin, t);
        return _age < Time;
    }

    public void Free()
    {
        SignatureFx.FreeNode(_beam);
        _hit.Clear();
    }
}
