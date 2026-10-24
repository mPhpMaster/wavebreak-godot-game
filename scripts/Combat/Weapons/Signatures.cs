using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Data;
using Wavebreak.Stats;

namespace Wavebreak.Combat.Weapons;

/// <summary>Which sound a signature weapon wants. Published on the bus so
/// combat holds no reference to audio (<c>Audio/AudioDirector.Heroes.cs</c>).</summary>
public enum SignatureSound { SpyglassCast, SpyglassBullseye, SpyglassRicochet, AnchorThrow, AnchorLand, AnchorHaul, KiteWhip, KiteZap }

public readonly record struct SignatureCue(SignatureSound Sound, Vector3 Position);

/// <summary>
/// Signature weapons: one per hero of the heroes trio (Meta/Characters.Heroes.cs).
///
/// A signature is an ordinary <see cref="WeaponDef"/> in the catalogue (codex,
/// icons, quests, the stat sheet and rolled upgrades all treat it like any
/// other weapon) with three differences:
///
///   * only its owner starts with it, and the offer pool never offers it as a
///     new weapon (<see cref="Progression.OfferPool"/>), so nobody else can hold
///     it; held, it levels through the usual rolled upgrade cards;
///   * it has two milestones of its own, at <see cref="MilestoneLevel"/> and at
///     max level, named on the card that reaches them (<see cref="MilestoneLine"/>);
///   * it is delivered here rather than by the shape switch, and announces its
///     own sounds (the generic shape sound is swallowed, see <see cref="ConsumeFired"/>).
/// </summary>
public static class Signatures
{
    public const string Spyglass = "spyglass_glint";
    public const string Anchor = "mooring_anchor";
    public const string Kite = "skimmer_kite";

    public const int MilestoneLevel = 4;

    /// <summary>Weapon id -> the character it belongs to.</summary>
    public static readonly IReadOnlyDictionary<string, string> Owner = new Dictionary<string, string>
    {
        [Spyglass] = "sunny",
        [Anchor] = "biscuit",
        [Kite] = "flick",
    };

    public static bool Is(string? weaponId) => weaponId is not null && Owner.ContainsKey(weaponId);

    /// <summary>"Lv 4 — Steady Lens: ..." / "Lv 8 — ..." text per weapon, shown
    /// on the card that reaches the level and in the codex.</summary>
    public static (string Name, string Text) Milestone(string id, bool max) => (id, max) switch
    {
        (Spyglass, false) => ("Steady Lens", "+1 pierce, and the sight cone widens."),
        (Spyglass, true) => ("Sun Glint", "A bullseye kill reflects the beam into the farthest enemy nearby."),
        (Anchor, false) => ("Bosun's Knot", "The haul stuns everything still moored."),
        (Anchor, true) => ("Deep Mooring", "The haul drags the moored together, then the anchor slams down again."),
        (Kite, false) => ("Twin Tails", "A second kite joins the first."),
        (Kite, true) => ("Storm Kite", "Every 2 s each kite drops lightning on an enemy near it."),
        _ => ("", ""),
    };

    /// <summary>The extra card line when <paramref name="nextLevel"/> is a milestone, else "".</summary>
    public static string MilestoneLine(WeaponDef def, int nextLevel)
    {
        if (!Is(def.Id)) return "";
        bool max = nextLevel >= def.MaxLevel;
        if (!max && nextLevel != MilestoneLevel) return "";
        var (name, text) = Milestone(def.Id, max);
        return "\n" + Meta.Loc.F("★ {0}: {1}", name, text);
    }

    // ------------------------------------------------------------ dispatch

    private static bool _lastWasSignature;

    /// <summary>True once after a signature weapon fired; the audio director
    /// asks this before playing the generic shape sound.</summary>
    public static bool ConsumeFired()
    {
        bool was = _lastWasSignature;
        _lastWasSignature = false;
        return was;
    }

    internal static bool TryFire(WeaponRuntime w, WeaponSystem sys, StatSheet s)
    {
        _lastWasSignature = false;
        switch (w.Def.Id)
        {
            case Spyglass: FireSpyglass(w, sys, s); break;
            case Anchor: FireAnchor(w, sys, s); break;
            case Kite: FireKite(w, sys, s); break;
            default: return false;
        }
        _lastWasSignature = true;
        return true;
    }

    internal static void Cue(SignatureSound sound, Vector3 at) => Core.EventBus.Publish(new SignatureCue(sound, at));

    // ------------------------------------------------------------- shared

    private static readonly List<IDamageable> Buffer = new(64);
    private static readonly List<(IDamageable T, float Along)> Line = new(32);
    private static readonly Core.Rng FxRng = new(0x51C7_A7E5UL);

    private static int Shots(WeaponRuntime w, StatSheet s)
        => Math.Max(1, w.Def.Projectiles + w.Bonus.Projectiles + (int)s.Get(Stat.ProjectileCount));

    private static float Area(WeaponRuntime w, StatSheet s) => s.Get(Stat.AreaSize) * (1f + w.Bonus.Area);
    private static float Reach(WeaponRuntime w) => w.Def.Range * (1f + w.Bonus.Range);
    private static float Duration(StatSheet s) => MathF.Max(0.25f, s.Get(Stat.Duration));
    private static Vector3 Ground(Vector3 muzzle) => muzzle - Vector3.Up * FxGuard.MuzzleHeight;

    /// <summary>Apply a hit and report it, the same way every other weapon does
    /// (co-op forwards it from inside TakeDamage).</summary>
    internal static DamageInfo Strike(WeaponSystem sys, IDamageable t, DamageInfo template, Vector3 from, float scale = 1f)
    {
        DamageInfo info = template.Clone();
        info.Amount *= scale;
        info.Direction = Projectile.FlatDirection(t.Position - from, Vector3.Forward);
        t.TakeDamage(info);
        sys.ReportHit(t, info);
        Count(info);
        return info;
    }

    /// <summary>Signature hits that landed this run (the co-op test reads it).</summary>
    public static int Hits { get; private set; }

    internal static void ResetHits() => Hits = 0;

    private static void Count(DamageInfo info)
    {
        if (!info.Nullified && info.Amount > 0f) Hits++;
    }

    private static void ForceCrit(DamageInfo info, StatSheet s)
    {
        if (info.Crit) return;
        info.Crit = true;
        info.Amount *= 1f + s.Get(Stat.CritDamage);
    }

    /// <summary>Nearest live target from this frame's target set (which the
    /// audit's dummies replace), unlike <see cref="Targeting.Nearest"/>.</summary>
    private static IDamageable? NearestLive(Vector3 from, float range)
    {
        IReadOnlyList<IDamageable> live = Targeting.Live;
        IDamageable? best = null;
        float bestD = range;
        for (int i = 0; i < live.Count; i++)
        {
            IDamageable t = live[i];
            if (!t.IsAlive) continue;
            float d = FlatDistance(t.Position, from);
            if (d <= bestD) { bestD = d; best = t; }
        }
        return best;
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.Y = 0f; b.Y = 0f;
        return a.DistanceTo(b);
    }

    // ========================================================= SPYGLASS GLINT
    //
    // The precise one. A thin instant beam along the line to one target. The
    // target is whatever you are looking at: the enemy closest to the centre of
    // the camera's view inside a narrow sight cone (elites and bosses first).
    // A sighted shot is a BULLSEYE: a guaranteed crit on the first body. The
    // first body also takes more the farther away it is, and each body the beam
    // passes through makes it hit the next one harder. With nothing in the
    // sight the beam still fires, at the nearest enemy, without the bullseye.

    private const float SightDegrees = 13f;
    private const float SightDegreesSteady = 19f;
    private const float LongShotPerMetre = 0.025f;   // past 4 m, up to +60%
    private const float LongShotMax = 0.60f;
    private const float Collateral = 0.15f;          // per body already pierced
    private const float RicochetRange = 12f;
    private const float RicochetScale = 0.7f;
    private static readonly Color GlintGold = new(1.00f, 0.88f, 0.45f);

    private static void FireSpyglass(WeaponRuntime w, WeaponSystem sys, StatSheet s)
    {
        WeaponDef d = w.Def;
        Vector3 origin = sys.MuzzlePosition;
        float range = Reach(w);
        int shots = Shots(w, s);
        bool steady = w.Level >= MilestoneLevel;
        bool sunGlint = w.Level >= d.MaxLevel;
        int pierce = d.Pierce + w.Bonus.Pierce + (steady ? 1 : 0);
        float width = MathF.Max(0.25f, d.Radius * MathF.Min(Area(w, s), 2f));
        float sight = MathF.Cos(Mathf.DegToRad(steady ? SightDegreesSteady : SightDegrees));
        Vector3 look = Projectile.FlatDirection(sys.AimDirection, Vector3.Forward);

        // Candidates: sighted ones first (elites/bosses, then nearest the
        // centre of the view), then the rest by distance for the fallback.
        IReadOnlyList<IDamageable> live = Targeting.Live;
        var picks = new List<(IDamageable T, bool Sighted)>(shots);
        for (int k = 0; k < shots; k++)
        {
            IDamageable? best = null;
            float bestScore = float.MinValue;
            bool bestSighted = false;
            for (int i = 0; i < live.Count; i++)
            {
                IDamageable t = live[i];
                if (!t.IsAlive || picks.Exists(p => ReferenceEquals(p.T, t))) continue;
                Vector3 to = t.Position - origin;
                to.Y = 0f;
                float dist = to.Length();
                if (dist > range || dist < 0.05f) continue;
                float cos = to.Dot(look) / dist;
                bool sighted = cos >= sight;
                float score = sighted
                    ? 1000f + (t is Enemy { IsBoss: true } or Enemy { IsElite: true } ? 500f : 0f) + cos * 100f
                    : -dist;
                if (score > bestScore) { bestScore = score; best = t; bestSighted = sighted; }
            }
            if (best is null) break;
            picks.Add((best, bestSighted));
        }

        if (picks.Count == 0)
        {
            // Nothing in range: a glint down the line of sight, so the weapon
            // never looks idle between waves.
            Projectile.SpawnStreak(origin, origin + look * range * 0.6f, 0.08f, d.Tint, 0.1f);
            Cue(SignatureSound.SpyglassCast, origin);
            sys.NoteShots(1);
            return;
        }

        bool anyBullseye = false;
        foreach (var (target, sighted) in picks)
        {
            Vector3 dir = Projectile.FlatDirection(target.Position - origin, look);
            DamageInfo template = sys.BuildDamage(w);
            Beam(sys, origin, dir, range, width, pierce, template, s, sighted, out Vector3 end,
                            out IDamageable? first, out bool killedFirst);
            Projectile.SpawnStreak(origin, end, 0.11f, d.Tint, 0.13f);
            if (first is not null)
            {
                Vector3 at = first.Position + Vector3.Up * 0.7f;
                SignatureFx.Add(new SignatureFx.Flash(at, sighted ? 0.75f : 0.4f, sighted ? GlintGold : d.Tint, sighted ? 0.32f : 0.2f));
                if (sighted) anyBullseye = true;
                if (sighted && killedFirst && sunGlint) Ricochet(sys, w, s, first, template);
            }
        }

        Cue(anyBullseye ? SignatureSound.SpyglassBullseye : SignatureSound.SpyglassCast, origin);
        sys.NoteShots(picks.Count);
    }

    /// <summary>Hit up to 1 + <paramref name="pierce"/> bodies along the line,
    /// nearest first. Returns how many were hit.</summary>
    private static int Beam(WeaponSystem sys, Vector3 origin, Vector3 dir, float range, float width, int pierce,
                            DamageInfo template, StatSheet s, bool bullseye, out Vector3 end,
                            out IDamageable? first, out bool killedFirst)
    {
        Line.Clear();
        IReadOnlyList<IDamageable> live = Targeting.Live;
        float reach = width + Targeting.AssumedTargetRadius;
        for (int i = 0; i < live.Count; i++)
        {
            IDamageable t = live[i];
            if (!t.IsAlive) continue;
            Vector3 rel = t.Position - origin;
            rel.Y = 0f;
            float along = rel.Dot(dir);
            if (along < -0.3f || along > range) continue;
            if ((rel - dir * along).Length() > reach) continue;
            Line.Add((t, along));
        }
        Line.Sort((a, b) => a.Along.CompareTo(b.Along));

        int max = Math.Min(Line.Count, 1 + Math.Max(0, pierce));
        first = null;
        killedFirst = false;
        end = origin + dir * range;
        int landed = 0;
        for (int i = 0; i < max; i++)
        {
            IDamageable t = Line[i].T;
            DamageInfo info = template.Clone();
            if (i == 0)
            {
                float longShot = Mathf.Clamp((Line[i].Along - 4f) * LongShotPerMetre, 0f, LongShotMax);
                info.Amount *= 1f + longShot;
                if (bullseye) ForceCrit(info, s);
            }
            else info.Amount *= 1f + Collateral * landed;

            info.Direction = dir;
            t.TakeDamage(info);
            sys.ReportHit(t, info);
            Count(info);
            if (i == 0) { first = t; killedFirst = !t.IsAlive; }
            if (!info.Nullified) landed++;
        }
        // Stop the drawn beam at the last body when the pierce ran out.
        if (max > 0 && max < Line.Count) end = origin + dir * (Line[max - 1].Along + 0.4f);
        return landed;
    }

    /// <summary>Sun Glint (max level): a bullseye kill reflects the beam into
    /// the farthest enemy within 12 m of the body.</summary>
    private static void Ricochet(WeaponSystem sys, WeaponRuntime w, StatSheet s, IDamageable from, DamageInfo template)
    {
        Vector3 at = from.Position;
        IReadOnlyList<IDamageable> live = Targeting.Live;
        IDamageable? far = null;
        float farD = 0f;
        for (int i = 0; i < live.Count; i++)
        {
            IDamageable t = live[i];
            if (!t.IsAlive || ReferenceEquals(t, from)) continue;
            float dd = FlatDistance(t.Position, at);
            if (dd <= RicochetRange && dd > farD) { farD = dd; far = t; }
        }
        if (far is null) return;
        DamageInfo info = template.Clone();
        ForceCrit(info, s);
        Strike(sys, far, info, at, RicochetScale);
        Vector3 a = at + Vector3.Up * 0.7f, b = far.Position + Vector3.Up * 0.7f;
        Projectile.SpawnStreak(a, b, 0.09f, GlintGold, 0.16f);
        SignatureFx.Add(new SignatureFx.Flash(b, 0.5f, GlintGold, 0.25f));
        Cue(SignatureSound.SpyglassRicochet, b);
    }

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

    // =========================================================== SKIMMER KITE
    //
    // The movement one. A kite on a long string that skims low behind you.
    // You do not aim it: you steer it, by running. The string cuts whatever it
    // sweeps through and the kite itself hits harder, and both hit harder the
    // faster the kite is moving, so turns, dashes and circles are where it
    // earns its keep. Standing still, it drifts round you in slow loops.

    private static readonly Dictionary<WeaponRuntime, KiteRig> Rigs = new(ReferenceEqualityComparer.Instance);

    internal static void ForgetRigs() => Rigs.Clear();

    private static void FireKite(WeaponRuntime w, WeaponSystem sys, StatSheet s)
    {
        if (!Rigs.TryGetValue(w, out KiteRig? rig) || rig.Dead)
        {
            rig = new KiteRig(sys, w);
            Rigs[w] = rig;
            SignatureFx.Add(rig);
        }
        rig.Configure(s);
        rig.Cut(s);
        sys.NoteShots(1);
    }

    private sealed class KiteRig : SignatureFx.IFx
    {
        private const int MaxKites = 3;
        private const float ZapEvery = 2f;
        private const float IdleTurn = 1.1f;   // rad/s: an idle loop is slower than a running sweep
        private const float ZapRange = 7f;
        private const float ZapScale = 4f;
        private const float HeadRadius = 1.0f;
        private const float HeadScale = 2f;
        private static readonly Color StringColor = new(0.95f, 0.96f, 1.00f);
        private static readonly Color ZapColor = new(0.85f, 0.88f, 1.00f);

        private readonly WeaponSystem _sys;
        private readonly WeaponRuntime _w;
        private readonly List<Vector3> _pos = new(), _vel = new();
        private readonly List<MeshInstance3D?> _kites = new(), _strings = new();
        private float _length = 5f, _width = 0.35f, _orbit, _clock, _zap = ZapEvery, _whipCue;
        private int _count;
        private float _sinceCut;
        public bool Dead;

        public KiteRig(WeaponSystem sys, WeaponRuntime w) { _sys = sys; _w = w; }

        private Vector3 Hand => _sys.MuzzlePosition + Vector3.Up * 0.15f;

        public void Configure(StatSheet s)
        {
            WeaponDef d = _w.Def;
            float area = Area(_w, s);
            _length = MathF.Max(2.5f, d.Range * (1f + _w.Bonus.Range) * MathF.Sqrt(MathF.Max(0.3f, area)));
            _width = MathF.Max(0.25f, d.Radius * area);
            int want = Math.Min(MaxKites, Shots(_w, s) + (_w.Level >= MilestoneLevel ? 1 : 0));
            while (_count < want) AddKite();
        }

        private void AddKite()
        {
            int i = _count++;
            // Starts where its idle loop would have it, so a new kite does not
            // whip across the player on its first frame.
            float a0 = _orbit + i * Mathf.Pi;
            Vector3 start = Hand + new Vector3(MathF.Cos(a0), 0f, MathF.Sin(a0)) * (LengthOf(i) * 0.85f) + Vector3.Down * 0.35f;
            _pos.Add(start);
            _vel.Add(Vector3.Zero);
            _kites.Add(SignatureFx.NewMesh(SignatureFx.Kite, SignatureFx.Mat(_w.Def.Tint)));
            MeshInstance3D? str = SignatureFx.NewMesh(CombatVisuals.Slab, SignatureFx.Line(StringColor));
            _strings.Add(str);
        }

        private float LengthOf(int i) => _length * (1f - 0.22f * i);

        public bool Step(float dt)
        {
            if (Dead) return false;
            if (dt <= 0f) return true;
            // A kite whose weapon stopped firing (run over, audit dummy, a
            // silenced or downed player) is reeled in rather than left flying.
            _sinceCut += dt;
            if (_sinceCut > MathF.Max(2f, _w.Interval * 4f)) return false;
            _clock += dt;
            _orbit += dt * IdleTurn;
            _whipCue -= dt;
            Vector3 hand = Hand;
            Vector3 v = _sys.Body is Player p && GodotObject.IsInstanceValid(p) ? p.PlanarVelocity : Vector3.Zero;
            v.Y = 0f;
            float speed = v.Length();

            for (int i = 0; i < _count; i++)
            {
                float len = LengthOf(i);
                float phase = i * Mathf.Tau / Math.Max(1, _count);
                Vector3 want;
                if (speed > 1.2f)
                {
                    // Trail behind and off to one side, swaying, skimming low:
                    // under the camera's line to the player, never on it.
                    Vector3 back = -v / speed;
                    float sway = 0.55f + 0.35f * MathF.Sin(_clock * 1.7f + phase);
                    Vector3 dir = back.Rotated(Vector3.Up, (i % 2 == 0 ? 1f : -1f) * sway);
                    want = hand + dir * (len * 0.92f);
                }
                else
                {
                    float a = _orbit + phase;
                    want = hand + new Vector3(MathF.Cos(a), 0f, MathF.Sin(a)) * (len * 0.85f);
                }
                want.Y = hand.Y - 0.35f + 0.25f * MathF.Sin(_clock * 2.3f + phase);

                Vector3 prev = _pos[i];
                Vector3 next = prev.Lerp(want, 1f - MathF.Exp(-dt * 4.5f));
                Vector3 off = next - hand;
                if (off.Length() > len) next = hand + off.Normalized() * len;
                _vel[i] = _vel[i].Lerp((next - prev) / dt, 1f - MathF.Exp(-dt * 10f));
                _pos[i] = next;
                Draw(i, hand);
            }

            if (_w.Level >= _w.Def.MaxLevel)
            {
                _zap -= dt;
                if (_zap <= 0f) { _zap = ZapEvery; Zap(); }
            }
            return true;
        }

        private void Draw(int i, Vector3 hand)
        {
            Vector3 k = _pos[i];
            if (_kites[i] is { } kite && GodotObject.IsInstanceValid(kite))
            {
                Vector3 face = Projectile.FlatDirection(hand - k, Vector3.Forward);
                float tilt = Mathf.Clamp(_vel[i].Length() * 0.05f, 0f, 0.5f);
                Basis b = Basis.LookingAt(-face, Vector3.Up).Rotated(face.Cross(Vector3.Up).Normalized(), -0.35f - tilt);
                kite.GlobalTransform = new Transform3D(b.Scaled(Vector3.One * 1.15f), k);
                kite.Transparency = SignatureFx.Fade(0f);
            }
            if (_strings[i] is { } str && GodotObject.IsInstanceValid(str))
            {
                SignatureFx.SetBar(str, hand, k, 0.03f);
                str.Transparency = SignatureFx.Fade(0.35f);
            }
        }

        /// <summary>The weapon's tick: every string and kite cuts what it touches.</summary>
        public void Cut(StatSheet s)
        {
            _sinceCut = 0f;
            Vector3 hand = Hand;
            DamageInfo template = _sys.BuildDamage(_w);
            IReadOnlyList<IDamageable> live = Targeting.Live;
            float reach = _width + Targeting.AssumedTargetRadius;
            bool any = false;
            float fastest = 0f;
            for (int i = 0; i < _count; i++)
            {
                Vector3 a = hand, b = _pos[i];
                a.Y = 0f; b.Y = 0f;
                Vector3 ab = b - a;
                float len = ab.Length();
                if (len < 0.1f) continue;
                Vector3 dir = ab / len;
                float kiteSpeed = _vel[i].Length();
                fastest = MathF.Max(fastest, kiteSpeed);
                float whip = Mathf.Clamp(0.7f + kiteSpeed / 9f, 0.7f, 2.2f);
                Targeting.AreaProbe?.Invoke(_pos[i], HeadRadius, Vector3.Zero, -1f);   // pots in the kite's way
                for (int j = 0; j < live.Count; j++)
                {
                    IDamageable t = live[j];
                    if (!t.IsAlive) continue;
                    Vector3 rel = t.Position - a;
                    rel.Y = 0f;
                    float along = rel.Dot(dir);
                    if (along < 0f || along > len + reach) continue;
                    float side = (rel - dir * Mathf.Min(along, len)).Length();
                    Vector3 hk = t.Position - _pos[i];
                    hk.Y = 0f;
                    bool head = hk.Length() <= HeadRadius + Targeting.AssumedTargetRadius;
                    if (!head && side > reach) continue;
                    DamageInfo info = template.Clone();
                    info.Amount *= whip * (head ? HeadScale : 1f);
                    info.Direction = Projectile.FlatDirection(_vel[i], Projectile.FlatDirection(rel, Vector3.Forward));
                    t.TakeDamage(info);
                    _sys.ReportHit(t, info);
                    Count(info);
                    any = true;
                }
            }
            if (any && fastest > 6f && _whipCue <= 0f)
            {
                _whipCue = 0.45f;
                Cue(SignatureSound.KiteWhip, _count > 0 ? _pos[0] : hand);
            }
        }

        /// <summary>Storm Kite (max level): lightning from each kite onto the
        /// nearest enemy below it.</summary>
        private void Zap()
        {
            DamageInfo template = _sys.BuildDamage(_w);
            template.Element = Element.Lightning;
            bool any = false;
            for (int i = 0; i < _count; i++)
            {
                IDamageable? t = NearestLive(_pos[i], ZapRange);
                if (t is null) continue;
                Strike(_sys, t, template, _pos[i], ZapScale);
                Vector3 top = _pos[i], bottom = t.Position + Vector3.Up * 0.5f;
                Vector3 mid = (top + bottom) * 0.5f + new Vector3(FxRng.Range(-0.5f, 0.5f), 0.2f, FxRng.Range(-0.5f, 0.5f));
                SignatureFx.Add(new SignatureFx.Wire(top, mid, 0.06f, ZapColor, 0.14f));
                SignatureFx.Add(new SignatureFx.Wire(mid, bottom, 0.06f, ZapColor, 0.14f));
                SignatureFx.Add(new SignatureFx.Flash(bottom, 0.45f, ZapColor, 0.18f));
                any = true;
            }
            if (any) Cue(SignatureSound.KiteZap, _count > 0 ? _pos[0] : Hand);
        }

        public void Free()
        {
            Dead = true;
            foreach (MeshInstance3D? k in _kites) SignatureFx.FreeNode(k);
            foreach (MeshInstance3D? str in _strings) SignatureFx.FreeNode(str);
            _kites.Clear();
            _strings.Clear();
            _count = 0;
            _pos.Clear();
            _vel.Clear();
        }
    }
}
