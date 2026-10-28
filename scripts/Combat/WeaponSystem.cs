using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Core;
using Wavebreak.Data;
using Wavebreak.Stats;

namespace Wavebreak.Combat;

/// <summary>
/// The player's weapons: what they own, when each one goes off, and the hit
/// reporting that everything downstream of a hit listens to.
///
/// A <see cref="Node"/> rather than a plain object because the scene root builds
/// it with the rest of the world and because pooled projectiles need somewhere in
/// the tree to live — and specifically a plain <c>Node</c> rather than a
/// <see cref="Node3D"/>, since a transform-less parent leaves its 3D children in
/// world space, which is exactly what a projectile wants and what parenting them
/// to the player would ruin.
///
/// It does not run its own <c>_Process</c>. The scene root ticks it in a defined
/// order, after enemies have moved, so weapons fire at where things actually are
/// rather than at where they were on whichever frame the scene tree happened to
/// visit them.
/// </summary>
public sealed partial class WeaponSystem : Node
{
    /// <summary>How many distinct weapons the player may carry. The cap is what
    /// makes a level-up a decision: without one, every offer is a yes.</summary>
    public const int MaxWeapons = 6;

    /// <summary>Ceiling on shots in flight. Reaching it drops new shots rather
    /// than the frame rate.</summary>
    public const int ProjectileCapacity = 512;

    /// <summary>Publish <see cref="EnemyDamaged"/> for every landed hit. On by
    /// default: lifesteal, damage meters and on-hit effects all read it, and the
    /// target itself has already written the real damage back into the info by
    /// the time it goes out.</summary>
    public static bool PublishHitEvents { get; set; } = true;

    /// <summary>Publish a floating number per landed hit. Split from
    /// <see cref="PublishHitEvents"/> so the number can be silenced — for a
    /// performance test, or if the HUD would rather draw them itself — without
    /// also silencing the mechanics.</summary>
    public static bool PublishDamageNumbers { get; set; } = true;

    /// <summary>Below this, a hit is not worth a number on screen.</summary>
    private const float PopupThreshold = 0.5f;

    private static readonly Color CritColor = new(1.00f, 0.85f, 0.30f);

    private readonly List<WeaponRuntime> _weapons = new(MaxWeapons);
    private readonly StatSheet _standaloneStats = new();

    private Player? _player;
    private StatSheet? _stats;
    private Rng? _rng;

    // ------------------------------------------------------------------ state

    /// <summary>Everything the player is carrying, in acquisition order.</summary>
    public IReadOnlyList<WeaponRuntime> Weapons => _weapons;

    /// <summary>True when no new weapon can be taken — only upgrades.</summary>
    public bool IsFull => _weapons.Count >= Slots;

    /// <summary>Weapon slots this run: <see cref="MaxWeapons"/> plus the Pearl
    /// Shop's Weapon Rack levels (Meta/ShopExtras).</summary>
    public int Slots { get; set; } = MaxWeapons;

    /// <summary>The player node, when there is one. Auras parent themselves to it.</summary>
    public Node3D? Body => _player;

    /// <summary>
    /// The sheet weapons are resolved against — the player's, or a private empty
    /// one when there is no player. The fallback exists so a headless test can
    /// tick weapons without building an avatar, and so a frame between runs
    /// cannot null-reference.
    /// </summary>
    public StatSheet Stats => _stats ?? _standaloneStats;

    /// <summary>
    /// The random source crit rolls come from. Assign the run's seeded
    /// <see cref="Rng"/> so a replayed seed crits on the same shots.
    ///
    /// Defaulted rather than reached for: combat resolving this out of the scene
    /// root would couple the whole layer to the composition root for one number,
    /// and it would stop building — and stop being testable — on its own.
    /// </summary>
    public Rng Rng
    {
        get => _rng ??= new Rng(Time.GetTicksUsec());
        set => _rng = value;
    }

    /// <summary>Where shots come from: mid-torso, not the feet.</summary>
    public Vector3 MuzzlePosition { get; private set; }

    /// <summary>Facing, flattened onto the ground plane. Tracks the player each
    /// tick; assign it directly when driving weapons without one.</summary>
    public Vector3 AimDirection { get; set; } = Vector3.Forward;

    /// <summary>Shots fired this run. Cheap, and the results screen wants it.</summary>
    public int ShotsFired { get; private set; }

    public WeaponSystem()
    {
        Name = "WeaponSystem";

        // Ticked by the scene root in a defined order; see the class note.
        SetProcess(false);
        SetPhysicsProcess(false);
    }

    public override void _Ready()
    {
        Projectile.Initialize(this, ProjectileCapacity);
        ContentAudit.Attach();
    }

    public override void _ExitTree()
    {
        ContentAudit.Report();
        Clear();
        Projectile.Shutdown();
        Targeting.Reset();
    }

    // ------------------------------------------------------------- ownership

    /// <summary>
    /// Take a weapon, or upgrade it if it is already owned.
    ///
    /// Levelling instead of duplicating is what stops the offer pool turning into
    /// six copies of the same gun, and it is why the weapon cap is meaningful.
    /// A weapon that is owned at maximum level, or a new one when the slots are
    /// full, is silently ignored — <see cref="CanAdd"/> is how a caller avoids
    /// offering either.
    /// </summary>
    public void Add(WeaponDef def) => Add(def, null);

    /// <summary>
    /// Take a weapon, or level it with <paramref name="upgrade"/> (the rolled
    /// upgrade shown on the level-up card). A null upgrade on an owned weapon
    /// rolls one here, with the player's luck, so callers that only have a def
    /// (chests, scripted rewards) still get a real upgrade.
    /// </summary>
    public void Add(WeaponDef def, WeaponUpgrade? upgrade)
    {
        if (def is null || string.IsNullOrEmpty(def.Id)) return;

        WeaponRuntime? owned = Find(def.Id);
        if (owned is not null)
        {
            if (owned.CanLevel && upgrade is null) upgrade = RollUpgrade(def);
            if (owned.LevelUp(upgrade))
                EventBus.Publish(new WeaponAcquired(def.Id, owned.Level));
            return;
        }

        if (IsFull) return;

        WeaponRuntime runtime = new(def);
        _weapons.Add(runtime);
        EventBus.Publish(new WeaponAcquired(def.Id, runtime.Level));
    }

    /// <summary>True when <see cref="Add"/> would actually change something:
    /// a free slot for a new weapon, or headroom on an owned one.</summary>
    public bool CanAdd(WeaponDef def)
    {
        if (def is null || string.IsNullOrEmpty(def.Id)) return false;

        WeaponRuntime? owned = Find(def.Id);
        return owned is null ? !IsFull : owned.CanLevel;
    }

    public bool Has(string id) => Find(id) is not null;

    /// <summary>The runtime of an owned weapon, or null.</summary>
    public WeaponRuntime? Get(string id) => Find(id);

    /// <summary>Roll an upgrade for <paramref name="def"/> with the player's
    /// luck and extra-line stat.</summary>
    public WeaponUpgrade RollUpgrade(WeaponDef def, Rng? rng = null)
    {
        StatSheet s = Stats;
        return WeaponUpgrades.Roll(def, rng ?? Rng, s.Get(Stat.Luck), (int)s.Get(Stat.UpgradeStats));
    }

    /// <summary>Current level of a weapon, or 0 when it is not owned.</summary>
    public int LevelOf(string id) => Find(id)?.Level ?? 0;

    /// <summary>Drop every weapon and everything they put in the world.</summary>
    public void Clear()
    {
        for (int i = 0; i < _weapons.Count; i++) _weapons[i].Dispose();
        _weapons.Clear();
    }

    private WeaponRuntime? Find(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;

        for (int i = 0; i < _weapons.Count; i++)
            if (_weapons[i].Def.Id == id) return _weapons[i];

        return null;
    }

    // ------------------------------------------------------------------ tick

    /// <summary>Advance every weapon and every shot in flight by one frame.</summary>
    public void Tick(float dt)
    {
        if (dt <= 0f) return;

        Resolve();
        if (SuspendForRest()) return;
        Targeting.Refresh();

        // Flag-gated (--validate-content); runs once, before any real shot.
        if (ContentAudit.Pending && _player is not null) ContentAudit.Run(this);

        // Pacifist challenge: weapons hold fire for a while on each map; shots
        // already in flight still land.
        // Co-op: a downed player's weapons are silent until a teammate revives them.
        if (!Meta.Challenges.WeaponsSilenced && !(Net.Coop.InRun && _player is { IsAlive: false }))
            for (int i = 0; i < _weapons.Count; i++)
                _weapons[i].Tick(dt, this);

        using (Wavebreak.Game.Prof.Scope("Projectiles")) Projectile.UpdateAll(dt);
    }

    /// <summary>Re-reads the player once per tick. Doing it per shot would mean an
    /// interop call and a validity check for every projectile in a volley.</summary>
    private void Resolve()
    {
        Player? player = Player.Instance;

        if (player is null || !GodotObject.IsInstanceValid(player))
        {
            _player = null;
            _stats = null;
            return;
        }

        _player = player;
        _stats = player.Stats;
        MuzzlePosition = player.CenterPosition;

        Vector3 aim = player.AimDirection;
        aim.Y = 0f;
        if (aim.LengthSquared() > 1e-6f) AimDirection = aim.Normalized();
    }

    // ---------------------------------------------------------------- firing

    /// <summary>
    /// The direction a weapon should fire in: at the nearest target inside
    /// <paramref name="range"/>, or straight ahead when there is nothing there.
    ///
    /// Firing regardless is deliberate. Weapons that hold fire until something is
    /// in range feel broken during the quiet seconds between waves, and a shot
    /// into empty space costs nothing.
    /// </summary>
    internal Vector3 AimAt(Vector3 origin, float range)
    {
        IDamageable? target = Targeting.Nearest(origin, range);
        if (target is not null)
        {
            Vector3 to = target.Position - origin;
            to.Y = 0f;
            if (to.LengthSquared() > 1e-6f) return to.Normalized();
        }

        Vector3 aim = AimDirection;
        aim.Y = 0f;
        return aim.LengthSquared() > 1e-6f ? aim.Normalized() : Vector3.Forward;
    }

    /// <summary>
    /// Builds the hit one shot carries.
    ///
    /// The crit roll happens here, once per shot, before the hit is cloned onto
    /// each target it reaches — so a piercing shot or a blast crits as a whole.
    /// Crit chance is the player's plus the weapon's own, which is what lets a
    /// weapon be characterised as "crits a lot" without an item to say so.
    /// </summary>
    internal DamageInfo BuildDamage(WeaponRuntime weapon)
    {
        WeaponDef def = weapon.Def;
        WeaponBonus bonus = weapon.Bonus;
        StatSheet stats = Stats;

        float amount = weapon.BaseDamage * stats.GetMultiplier(Stat.Damage);
        if (def.Element != Element.Physical) amount *= stats.GetMultiplier(Stat.ElementalDamage);

        bool crit = Rng.Chance(stats.Get(Stat.CritChance) + def.CritChance + bonus.CritChance);
        bool mega = false;
        if (crit)
        {
            amount *= 1f + stats.Get(Stat.CritDamage);
            // Rolled only when the stat exists, so a build without it draws
            // exactly the same crit sequence as before megacrits existed.
            mega = Rng.Chance(stats.Get(Stat.MegacritChance));
            if (mega) amount *= StatusEffects.MegacritMultiplier;
        }

        return new DamageInfo
        {
            Amount = amount,
            Element = def.Element,
            Crit = crit,
            Megacrit = mega,
            Knockback = def.Knockback * (1f + bonus.Knockback) * stats.Get(Stat.Knockback),
            ProcCoefficient = def.ProcCoefficient,
            StatusChance = bonus.StatusChance,
            StatusDuration = 1f + bonus.StatusDuration,
            Source = def.Id,
        };
    }

    internal void NoteShots(int count) => ShotsFired += count;

    /// <summary>
    /// Announce a hit that landed.
    ///
    /// Called after the target has had <see cref="IDamageable.TakeDamage"/>, so
    /// <paramref name="info"/> holds what actually happened rather than what was
    /// attempted — overkill already clamped, dodges already nullified. Everything
    /// that reacts to damage reads it from here, which is why nothing in combat
    /// needs a reference to the HUD, the inventory or the player's health.
    /// </summary>
    internal void ReportHit(IDamageable target, DamageInfo info)
    {
        if (info.Nullified || info.Amount <= 0f) return;

        if (PublishHitEvents)
            EventBus.Publish(new EnemyDamaged(target, info));

        // Statuses after the hit is announced, so a listener sees the hit
        // before the burn it caused.
        StatusEffects.OnHit(this, target, info);

        if (!PublishDamageNumbers || info.Amount < PopupThreshold) return;

        EventBus.Publish(new PopupRequested(
            target.Position + Vector3.Up * 0.35f,
            Mathf.RoundToInt(Mathf.Max(1f, info.Amount)).ToString() + (info.Megacrit ? "!" : ""),
            info.Megacrit ? StatusEffects.MegacritColor : info.Crit ? CritColor : ColorOf(info.Element),
            info.Megacrit ? 1.75f : info.Crit ? 1.35f : 0.95f));
    }

    /// <summary>Damage numbers carry the element as colour. It is the only channel
    /// an untextured game has for telling the player which of their six weapons
    /// is doing the work.</summary>
    public static Color ColorOf(Element element) => element switch
    {
        Element.Fire => new Color(1.00f, 0.55f, 0.20f),
        Element.Ice => new Color(0.55f, 0.85f, 1.00f),
        Element.Lightning => new Color(0.95f, 0.90f, 0.35f),
        Element.Poison => new Color(0.60f, 0.95f, 0.35f),
        _ => new Color(0.96f, 0.96f, 0.96f),
    };
}
