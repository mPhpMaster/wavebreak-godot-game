using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Combat.Weapons;
using Wavebreak.Data;
using Wavebreak.Stats;

namespace Wavebreak.Combat;

/// <summary>
/// The original eighteen weapons used to share five deliveries and five
/// meshes, so every straight shot, every seeker, every shell, every field and
/// every swing looked and behaved like the others of its shape
/// (docs/weapon-audit.md). This is where each one gets its own twist on top of
/// the shape switch; the numbers still come from the def, the level and the
/// stat sheet, and a weapon without a row here fires exactly as before.
/// </summary>
public sealed partial class WeaponRuntime
{
    private static readonly List<IDamageable> LookBuffer = new(64);

    private Look _look;
    private bool _lookRead;
    private int _thrustSide = 1;

    private Look CurrentLook
    {
        get
        {
            if (!_lookRead) { _look = WeaponLooks.For(this); _lookRead = true; }
            return _look;
        }
    }

    /// <summary>Forget the cached row (an evolution changes it).</summary>
    private void RereadLook() => _lookRead = false;

    // ------------------------------------------------------------- shots

    /// <summary>The projectile shapes with a twist in how the volley leaves.
    /// False leaves the volley to the plain loop.</summary>
    private bool FireStyled(WeaponSystem system, ProjectileSpec spec, Vector3 heading, int count)
    {
        Look look = CurrentLook;
        if (look.Style == ShotStyle.None) return false;
        Vector3 origin = spec.Origin;
        float range = spec.Range;

        switch (look.Style)
        {
            case ShotStyle.Tracer:
                // Searing Longshot: a sniper picks the toughest body in reach, not the nearest.
                if (Toughest(origin, range) is { } mark)
                {
                    Vector3 to = Projectile.FlatDirection(mark.Position - origin, heading);
                    DistinctStats.Note("longshot_toughest");
                    heading = to;
                }
                break;

            case ShotStyle.Needle:
                // Glasswire Needles: a stream, one needle after another, each re-aimed.
                for (int i = 0; i < count; i++)
                {
                    int index = i;
                    ProjectileSpec s = spec;
                    WeaponLooks.Dress(ref s, look, index);
                    DamageInfo hit = system.BuildDamage(this);
                    DistinctFx.After(i * 0.05f, () =>
                    {
                        s.Origin = system.MuzzlePosition;
                        Vector3 aim = system.AimAt(s.Origin, range);
                        s.Direction = aim.Rotated(Vector3.Up, Mathf.DegToRad(SpreadOffset(index, count) * 0.4f));
                        Projectile.Spawn(system, s, hit);
                    });
                }
                if (count > 1) DistinctStats.Note("needle_stream");
                return true;

            case ShotStyle.Lob:
            case ShotStyle.Hail:
            {
                // Mortar: one high lob onto the body it is aimed at. Hailstones: a
                // low carpet laid in front, near to far, wherever the bodies are.
                IDamageable? at = Targeting.Nearest(origin, range);
                Vector3 ground = Weapons.WeaponBehaviors.Ground(origin);
                // Hailstones land in a row from just short of the nearest body to
                // just past it, so the carpet is laid across where they are.
                float near = range * 0.6f;
                if (at is not null)
                {
                    Vector3 off = at.Position - origin;
                    off.Y = 0f;
                    near = Mathf.Clamp(off.Length(), 1.5f, range);
                }
                for (int i = 0; i < count; i++)
                {
                    ProjectileSpec s = spec;
                    WeaponLooks.Dress(ref s, look, i);
                    Vector3 dir = heading.Rotated(Vector3.Up, Mathf.DegToRad(SpreadOffset(i, count)));
                    Vector3 land;
                    if (look.Style == ShotStyle.Lob)
                        land = at is not null && i == 0 ? at.Position : origin + dir * (range * 0.7f);
                    else
                        land = origin + dir * (near * (0.7f + 0.6f * (count <= 1 ? 0.5f : i / (float)(count - 1))));
                    s.Target = new Vector3(land.X, ground.Y + 0.3f, land.Z);
                    s.Direction = dir;
                    DamageInfo hit = system.BuildDamage(this);
                    // Laid where the bodies are, every stone lands near one: softer each.
                    if (look.Style == ShotStyle.Hail) hit.Amount *= 0.8f;
                    Projectile.Spawn(system, s, hit);
                }
                return true;
            }

            case ShotStyle.Pod:
            {
                // The pod's own burst is softer; its seedlings make up the rest.
                for (int i = 0; i < count; i++)
                {
                    ProjectileSpec s = spec;
                    WeaponLooks.Dress(ref s, look, i);
                    s.Direction = heading.Rotated(Vector3.Up, Mathf.DegToRad(SpreadOffset(i, count)));
                    DamageInfo hit = system.BuildDamage(this);
                    hit.Amount *= 0.8f;
                    Projectile.Spawn(system, s, hit);
                }
                return true;
            }
        }

        for (int i = 0; i < count; i++)
        {
            ProjectileSpec s = spec;
            WeaponLooks.Dress(ref s, look, i);
            s.Direction = heading.Rotated(Vector3.Up, Mathf.DegToRad(SpreadOffset(i, count)));
            Projectile.Spawn(system, s, system.BuildDamage(this));
        }
        return true;
    }

    /// <summary>The body in reach with the most health, elites and bosses first.</summary>
    private static IDamageable? Toughest(Vector3 origin, float range)
    {
        int n = Targeting.Overlap(origin, range, LookBuffer);
        IDamageable? best = null;
        float bestScore = float.MinValue;
        for (int i = 0; i < n; i++)
        {
            IDamageable t = LookBuffer[i];
            if (!t.IsAlive) continue;
            float score = t is Actors.Enemy e ? e.Health + (e.IsBoss ? 1e6f : e.IsElite ? 1e5f : 0f) : 0f;
            if (score > bestScore) { bestScore = score; best = t; }
        }
        return best;
    }

    // ------------------------------------------------------------- swings

    /// <summary>Melee weapons whose hit shape is not the cone. True when handled.</summary>
    private bool FireMeleeStyled(WeaponSystem system, Vector3 origin, Vector3 heading, float reach, float halfArc)
    {
        switch (Def.Id)
        {
            case "riptide_blades":
            {
                // A lunge: everything on a narrow line, a little longer than the
                // old cone, alternating blades left and right.
                float length = reach * 1.25f;
                float width = 0.65f;
                bool undertow = Echoing && Evolution is { Id: "evo_undertow_blades" };
                _thrustSide = -_thrustSide;
                float side = (undertow ? -_thrustSide : _thrustSide) * 0.25f;
                DamageInfo template = system.BuildDamage(this);
                int n = Targeting.Overlap(origin + heading * (length * 0.5f), length * 0.5f + width, LookBuffer);
                int hits = 0;
                for (int i = 0; i < n; i++)
                {
                    IDamageable t = LookBuffer[i];
                    Vector3 rel = t.Position - origin;
                    rel.Y = 0f;
                    float along = rel.Dot(heading);
                    if (along < -0.3f || along > length) continue;
                    if ((rel - heading * along).Length() > width + Targeting.AssumedTargetRadius) continue;
                    DamageInfo info = template.Clone();
                    // Undertow's echo drags the line back toward you.
                    info.Direction = undertow ? Projectile.FlatDirection(origin - t.Position, -heading) : heading;
                    if (undertow) info.Knockback = MathF.Max(info.Knockback, 2.5f);
                    t.TakeDamage(info);
                    system.ReportHit(t, info);
                    hits++;
                }
                DistinctStats.Note(undertow ? "undertow_drag" : "riptide_thrust");
                DistinctFx.Thrust(origin, heading, length, side, undertow ? Def.Tint.Darkened(0.35f) : Def.Tint);
                system.NoteShots(1);
                return true;
            }
            case "forgefall_maul":
            {
                // An overhead slam: a circle ahead of you about the size of the
                // old arc, with a short stun in the middle.
                float r = reach * 0.33f;
                Vector3 centre = origin + heading * (reach * 0.62f);
                DamageInfo template = system.BuildDamage(this);
                int n = Targeting.Overlap(centre, r, LookBuffer);
                for (int i = 0; i < n; i++)
                {
                    IDamageable t = LookBuffer[i];
                    DamageInfo info = template.Clone();
                    info.Direction = Projectile.FlatDirection(t.Position - centre, heading);
                    t.TakeDamage(info);
                    system.ReportHit(t, info);
                    Vector3 off = t.Position - centre;
                    off.Y = 0f;
                    if (off.Length() < r * 0.4f && t is Actors.Enemy e && e.IsAlive) { e.ApplyStun(0.35f); DistinctStats.Note("maul_stun"); }
                }
                DistinctStats.Note("maul_slam");
                DistinctFx.Slam(Weapons.WeaponBehaviors.Ground(centre), r, Def.Tint);
                system.NoteShots(1);
                return true;
            }
        }
        return false;
    }

    /// <summary>What a cone swing looks like, per weapon.</summary>
    private void DrawSwing(Vector3 origin, Vector3 heading, float reach, float halfArc)
    {
        if (Def.Id == "cleaver_arc")
        {
            DistinctFx.Sweep(origin, heading, reach, halfArc, Def.Tint, 1f, 0.2f);
            DistinctStats.Note("cleaver_sweep");
            return;
        }
        Projectile.SpawnSwipe(origin, heading, reach, halfArc, Def.Tint);
    }

    // ------------------------------------------------------------- fields

    private readonly Dictionary<IDamageable, int> _emberStacks = new();
    private int _emberRevision;

    /// <summary>Per-target shaping of an aura tick, before it lands.</summary>
    private void ShapeAuraHit(DamageInfo info, IDamageable target, Vector3 centre)
    {
        switch (Def.Id)
        {
            case "ember_halo":
            {
                // Standing in the halo is the mistake: a body that just walked in
                // takes 60%, +10% for every tick it stays, up to 110%.
                int stacks = Math.Min(5, _emberStacks.GetValueOrDefault(target));
                info.Amount *= 0.6f + 0.1f * stacks;
                _emberStacks[target] = stacks + 1;
                if (stacks > 0) DistinctStats.Note("ember_ramp");
                break;
            }
            case "rimeheart":
            {
                // It would rather they stayed: a gentle pull toward you.
                Vector3 inward = Projectile.FlatDirection(centre - target.Position, Vector3.Zero);
                if (inward.LengthSquared() > 1e-4f)
                {
                    info.Direction = inward;
                    info.Knockback = 1.1f;
                    DistinctStats.Note("rime_pull");
                }
                break;
            }
        }
    }

    /// <summary>After an aura tick: visuals per field, and housekeeping.</summary>
    private void AfterAuraTick(WeaponSystem system, Vector3 centre, float radius, int hitCount)
    {
        switch (Def.Id)
        {
            case "ember_halo":
                // Bodies that left the halo lose their stacks (checked now and then).
                if (++_emberRevision % 8 == 0)
                {
                    var gone = new List<IDamageable>();
                    foreach (IDamageable t in _emberStacks.Keys)
                    {
                        Vector3 off = t.Position - centre;
                        off.Y = 0f;
                        if (!t.IsAlive || off.Length() > radius + 1f) gone.Add(t);
                    }
                    foreach (IDamageable t in gone) _emberStacks.Remove(t);
                }
                break;
            case "static_field":
                if (hitCount > 0)
                {
                    DistinctFx.Pulse(centre, radius, Def.Tint);
                    // Arcs to the first few bodies it shoved, so the pulse reads as a discharge.
                    for (int i = 0; i < Math.Min(3, hitCount) && i < Targets.Count; i++)
                        DistinctFx.Jagged(centre, Targets[i].Position + Vector3.Up * 0.5f, Def.Tint, 0.05f, 0.14f, 3);
                    DistinctStats.Note("static_pulse");
                }
                break;
        }
    }

    // Ornaments on the persistent field, one per aura weapon.
    private MeshInstance3D? _ornament;

    private void EnsureOrnament(float shown)
    {
        if (_auraRoot is null || !GodotObject.IsInstanceValid(_auraRoot)) return;
        Mesh? mesh = Def.Id switch
        {
            "ember_halo" => LookMeshes.FlameRing,
            "rimeheart" => LookMeshes.Snowflake,
            "static_field" => LookMeshes.SparkRing,
            _ => null,
        };
        if (mesh is null) return;
        if (_ornament is null || !GodotObject.IsInstanceValid(_ornament))
        {
            _ornament = new MeshInstance3D
            {
                Name = "Ornament",
                Mesh = mesh,
                MaterialOverride = ExpansionVisuals.Glow(Def.Tint),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            _auraRoot.AddChild(_ornament);
        }
        // Rimeheart's snowflake fills its small ring; the halo's flames and the
        // field's crackle stand on the edge.
        _ornament.Scale = new Vector3(shown, Def.Id == "ember_halo" ? 1f : 1f, shown);
    }

    private void AnimateOrnament(float dt)
    {
        if (_ornament is null || !GodotObject.IsInstanceValid(_ornament)) return;
        float spin = Def.Id switch { "ember_halo" => 0.9f, "rimeheart" => -0.25f, _ => 2.5f };
        _ornament.RotateY(spin * dt);
        float own = Def.Id switch
        {
            // The field's crackle only shows on a pulse.
            "static_field" => 1f - 0.9f * _pulse,
            "ember_halo" => 0.25f + 0.1f * MathF.Sin((float)Time.GetTicksMsec() * 0.012f),
            _ => 0.35f,
        };
        _ornament.Transparency = Meta.GraphicsSettings.FxFade(own);
    }
}
