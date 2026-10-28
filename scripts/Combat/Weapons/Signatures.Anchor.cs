using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Data;
using Wavebreak.Stats;

namespace Wavebreak.Combat.Weapons;

public static partial class Signatures
{
    // ========================================================= MOORING ANCHOR
    //
    // The control one. Hurls an anchor at the nearest enemy. Where it lands it
    // hits, then moors a ring of ground: everything inside is slowed, chafed
    // by the ropes twice a second, and anything trying to walk out of it is
    // pulled back in. When the mooring runs out the anchor is hauled home and
    // everything still inside takes the haul.

    private const int MaxAnchors = 3;
    private static readonly Color RopeColor = new(0.95f, 0.88f, 0.70f);

    private static void FireAnchor(WeaponRuntime w, WeaponSystem sys, StatSheet s)
    {
        WeaponDef d = w.Def;
        Vector3 origin = sys.MuzzlePosition;
        float range = Reach(w);
        int shots = Shots(w, s);
        int n = Math.Min(shots, MaxAnchors);
        float scale = shots > n ? (float)shots / n : 1f;   // extra anchors fold into damage
        float ring = MathF.Max(1.5f, d.Radius * Area(w, s));
        float speed = MathF.Max(6f, d.ProjectileSpeed * (1f + w.Bonus.ProjectileSpeed) * s.Get(Stat.ProjectileSpeed));
        float moor = 3.0f * Duration(s) * (w.Level >= d.MaxLevel ? 1.3f : 1f);

        var used = new List<IDamageable>(n);
        IDamageable? nearest = NearestLive(origin, range);
        for (int i = 0; i < n; i++)
        {
            IDamageable? target = i == 0 ? nearest : RandomOther(origin, range, used);
            Vector3 land;
            if (target is not null)
            {
                used.Add(target);
                land = target.Position;
                // On the floor under the target, not at the thrower's height:
                // a player in mid-jump must not moor a ring in the air.
                land.Y = target is Enemy e ? e.GlobalPosition.Y : FloorUnder(sys, land);
            }
            else
            {
                Vector3 look = Projectile.FlatDirection(sys.AimDirection, Vector3.Forward);
                float a = (i - (n - 1) * 0.5f) * 0.6f;
                land = Ground(origin) + look.Rotated(Vector3.Up, a) * (range * 0.5f);
                land.Y = FloorUnder(sys, land);
            }
            DamageInfo template = sys.BuildDamage(w);
            template.Amount *= scale;
            SignatureFx.Add(new MooredAnchor(sys, w, template, origin, land, ring, moor, speed, d.Tint));
        }
        Cue(SignatureSound.AnchorThrow, origin);
        sys.NoteShots(n);
    }

    /// <summary>Height of the ground under <paramref name="at"/> (a ray down
    /// through the world), or the player's feet when nothing is hit.</summary>
    private static float FloorUnder(WeaponSystem sys, Vector3 at)
    {
        float fallback = sys.Body is Player p && GodotObject.IsInstanceValid(p) ? p.GlobalPosition.Y : Ground(sys.MuzzlePosition).Y;
        if (sys.Body is not { } body || !GodotObject.IsInstanceValid(body) || !body.IsInsideTree()) return fallback;
        var space = body.GetWorld3D()?.DirectSpaceState;
        if (space is null) return fallback;
        var query = PhysicsRayQueryParameters3D.Create(at with { Y = fallback + 12f }, at with { Y = fallback - 30f });
        query.CollideWithAreas = false;
        if (body is CollisionObject3D co) query.Exclude = new Godot.Collections.Array<Rid> { co.GetRid() };
        var hit = space.IntersectRay(query);
        return hit.Count > 0 && hit.TryGetValue("position", out Variant v) ? v.AsVector3().Y : fallback;
    }

    private static IDamageable? RandomOther(Vector3 origin, float range, List<IDamageable> used)
    {
        int found = Targeting.Overlap(origin, range, Buffer);
        for (int tries = 0; tries < 6 && found > 0; tries++)
        {
            IDamageable t = Buffer[FxRng.RangeInt(0, found)];
            bool near = false;
            foreach (IDamageable u in used) if (FlatDistance(u.Position, t.Position) < 3f) { near = true; break; }
            if (!near) return t;
        }
        return null;
    }

    /// <summary>One thrown anchor: flight, mooring, haul.</summary>
    private sealed class MooredAnchor : SignatureFx.IFx
    {
        private const float Flight = 0.38f;
        private const float TickEvery = 0.5f;
        private const float HaulTime = 0.28f;
        private const float ImpactScale = 1.0f;
        private const float TickScale = 0.12f;
        private const float HaulScale = 1.6f;

        private readonly WeaponSystem _sys;
        private readonly WeaponRuntime _w;
        private readonly DamageInfo _template;
        private readonly Vector3 _from, _land;
        private readonly float _ring, _moor, _flight;
        private readonly bool _knot, _deep;
        private readonly MeshInstance3D? _anchor, _rope, _chain;
        private float _age, _tick, _slam = -1f;
        private int _phase;            // 0 flight, 1 moored, 2 haul
        private Vector3 _haulFrom;
        private readonly List<IDamageable> _inside = new(32);

        public MooredAnchor(WeaponSystem sys, WeaponRuntime w, DamageInfo template, Vector3 from, Vector3 land,
                            float ring, float moor, float speed, Color tint)
        {
            _sys = sys; _w = w; _template = template; _from = from; _land = land;
            _ring = ring; _moor = moor;
            _flight = Mathf.Clamp(FlatDistance(from, land) / speed, 0.18f, Flight * 1.6f);
            _knot = w.Level >= MilestoneLevel;
            _deep = w.Level >= w.Def.MaxLevel;
            _anchor = SignatureFx.NewMesh(SignatureFx.Anchor, SignatureFx.Mat(tint));
            _rope = SignatureFx.NewMesh(SignatureFx.RopeRing, SignatureFx.Mat(RopeColor));
            _chain = SignatureFx.NewMesh(CombatVisuals.Slab, SignatureFx.Line(new Color(0.80f, 0.82f, 0.88f)));
            if (_rope is not null) _rope.Visible = false;
            PlaceAnchor(from, 0f);
        }

        private Vector3 Hand => _sys.MuzzlePosition;

        private void PlaceAnchor(Vector3 at, float spin)
        {
            if (_anchor is null) return;
            var b = new Basis(Vector3.Up, spin).Scaled(Vector3.One * 0.55f);
            _anchor.GlobalTransform = new Transform3D(b, at);
        }

        public bool Step(float dt)
        {
            _age += dt;
            switch (_phase)
            {
                case 0:
                {
                    float t = Mathf.Clamp(_age / _flight, 0f, 1f);
                    Vector3 p = _from.Lerp(_land, t) + Vector3.Up * (MathF.Sin(t * Mathf.Pi) * 1.6f);
                    PlaceAnchor(p, t * 9f);
                    if (_chain is not null) { SignatureFx.SetBar(_chain, Hand, p + Vector3.Up * 0.55f, 0.04f); _chain.Transparency = SignatureFx.Fade(0.35f); }
                    if (t >= 1f) Land();
                    return true;
                }
                case 1:
                    return Moored(dt);
                default:
                    return Haul(dt);
            }
        }

        private void Land()
        {
            _phase = 1;
            _age = 0f;
            _tick = TickEvery * 0.5f;
            PlaceAnchor(_land, 0.4f);
            if (_chain is not null) _chain.Visible = false;
            if (_rope is not null)
            {
                _rope.Visible = true;
                float shown = MathF.Min(_ring, FxGuard.MaxCentredRadius);
                _rope.GlobalTransform = new Transform3D(Basis.Identity.Scaled(new Vector3(shown, 1f, shown)), _land + Vector3.Up * FxGuard.GroundLift);
                _rope.Transparency = SignatureFx.Fade(0.2f);
            }
            int n = Targeting.Overlap(_land, MathF.Max(1.6f, _ring * 0.5f), Buffer);
            for (int i = 0; i < n; i++)
            {
                DamageInfo info = _template.Clone();
                info.Knockback = 0f;   // keep them in the ring
                Strike(_sys, Buffer[i], info, _land, ImpactScale);
            }
            SignatureFx.Add(new SignatureFx.Flash(_land + Vector3.Up * 0.3f, 0.6f, RopeColor, 0.22f));
            Cue(SignatureSound.AnchorLand, _land);
        }

        private bool Moored(float dt)
        {
            if (_rope is not null && GodotObject.IsInstanceValid(_rope))
            {
                float pulse = MathF.Max(0f, 1f - (_age % TickEvery) / 0.18f);
                float over = FxGuard.OverPlayer(_land, MathF.Min(_ring, FxGuard.MaxCentredRadius));
                float left = _moor - _age;
                float fadeOut = left < 0.4f ? 1f - left / 0.4f : 0f;
                _rope.Transparency = SignatureFx.Fade(MathF.Max(0.25f - 0.2f * pulse + over * 0.5f, fadeOut));
            }

            _tick -= dt;
            if (_tick <= 0f)
            {
                _tick += TickEvery;
                Chafe();
            }
            if (_age >= _moor) StartHaul();
            return true;
        }

        /// <summary>Twice a second: slow and graze everything moored, and pull
        /// back anything at the rope's edge or just past it.</summary>
        private void Chafe()
        {
            int n = Targeting.Overlap(_land, _ring + 1.3f, Buffer);
            for (int i = 0; i < n; i++)
            {
                IDamageable t = Buffer[i];
                float dist = FlatDistance(t.Position, _land);
                bool inside = dist <= _ring;
                bool edge = dist > _ring * 0.7f;
                if (!inside && !edge) continue;
                DamageInfo info = _template.Clone();
                info.Amount *= inside ? TickScale : 0.03f;
                info.ProcCoefficient *= 0.2f;
                info.Crit = false;
                info.Knockback = edge ? 2.6f + 1.5f * Mathf.Clamp((dist - _ring * 0.7f) / (_ring * 0.3f + 1.3f), 0f, 1f) : 0f;
                info.Direction = Projectile.FlatDirection(_land - t.Position, Vector3.Zero);
                t.TakeDamage(info);
                _sys.ReportHit(t, info);
                Count(info);
                if (t is Enemy e && e.IsAlive) e.ApplyChill(TickEvery + 0.35f, 99, 0f);
            }
        }

        private void StartHaul()
        {
            _phase = 2;
            _age = 0f;
            _haulFrom = _land;
            _inside.Clear();
            int n = Targeting.Overlap(_land, _ring, Buffer);
            for (int i = 0; i < n; i++)
            {
                IDamageable t = Buffer[i];
                DamageInfo info = _template.Clone();
                if (_deep)
                {
                    info.Knockback = 7f;   // drag them into the middle
                    info.Direction = Projectile.FlatDirection(_land - t.Position, Vector3.Zero);
                    info.Amount *= HaulScale;
                    t.TakeDamage(info);
                    _sys.ReportHit(t, info);
                    Count(info);
                }
                else Strike(_sys, t, info, _land, HaulScale);
                if (_knot && t is Enemy e && e.IsAlive) e.ApplyStun(0.6f);
            }
            if (_rope is not null) _rope.Visible = false;
            if (_deep) _slam = 0.32f;
            Cue(SignatureSound.AnchorHaul, _land);
        }

        private bool Haul(float dt)
        {
            if (_slam > 0f)
            {
                _slam -= dt;
                if (_slam <= 0f)
                {
                    int n = Targeting.Overlap(_land, MathF.Max(2f, _ring * 0.6f), Buffer);
                    for (int i = 0; i < n; i++)
                    {
                        DamageInfo info = _template.Clone();
                        info.Knockback = 1f;
                        Strike(_sys, Buffer[i], info, _land, 1.2f);
                    }
                    SignatureFx.Add(new SignatureFx.Flash(_land + Vector3.Up * 0.35f, 0.85f, RopeColor, 0.26f));
                    Cue(SignatureSound.AnchorLand, _land);
                }
                PlaceAnchor(_land + Vector3.Up * (0.9f * MathF.Max(0f, _slam) / 0.32f), 0f);
                return true;
            }

            float t = Mathf.Clamp(_age / HaulTime, 0f, 1f);
            Vector3 p = _haulFrom.Lerp(Hand - Vector3.Up * 0.5f, t * t);
            PlaceAnchor(p, t * 6f);
            if (_chain is not null)
            {
                SignatureFx.SetBar(_chain, Hand, p + Vector3.Up * 0.55f, 0.05f);
                _chain.Transparency = SignatureFx.Fade(0.2f + 0.7f * t);
            }
            if (_anchor is not null) _anchor.Transparency = SignatureFx.Fade(t * t);
            return t < 1f;
        }

        public void Free()
        {
            SignatureFx.FreeNode(_anchor);
            SignatureFx.FreeNode(_rope);
            SignatureFx.FreeNode(_chain);
        }
    }

}
