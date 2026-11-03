using System;
using Godot;
using Wavebreak.Meta;

namespace Wavebreak.Combat.Weapons;

/// <summary>
/// The short-lived shapes that tell look-alike weapons apart where they land:
/// a crescent sweep, a slam crater, a thrust, a breath of sleet, a lob's landing
/// ring, a frost star, a spore puff, a jagged bolt, a pillar of light.
///
/// They run on <see cref="SignatureFx"/> (stepped from Projectile.UpdateAll,
/// paused with the run, drained by the audit's fire test), follow the
/// <see cref="FxGuard"/> rules (flat on the ground, sizes capped, faded through
/// <see cref="GraphicsSettings.FxFade"/>), soften under reduced flashes, and
/// are budgeted: a live cap, and per-hit sparks are rate-limited and skipped on
/// the Low preset. A dropped effect never changes a hit.
/// </summary>
internal static class DistinctFx
{
    private const int MaxLive = 40;
    private const int HitSparksPerSecond = 18;

    private static int _live;
    private static double _sparkWindow;
    private static int _sparksInWindow;

    /// <summary>Effects alive now (the check reads it to prove the cap).</summary>
    public static int Live => _live;

    /// <summary>Most effects alive at once since the last reset (the check).</summary>
    public static int Peak { get; set; }

    private static Color Soft(Color c) => c.Lightened(0.15f);

    /// <summary>Extra transparency under reduced flashes, for anything bright.</summary>
    private static float Glare(float own) => Accessibility.ReduceFlashes ? MathF.Max(own, 0.7f) : own;

    // ------------------------------------------------------------- core

    /// <summary>One mesh that moves, scales and fades over its life.</summary>
    private sealed class Anim : SignatureFx.IFx
    {
        private readonly MeshInstance3D? _mi;
        private readonly Func<float, Transform3D> _place;
        private readonly Func<float, float> _fade;
        private readonly float _life;
        private float _age;

        public Anim(Mesh mesh, Color tint, float life, Func<float, Transform3D> place, Func<float, float> fade, bool additive)
        {
            _life = MathF.Max(0.05f, life);
            _place = place;
            _fade = fade;
            _mi = SignatureFx.NewMesh(mesh, additive ? CombatVisuals.Glow(tint, additive: true) : ExpansionVisuals.Glow(tint));
            Apply(0f);
        }

        private void Apply(float t)
        {
            if (_mi is null || !GodotObject.IsInstanceValid(_mi)) return;
            _mi.GlobalTransform = _place(t);
            _mi.Transparency = SignatureFx.Fade(Mathf.Clamp(_fade(t), 0f, 1f));
        }

        public bool Step(float dt)
        {
            _age += dt;
            if (_age >= _life) return false;
            Apply(_age / _life);
            return true;
        }

        public void Free()
        {
            SignatureFx.FreeNode(_mi);
            _live--;
        }
    }

    private static void Play(Mesh mesh, Color tint, float life, Func<float, Transform3D> place, Func<float, float> fade, bool additive = false)
    {
        if (_live >= MaxLive) return;
        var a = new Anim(mesh, tint, life, place, fade, additive);
        _live++;
        if (_live > Peak) Peak = _live;
        if (!SignatureFx.Add(a)) a.Free();
    }

    /// <summary>Run <paramref name="act"/> a moment later, on the run's clock.</summary>
    private sealed class Later : SignatureFx.IFx
    {
        private readonly Action _act;
        private float _left;
        public Later(float delay, Action act) { _left = delay; _act = act; }
        public bool Step(float dt)
        {
            if ((_left -= dt) > 0f) return true;
            _act();
            return false;
        }
        public void Free() { }
    }

    public static void After(float delay, Action act)
    {
        if (delay <= 0f) { act(); return; }
        SignatureFx.Add(new Later(delay, act));
    }

    private static Basis Facing(Vector3 dir)
    {
        dir.Y = 0f;
        if (dir.LengthSquared() < 1e-6f) return Basis.Identity;
        dir = dir.Normalized();
        return new Basis(Vector3.Up, MathF.Atan2(-dir.X, -dir.Z));
    }

    private static Vector3 Ground(Vector3 at) => at + Vector3.Up * FxGuard.GroundLift;

    // ------------------------------------------------------------- impacts

    /// <summary>A lob's landing spot, marked while the shell is in the air.</summary>
    public static void Telegraph(Vector3 at, float radius, Color tint, float life)
    {
        if (!WeaponLooks.Extras) return;
        float r = Mathf.Clamp(radius, 0.5f, FxGuard.MaxBlobRadius);
        Vector3 g = Ground(at with { Y = at.Y - 0.25f });
        Play(LookMeshes.Hoop, tint, life, t => new Transform3D(Basis.Identity.Scaled(new Vector3(r * (1.15f - 0.15f * t), 1f, r * (1.15f - 0.15f * t))), g),
            t => 0.55f - 0.35f * t);
    }

    /// <summary>What a burst looks like, by kind. <paramref name="radius"/> is the
    /// blast radius; drawn sizes are capped.</summary>
    public static void Impact(ImpactKind kind, Vector3 at, float radius, Color tint)
    {
        float r = Mathf.Clamp(radius, 0.4f, FxGuard.MaxBlobRadius);
        Vector3 g = Ground(at with { Y = at.Y - 0.25f });
        switch (kind)
        {
            case ImpactKind.Frost:
            case ImpactKind.Glass:
                // A flat star that spins open and fades.
                Play(LookMeshes.Star, Soft(tint), 0.32f,
                    t => new Transform3D(new Basis(Vector3.Up, t * 1.4f).Scaled(Vector3.One * r * 0.6f * (0.4f + 0.6f * MathF.Sqrt(t))), g),
                    t => Glare(0.2f + 0.8f * t * t));
                break;
            case ImpactKind.Spore:
                // A soft puff that rises and swells.
                Play(LookMeshes.Puff, tint, 0.5f,
                    t => new Transform3D(Basis.Identity.Scaled(Vector3.One * r * (0.35f + 0.4f * t)), g + Vector3.Up * (0.2f + 0.6f * t)),
                    t => 0.35f + 0.65f * t);
                break;
            case ImpactKind.Debris:
                // A cracked ring of rubble kicked out from the centre.
                Play(LookMeshes.Crater, tint.Darkened(0.25f), 0.45f,
                    t => new Transform3D(Basis.Identity.Scaled(new Vector3(r * (0.5f + 0.5f * t), 0.6f, r * (0.5f + 0.5f * t))), g),
                    t => 0.15f + 0.85f * t * t);
                break;
            case ImpactKind.Fire:
                Play(LookMeshes.FlameRing, tint, 0.4f,
                    t => new Transform3D(Basis.Identity.Scaled(new Vector3(r * (0.4f + 0.6f * t), 1f - 0.6f * t, r * (0.4f + 0.6f * t))), g),
                    t => Glare(0.2f + 0.8f * t));
                break;
            case ImpactKind.Spark:
                Play(LookMeshes.SparkRing, tint, 0.25f,
                    t => new Transform3D(new Basis(Vector3.Up, t * 2f).Scaled(new Vector3(r * (0.3f + 0.7f * t), 1f, r * (0.3f + 0.7f * t))), g),
                    t => Glare(0.15f + 0.85f * t));
                break;
            case ImpactKind.Thunder:
                Pillar(at, tint);
                break;
        }
    }

    /// <summary>A small per-hit mark in the weapon's impact style; rate-limited
    /// and skipped on the Low preset.</summary>
    public static void HitSpark(ImpactKind kind, Vector3 at, Color tint)
    {
        if (!WeaponLooks.Extras || kind is ImpactKind.Blast or ImpactKind.Thunder) return;
        double now = Time.GetTicksMsec() / 1000.0;
        if (now - _sparkWindow > 1.0) { _sparkWindow = now; _sparksInWindow = 0; }
        if (++_sparksInWindow > HitSparksPerSecond) return;
        Impact(kind, at, 0.55f, tint);
    }

    /// <summary>A thin pillar of light on a body (Judgement Lance, Final Verdict).</summary>
    public static void Pillar(Vector3 at, Color tint)
    {
        Vector3 foot = at with { Y = at.Y - 0.3f };
        SignatureFx.Add(new SignatureFx.Wire(foot + Vector3.Up * 6f, foot, 0.12f, tint.Lightened(0.3f), 0.3f));
        Play(LookMeshes.Hoop, tint, 0.3f, t => new Transform3D(Basis.Identity.Scaled(new Vector3(0.6f + 0.6f * t, 1f, 0.6f + 0.6f * t)), Ground(foot)),
            t => Glare(0.2f + 0.8f * t));
    }

    /// <summary>A jagged bolt between two points: a few kinked wires, so a chain
    /// reads as lightning and not as a beam.</summary>
    public static void Jagged(Vector3 a, Vector3 b, Color tint, float width, float life, int segments = 4)
    {
        Vector3 d = b - a;
        if (d.LengthSquared() < 0.01f) return;
        Vector3 side = d.Cross(MathF.Abs(d.Normalized().Y) > 0.9f ? Vector3.Right : Vector3.Up).Normalized();
        float kink = MathF.Min(0.6f, d.Length() * 0.12f);
        Vector3 prev = a;
        for (int i = 1; i <= segments; i++)
        {
            Vector3 next = i == segments ? b : a + d * (i / (float)segments) + side * (kink * (i % 2 == 0 ? -1f : 1f));
            SignatureFx.Add(new SignatureFx.Wire(prev, next, width, tint.Lightened(0.2f), life));
            prev = next;
        }
    }

    // ------------------------------------------------------------- swings

    /// <summary>A crescent that sweeps across the arc it hit (Cleaver Arc,
    /// Skyline Twin Swords). <paramref name="sweepSign"/> picks left-to-right
    /// or right-to-left.</summary>
    public static void Sweep(Vector3 origin, Vector3 dir, float reach, float halfArcDeg, Color tint, float sweepSign = 1f, float life = 0.18f, Mesh? mesh = null)
    {
        float r = MathF.Min(reach, FxGuard.MaxSheet);
        Vector3 g = Ground(origin - Vector3.Up * (FxGuard.MuzzleHeight - 0.1f));
        Basis face = Facing(dir);
        float half = Mathf.DegToRad(Mathf.Clamp(halfArcDeg, 10f, 89f));
        Play(mesh ?? LookMeshes.Crescent, tint, life, t =>
        {
            // The crescent covers +-60 degrees; turn it across the swing.
            float swing = sweepSign * Mathf.Lerp(-half * 0.6f, half * 0.6f, t);
            float w = half / (Mathf.Pi / 3f);
            return new Transform3D((face * new Basis(Vector3.Up, swing)).Scaled(new Vector3(r * MathF.Max(0.5f, w), 1f, r)), g);
        }, t => 0.25f + 0.75f * t * t);
    }

    /// <summary>A narrow thrust: crossed cuts stretched along the line (Riptide Blades).</summary>
    public static void Thrust(Vector3 origin, Vector3 dir, float length, float sideOffset, Color tint)
    {
        Basis face = Facing(dir);
        Vector3 g = Ground(origin - Vector3.Up * (FxGuard.MuzzleHeight - 0.25f)) + face * new Vector3(sideOffset, 0f, 0f);
        float len = MathF.Min(length, FxGuard.MaxSheet);
        Play(LookMeshes.CrossCut, tint, 0.2f,
            t => new Transform3D(face.Scaled(new Vector3(2.6f, 1f, len * (0.55f + 0.45f * t))), g),
            t => 0.2f + 0.8f * t);
    }

    /// <summary>The Maul's slam: a cracked crater and a shove of dust.</summary>
    public static void Slam(Vector3 centre, float radius, Color tint)
    {
        float r = MathF.Min(radius, FxGuard.MaxBlobRadius * 1.3f);
        Vector3 g = Ground(centre);
        Play(LookMeshes.Crater, tint, 0.5f,
            t => new Transform3D(Basis.Identity.Scaled(new Vector3(r * (0.7f + 0.3f * t), 1f, r * (0.7f + 0.3f * t))), g),
            t => 0.1f + 0.9f * t * t);
        Play(LookMeshes.Hoop, tint.Lightened(0.2f), 0.3f,
            t => new Transform3D(Basis.Identity.Scaled(new Vector3(r * (0.3f + 0.9f * t), 1f, r * (0.3f + 0.9f * t))), g),
            t => Glare(0.1f + 0.9f * t), additive: true);
        if (WeaponLooks.Extras)
            for (int i = 0; i < 3; i++)
            {
                float a = i * Mathf.Tau / 3f + 0.4f;
                Vector3 p = g + new Vector3(MathF.Sin(a), 0f, MathF.Cos(a)) * r * 0.55f;
                Play(LookMeshes.Puff, tint.Darkened(0.2f), 0.45f,
                    t => new Transform3D(Basis.Identity.Scaled(Vector3.One * (0.25f + 0.25f * t)), p + Vector3.Up * (0.1f + 0.7f * t)),
                    t => 0.3f + 0.7f * t);
            }
    }

    /// <summary>The horn's breath: puffs of sleet that drift out along the cone.</summary>
    public static void Breath(Vector3 origin, Vector3 dir, float reach, float halfDeg, Color tint)
    {
        Basis face = Facing(dir);
        float r = MathF.Min(reach, FxGuard.MaxSheet);
        Vector3 g = origin - Vector3.Up * (FxGuard.MuzzleHeight - 0.45f);
        int puffs = WeaponLooks.Extras ? 4 : 2;
        float half = Mathf.DegToRad(Mathf.Clamp(halfDeg, 5f, 60f));
        for (int i = 0; i < puffs; i++)
        {
            float side = (i / (float)Math.Max(1, puffs - 1) - 0.5f) * half;
            float delay = i * 0.03f;
            Vector3 lane = face * new Vector3(MathF.Sin(side), 0f, -MathF.Cos(side));
            Play(LookMeshes.Puff, tint, 0.3f + delay,
                t => new Transform3D(Basis.Identity.Scaled(Vector3.One * (0.15f + 0.35f * t)), g + lane * (r * t)),
                t => 0.3f + 0.7f * t);
        }
    }

    /// <summary>Twin Horizons' echo: a thin bright line laid across the far
    /// edge of the swing.</summary>
    public static void Horizon(Vector3 origin, Vector3 dir, float reach, Color tint)
    {
        Basis face = Facing(dir);
        float r = MathF.Min(reach, FxGuard.MaxSheet);
        Vector3 g = Ground(origin - Vector3.Up * (FxGuard.MuzzleHeight - 0.3f)) + face * new Vector3(0f, 0f, -r * 0.85f);
        Play(CombatVisuals.Slab, tint, 0.22f,
            t => new Transform3D(face.Scaled(new Vector3(r * 1.5f * (0.4f + 0.6f * t), 0.03f, 0.12f)), g),
            t => Glare(0.15f + 0.85f * t), additive: true);
    }

    /// <summary>Static Field's pulse: a zigzag ring that rushes out to the edge.</summary>
    public static void Pulse(Vector3 centre, float radius, Color tint)
    {
        float r = MathF.Min(radius, FxGuard.MaxCentredRadius);
        Vector3 g = Ground(centre - Vector3.Up * (FxGuard.MuzzleHeight - 0.05f));
        Play(LookMeshes.SparkRing, tint, 0.28f,
            t => new Transform3D(new Basis(Vector3.Up, t * 1.2f).Scaled(new Vector3(r * (0.25f + 0.75f * t), 1f, r * (0.25f + 0.75f * t))), g),
            t => Glare(0.1f + 0.9f * t * t), additive: true);
    }

    /// <summary>Breaching Gun's shaped charge: puffs blown on through the body.</summary>
    public static void Charge(Vector3 at, Vector3 dir, float reach, Color tint)
    {
        Basis face = Facing(dir);
        Vector3 g = at with { Y = at.Y - 0.1f };
        for (int i = 0; i < 3; i++)
        {
            float k = (i + 1) / 3f;
            Play(LookMeshes.Puff, tint, 0.3f,
                t => new Transform3D(Basis.Identity.Scaled(Vector3.One * (0.2f + 0.3f * k) * (0.6f + 0.4f * t)), g + face * new Vector3(0f, 0f, -reach * k * (0.5f + 0.5f * t))),
                t => 0.3f + 0.7f * t);
        }
    }
}
