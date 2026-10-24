using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Data;
using Wavebreak.Stats;

namespace Wavebreak.Combat;

/// <summary>
/// One owned weapon: a <see cref="WeaponDef"/>, the level it has been upgraded
/// to, and the timer that decides when it goes off.
///
/// The firing code for all five <see cref="AttackShape"/> values lives here
/// rather than in a subclass per shape. Weapons are data — a new one is a .tres
/// file, not a class — so the switch is the point: it is the one place that
/// knows how a shape is delivered, and it stays small because everything
/// *numeric* about a weapon comes from the def and the stat sheet.
///
/// Nothing here reads a stat more than once per shot. The sheet caches, but a
/// weapon that fires twelve projectiles should not ask twelve times what the
/// area size is.
/// </summary>
public sealed class WeaponRuntime
{
    /// <summary>Cap on shots resolved in a single tick. A frame spike must not
    /// turn into a stored volley that all lands at once; the excess is dropped.</summary>
    private const int MaxShotsPerTick = 4;

    /// <summary>Floor on a melee arc. A melee weapon inherits SpreadDegrees from
    /// the same field projectiles use for scatter, and a 12-degree sword would
    /// miss anything not standing exactly on the crosshair.</summary>
    private const float MinMeleeArcDegrees = 60f;

    /// <summary>Extra arc, in degrees, per bonus projectile on a melee weapon.
    /// Projectile count has to mean *something* for a shape with no projectiles,
    /// and a wider swing is the honest translation.</summary>
    private const float MeleeArcPerExtraShot = 14f;

    // Shared across weapons: only one weapon resolves targets at a time, and the
    // list is fully consumed before the next call.
    private static readonly List<IDamageable> Targets = new(64);

    private float _cooldown;
    private float _interval = 1f;
    private float _pulse;

    private Node3D? _auraRoot;
    private MeshInstance3D? _auraDisc;
    private OmniLight3D? _auraLight;

    internal WeaponRuntime(WeaponDef def)
    {
        Def = def;

        // Zero, so a weapon fires the instant it is picked up. Making a new
        // weapon wait out a cooldown reads as the pickup not having worked.
        _cooldown = 0f;
    }

    /// <summary>What this weapon is.</summary>
    public WeaponDef Def { get; }

    /// <summary>1-based, capped at <see cref="WeaponDef.MaxLevel"/>.</summary>
    public int Level { get; private set; } = 1;

    /// <summary>True when there is still an upgrade to offer.</summary>
    public bool CanLevel => Level < Def.MaxLevel;

    /// <summary>Seconds between shots at the current level and attack speed.</summary>
    public float Interval => _interval;

    /// <summary>0 just after firing, 1 when ready. For cooldown pips on the HUD.</summary>
    public float Charge => _interval <= 0f ? 1f : Mathf.Clamp(1f - _cooldown / _interval, 0f, 1f);

    /// <summary>Total damage this weapon puts into the air per shot volley, before
    /// crit and before target count. The honest number for a tooltip.</summary>
    public float VolleyDamage(StatSheet stats)
        => BaseDamage * stats.GetMultiplier(Stat.Damage) * ShotCount(stats);

    /// <summary>Everything the rolled upgrades have added. Level 1 is all zero,
    /// so level 1 is exactly the def's numbers.</summary>
    public WeaponBonus Bonus { get; } = new();

    /// <summary>The upgrades taken, in order (for UI and logs).</summary>
    public List<WeaponUpgrade> Upgrades { get; } = new();

    /// <summary>Damage per hit before the player's stats.</summary>
    public float BaseDamage => Def.Damage * (1f + Bonus.Damage);

    /// <summary>Cooldown before the player's attack speed.</summary>
    public float BaseCooldown => MathF.Max(0.05f, Def.Cooldown / (1f + Bonus.AttackSpeed));

    /// <summary>Shots per volley before the player's bonus.</summary>
    public int BaseProjectiles => Def.Projectiles + Bonus.Projectiles;

    /// <summary>Level up with a rolled upgrade. Null applies the deterministic
    /// reference line (a common damage roll).</summary>
    internal bool LevelUp(WeaponUpgrade? upgrade = null)
    {
        if (!CanLevel) return false;
        Level++;
        upgrade ??= WeaponUpgrades.Reference(Def);
        Bonus.Add(upgrade);
        Upgrades.Add(upgrade);
        return true;
    }

    // ------------------------------------------------------------------ tick

    internal void Tick(float dt, WeaponSystem system)
    {
        StatSheet stats = system.Stats;
        _interval = stats.AttackInterval(BaseCooldown);
        _cooldown -= dt;

        int fired = 0;
        while (_cooldown <= 0f && fired < MaxShotsPerTick)
        {
            Fire(system, stats);
            _cooldown += _interval;
            fired++;
        }

        // Whatever debt is left after the cap is forgiven rather than banked.
        if (_cooldown < 0f) _cooldown = 0f;

        if (Def.Shape == AttackShape.Aura) AnimateAura(dt);
    }

    private void Fire(WeaponSystem system, StatSheet stats)
    {
        // Expansion weapons (Combat/Weapons) deliver themselves; see WeaponBehaviors.
        if (Weapons.WeaponBehaviors.TryFire(this, system, stats)) { }
        else switch (Def.Shape)
        {
            case AttackShape.Melee:
                FireMelee(system, stats);
                break;
            case AttackShape.Aura:
                FireAura(system, stats);
                break;
            default:
                FireProjectiles(system, stats);
                break;
        }

        // Announced after the shot resolves, so a listener sees the same muzzle
        // position the shot used. Combat holds no reference to audio.
        Core.EventBus.Publish(new Core.WeaponFired(system.MuzzlePosition, Def.Shape));
    }

    /// <summary>Shots per volley: the weapon's own count plus the player's bonus,
    /// never below one.</summary>
    private int ShotCount(StatSheet stats)
        => Math.Max(1, BaseProjectiles + (int)stats.Get(Stat.ProjectileCount));

    // ----------------------------------------------------------- projectiles

    private void FireProjectiles(WeaponSystem system, StatSheet stats)
    {
        int count = ShotCount(stats);
        float area = stats.Get(Stat.AreaSize) * (1f + Bonus.Area);
        float speed = MathF.Max(0.5f, Def.ProjectileSpeed * (1f + Bonus.ProjectileSpeed) * stats.Get(Stat.ProjectileSpeed));

        Vector3 origin = system.MuzzlePosition;
        Vector3 heading = system.AimAt(origin, Def.Range * (1f + Bonus.Range));

        bool explosive = Def.Shape == AttackShape.Explosive;

        ProjectileSpec spec = new()
        {
            Mode = explosive ? ProjectileMode.Explosive
                 : Def.Shape == AttackShape.Homing ? ProjectileMode.Homing
                 : ProjectileMode.Straight,
            Origin = origin,
            Speed = speed,
            Range = Def.Range * (1f + Bonus.Range),
            // A shell needs a small body and a large blast; everything else uses
            // the def's radius as the width of the shot itself.
            HitRadius = explosive ? 0.45f : MathF.Max(0.2f, Def.Radius * area),
            BlastRadius = explosive ? MathF.Max(0.5f, Def.Radius * area) : 0f,
            Pierce = Def.Pierce + Bonus.Pierce,
            Tint = Def.Tint,
        };

        for (int i = 0; i < count; i++)
        {
            spec.Direction = heading.Rotated(Vector3.Up, Mathf.DegToRad(SpreadOffset(i, count)));

            // One roll per shot, not per volley: a shotgun where every pellet
            // crits together is a coin flip, not a damage distribution.
            Projectile.Spawn(system, spec, system.BuildDamage(this));
        }

        system.NoteShots(count);
    }

    /// <summary>Where shot <paramref name="index"/> sits in the spread, in degrees
    /// off the aim line. Evenly spaced across the whole cone, centred; a single
    /// shot always goes straight down the middle.</summary>
    private float SpreadOffset(int index, int count)
    {
        if (count <= 1 || Def.SpreadDegrees <= 0f) return 0f;
        return Def.SpreadDegrees * (index / (float)(count - 1) - 0.5f);
    }

    // ------------------------------------------------------------------ melee

    private void FireMelee(WeaponSystem system, StatSheet stats)
    {
        float area = stats.Get(Stat.AreaSize) * (1f + Bonus.Area);
        float reach = MathF.Max(0.6f, Def.Range * (1f + Bonus.Range) * area);

        int extra = ShotCount(stats) - Def.Projectiles;
        float arc = MathF.Max(Def.SpreadDegrees, MinMeleeArcDegrees) + MathF.Max(0, extra) * MeleeArcPerExtraShot;
        float halfArc = MathF.Min(179f, arc) * 0.5f;

        Vector3 origin = system.MuzzlePosition;
        Vector3 heading = system.AimAt(origin, reach);

        // One roll for the swing. A single arc is one attack, and splitting the
        // crit per target would make a wide swing statistically safer than a
        // narrow one for no reason a player could name.
        DamageInfo template = system.BuildDamage(this);

        // Reach is the weapon's own; the target's radius is added by the query.
        int count = Targeting.Cone(origin, heading, reach, halfArc, Targets);
        for (int i = 0; i < count; i++)
        {
            IDamageable target = Targets[i];

            DamageInfo info = template.Clone();
            info.Direction = Projectile.FlatDirection(target.Position - origin, heading);
            target.TakeDamage(info);
            system.ReportHit(target, info);
        }

        Projectile.SpawnSwipe(origin, heading, reach, halfArc, Def.Tint);
        system.NoteShots(1);
    }

    // ------------------------------------------------------------------ aura

    private void FireAura(WeaponSystem system, StatSheet stats)
    {
        float radius = MathF.Max(0.5f, Def.Radius * stats.Get(Stat.AreaSize) * (1f + Bonus.Area));
        Vector3 centre = system.MuzzlePosition;

        EnsureAura(system, radius);

        DamageInfo template = system.BuildDamage(this);

        int count = Targeting.Overlap(centre, radius, Targets);
        for (int i = 0; i < count; i++)
        {
            IDamageable target = Targets[i];

            DamageInfo info = template.Clone();
            // Pushed outward from the player, so a field with knockback clears
            // space around you instead of shoving everything one way.
            info.Direction = Projectile.FlatDirection(target.Position - centre, Vector3.Forward);
            target.TakeDamage(info);
            system.ReportHit(target, info);
        }

        // Kick the pulse so the field visibly throbs on each tick — otherwise a
        // persistent aura gives no feedback that it is doing anything.
        _pulse = 1f;
        system.NoteShots(1);
    }

    private void EnsureAura(WeaponSystem system, float radius)
    {
        Node3D? body = system.Body;
        if (body is null || !GodotObject.IsInstanceValid(body)) return;

        if (_auraRoot is null || !GodotObject.IsInstanceValid(_auraRoot))
        {
            _auraRoot = new Node3D { Name = $"Aura_{Def.Id}", Position = new Vector3(0f, 0.05f, 0f) };

            _auraDisc = new MeshInstance3D
            {
                Name = "Field",
                Mesh = CombatVisuals.Disc,
                // Translucent and additive: the player has to be able to see the
                // enemies standing inside their own aura.
                MaterialOverride = CombatVisuals.Glow(new Color(Def.Tint.R, Def.Tint.G, Def.Tint.B, 0.20f), additive: true),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            _auraRoot.AddChild(_auraDisc);

            _auraLight = new OmniLight3D
            {
                Name = "Glow",
                LightColor = Def.Tint,
                ShadowEnabled = false,
                OmniAttenuation = 1.5f,
                Position = new Vector3(0f, 0.9f, 0f),
            };
            _auraRoot.AddChild(_auraLight);

            // Parented to the player so it follows without any per-frame work.
            body.AddChild(_auraRoot);
        }

        // Drawn capped (Weapons/FxGuard): a max-area field used to paint the
        // whole screen; the hit radius stays the real one.
        float shown = MathF.Min(radius, Weapons.FxGuard.MaxCentredRadius);
        _auraDisc!.Scale = new Vector3(shown, 1f, shown);
        _auraLight!.OmniRange = shown * 1.7f;
    }

    private void AnimateAura(float dt)
    {
        if (_auraDisc is null || !GodotObject.IsInstanceValid(_auraDisc)) return;

        _pulse = MathF.Max(0f, _pulse - dt * 3.2f);
        _auraDisc.Transparency = Wavebreak.Meta.GraphicsSettings.FxFade(0.35f - 0.35f * _pulse);
        if (_auraLight is not null && GodotObject.IsInstanceValid(_auraLight))
            _auraLight.LightEnergy = 0.5f + 1.6f * _pulse;
    }

    /// <summary>Tear down anything this weapon put in the world. Called when the
    /// run ends; a persistent aura would otherwise outlive its owner.</summary>
    internal void Dispose()
    {
        if (_auraRoot is not null && GodotObject.IsInstanceValid(_auraRoot))
            _auraRoot.QueueFree();

        _auraRoot = null;
        _auraDisc = null;
        _auraLight = null;
    }
}
