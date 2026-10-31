using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Combat;

namespace Wavebreak.Data;

/// <summary>Which firing strategy an evolved weapon switches to. Keep stays on
/// the weapon's own delivery; the rest are the expansion's behaviours
/// (Combat/Weapons/WeaponBehaviors.cs), so an evolution reuses a delivery the
/// game already implements and reads every number from the evolved def.</summary>
public enum EvolutionDelivery
{
    Keep,
    Boomerang,
    Orbit,
    Chain,
    Beam,
    Sentry,
    Vortex,
    Skimstone,
    Strikes,
    Swell,
}

/// <summary>What an evolved weapon does on top of its delivery
/// (Combat/Weapons/EvolutionRiders.cs).</summary>
public enum EvolutionRider
{
    None,
    /// <summary>The whole volley goes off a second time, a moment later.</summary>
    Echo,
    /// <summary>Lightning jumps from a struck body to its neighbours.</summary>
    Arc,
    /// <summary>A hit bursts into a small blast.</summary>
    Blast,
    /// <summary>A kill flings a ring of shards.</summary>
    Shards,
    /// <summary>A hit leaves a lingering pool.</summary>
    Pool,
    /// <summary>A volley also rolls an expanding ring along the ground.</summary>
    Wave,
    /// <summary>A volley also drops something from the sky on a nearby enemy.</summary>
    Meteor,
    /// <summary>A volley also throws a ring of bolts in every direction.</summary>
    Nova,
    /// <summary>A volley also runs a line of ground bursts along the aim.</summary>
    Quake,
}

/// <summary>
/// One weapon evolution, as data: a maxed weapon plus one catalyst (a tome or an
/// item held at any level) becomes a stronger form with a different delivery or
/// rider. The evolved weapon keeps its id, level and rolled upgrades, so quests,
/// the scoreboard, kill counts and signature checks need no second row; only the
/// numbers below change (<see cref="Evolve"/>).
/// </summary>
public sealed class EvolutionDef
{
    public string Id { get; init; } = "";
    public string WeaponId { get; init; } = "";
    public string CatalystId { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";

    public EvolutionDelivery Delivery { get; init; } = EvolutionDelivery.Keep;
    public EvolutionRider Rider { get; init; } = EvolutionRider.None;

    /// <summary>The rider's damage as a fraction of the hit that caused it.</summary>
    public float RiderPower { get; init; } = 0.5f;

    /// <summary>Volley riders: how many bolts, stones or bursts. Arc: jumps.</summary>
    public int RiderCount { get; init; } = 1;

    /// <summary>Rider reach in metres: blast radius, pool radius, arc hop,
    /// wave reach, line length.</summary>
    public float RiderRadius { get; init; } = 3f;

    /// <summary>Fires on every Nth hit or volley, so a rapid weapon pays for its
    /// rate of fire and a hit stream cannot flood the projectile pool.</summary>
    public int RiderEvery { get; init; } = 1;

    public float DamageScale { get; init; } = 1f;
    public float CooldownScale { get; init; } = 1f;
    public int ExtraProjectiles { get; init; }
    public int ExtraPierce { get; init; }

    // Overrides for what a new delivery reads differently; 0 keeps the weapon's own.
    public float Range { get; init; }
    public float Radius { get; init; }
    public float Speed { get; init; }
    public float Spread { get; init; } = -1f;

    /// <summary>A delivery that leaves the aura shape says so here, because the
    /// firing gate reads an aura's reach from its radius.</summary>
    public AttackShape? Shape { get; init; }

    public Color Tint { get; init; } = new(1f, 0.84f, 0.38f);
    public Color Trail { get; init; } = new(1f, 0.92f, 0.6f, 0.9f);

    private WeaponDef? _evolved;

    /// <summary>The stronger form of <paramref name="source"/>, built once. Same
    /// <see cref="WeaponDef.Id"/>, so everything keyed by id keeps working.</summary>
    public WeaponDef Evolve(WeaponDef source)
    {
        if (_evolved is not null) return _evolved;
        var w = (WeaponDef)source.Duplicate();
        w.DisplayName = Name;
        w.Description = Description;
        w.Rarity = Rarity.Legendary;
        w.Tint = Tint;
        w.Damage = source.Damage * DamageScale;
        w.Cooldown = source.Cooldown * CooldownScale;
        w.Projectiles = source.Projectiles + ExtraProjectiles;
        w.Pierce = source.Pierce + ExtraPierce;
        if (Range > 0f) w.Range = Range;
        if (Radius > 0f) w.Radius = Radius;
        if (Speed > 0f) w.ProjectileSpeed = Speed;
        if (Spread >= 0f) w.SpreadDegrees = Spread;
        if (Shape is { } shape) w.Shape = shape;
        return _evolved = w;
    }
}

/// <summary>
/// The evolution table. One rule covers all of it: a weapon at max level plus
/// its catalyst means the next level-up or chest offers a gold EVOLUTION card;
/// taking it is optional, and the catalyst is not consumed.
///
/// Balance record. <see cref="EvolutionDef.DamageScale"/> is tuned against
/// <c>--check-evolutions</c>, which fires the max-level and the evolved weapon
/// at the same pack of dummies: every row lands at 1.6 to 2.2 times the weapon's
/// max-level damage output. Retune with that table, not by feel: a rider's share
/// of the output depends on how many bodies it reaches.
/// </summary>
public static class Evolutions
{
    public static readonly IReadOnlyList<EvolutionDef> All = new List<EvolutionDef>
    {
        // ---- the original eighteen
        new()
        {
            Id = "evo_truthline_rail", WeaponId = "splinter_rail", CatalystId = "tome_precision",
            Name = "Truthline Rail", Description = "The shard no longer flies. A white line is simply drawn through everything in front of you.",
            Delivery = EvolutionDelivery.Beam, DamageScale = 0.66f, Range = 24f, Radius = 0.45f, Spread = 6f,
            Tint = new Color(0.86f, 0.97f, 1f), Trail = new Color(0.8f, 0.95f, 1f, 0.9f),
        },
        new()
        {
            Id = "evo_stormfan", WeaponId = "sparkfan", CatalystId = "stormglass_shard",
            Name = "Living Stormfan", Description = "Every bolt of the fan forks where it lands and leaps to the bodies beside it.",
            Rider = EvolutionRider.Arc, RiderPower = 0.15f, RiderCount = 2, RiderRadius = 5f, DamageScale = 1.1f, ExtraProjectiles = 2,
            Tint = new Color(1f, 0.95f, 0.45f), Trail = new Color(1f, 0.98f, 0.6f, 0.9f),
        },
        new()
        {
            Id = "evo_breakwater_cleaver", WeaponId = "cleaver_arc", CatalystId = "tome_breadth",
            Name = "Breakwater Cleaver", Description = "Every swing keeps going: a wall of water rolls out from the blade and cuts whatever it reaches.",
            Rider = EvolutionRider.Wave, RiderPower = 0.07f, RiderRadius = 6f, DamageScale = 1.35f,
            Tint = new Color(0.55f, 0.92f, 0.96f),
        },
        new()
        {
            Id = "evo_undertow_blades", WeaponId = "riptide_blades", CatalystId = "tome_tempo",
            Name = "Undertow Blades", Description = "Each cut drags a second cut behind it, a heartbeat later, out of the same cold water.",
            Rider = EvolutionRider.Echo, RiderPower = 0.75f, DamageScale = 1.15f,
            Tint = new Color(0.45f, 0.82f, 1f),
        },
        new()
        {
            Id = "evo_sunken_sun", WeaponId = "ember_halo", CatalystId = "emberbrand",
            Name = "Sunken Sun", Description = "The halo burns white, and pieces of it fall out of the sky on whatever dares to stand nearby.",
            Rider = EvolutionRider.Meteor, RiderPower = 1f, RiderCount = 2, RiderRadius = 2.6f, RiderEvery = 2, DamageScale = 1.5f,
            Tint = new Color(1f, 0.72f, 0.28f),
        },
        new()
        {
            Id = "evo_stormcage", WeaponId = "static_field", CatalystId = "sparking_limpet",
            Name = "Stormcage", Description = "The field fills until it cannot hold, then spits bolts of lightning out in every direction.",
            Rider = EvolutionRider.Nova, RiderPower = 0.7f, RiderCount = 10, RiderRadius = 16f, DamageScale = 1.4f,
            Tint = new Color(0.72f, 0.82f, 1f),
        },
        new()
        {
            Id = "evo_broadside_barrage", WeaponId = "mortar_shell", CatalystId = "ballast_stone",
            Name = "Broadside Barrage", Description = "You stop aiming. A whole ship's worth of shells comes down on the enemies around you.",
            Delivery = EvolutionDelivery.Strikes, ExtraProjectiles = 2, DamageScale = 0.62f, Range = 20f,
            Tint = new Color(1f, 0.55f, 0.25f),
        },
        new()
        {
            Id = "evo_bloomtide", WeaponId = "bloomcap_spores", CatalystId = "tome_renewal",
            Name = "Bloomtide", Description = "The pods take root where they burst, and the water around them stays poisonous for a long while.",
            Rider = EvolutionRider.Pool, RiderPower = 0.3f, RiderRadius = 2.4f, RiderEvery = 3, DamageScale = 0.95f,
            Tint = new Color(0.72f, 1f, 0.42f),
        },
        new()
        {
            Id = "evo_aurora_choir", WeaponId = "wisp_seeker", CatalystId = "tome_insight",
            Name = "Aurora Choir", Description = "The wisps sing as they go, and each verse sends a ring of cold light out around you.",
            Rider = EvolutionRider.Nova, RiderPower = 0.42f, RiderCount = 6, RiderRadius = 13f, DamageScale = 1.25f,
            Tint = new Color(0.62f, 1f, 0.92f),
        },
        new()
        {
            Id = "evo_final_verdict", WeaponId = "judgement_lance", CatalystId = "tome_fracture",
            Name = "Final Verdict", Description = "The lance passes sentence on one body, and the sentence jumps to everyone standing beside it.",
            Rider = EvolutionRider.Arc, RiderPower = 0.16f, RiderCount = 3, RiderRadius = 6f, DamageScale = 1.4f,
            Tint = new Color(1f, 0.93f, 0.55f),
        },
        new()
        {
            Id = "evo_deepwater_harpoon", WeaponId = "brinespike", CatalystId = "tome_leeches",
            Name = "Deepwater Harpoon", Description = "It no longer stops at the far end of the line. It turns, finds the next body, and goes on hunting.",
            Delivery = EvolutionDelivery.Skimstone, DamageScale = 1.05f, Radius = 0.5f,
            ExtraPierce = -5,
            Tint = new Color(0.5f, 1f, 0.62f),
        },
        new()
        {
            Id = "evo_glass_gale", WeaponId = "glasswire_needles", CatalystId = "tome_gales",
            Name = "Glass Gale", Description = "The needles fly out on the wind and the wind brings them home, through everything they missed.",
            Delivery = EvolutionDelivery.Boomerang, DamageScale = 0.38f, Speed = 18f, Radius = 0.45f, Spread = 20f,
            Tint = new Color(0.82f, 0.96f, 1f),
        },
        new()
        {
            Id = "evo_winters_keep", WeaponId = "rimeheart", CatalystId = "tome_shells",
            Name = "Winter's Keep", Description = "The cold stops waiting for enemies to come close. Rings of frost march outward from you in a slow, killing tide.",
            Delivery = EvolutionDelivery.Swell, DamageScale = 0.95f, Range = 4.2f, Radius = 1.2f, Speed = 6f,
            Shape = AttackShape.Projectile,
            CooldownScale = 0.7f,
            Tint = new Color(0.6f, 0.88f, 1f),
        },
        new()
        {
            Id = "evo_worldforge_maul", WeaponId = "forgefall_maul", CatalystId = "tome_edges",
            Name = "Worldforge Maul", Description = "The ground gives up. Each blow tears a line of molten bursts away from you toward the enemy.",
            Rider = EvolutionRider.Quake, RiderPower = 0.22f, RiderCount = 4, RiderRadius = 12f, DamageScale = 1.35f,
            Tint = new Color(1f, 0.5f, 0.2f),
        },
        new()
        {
            Id = "evo_frozen_gyre", WeaponId = "hailstone_cluster", CatalystId = "tome_tides",
            Name = "Frozen Gyre", Description = "The hail stops falling and starts circling, dragging the pack into one point before it all lets go.",
            Delivery = EvolutionDelivery.Vortex, DamageScale = 0.62f, ExtraProjectiles = -2, Radius = 2.8f, Speed = 16f,
            Tint = new Color(0.78f, 0.93f, 1f),
        },
        new()
        {
            Id = "evo_plague_hive", WeaponId = "blightwasp_swarm", CatalystId = "tome_currents",
            Name = "Plague Hive", Description = "The swarm builds a home where you stand, and keeps sending out more of itself long after you walk on.",
            Delivery = EvolutionDelivery.Sentry, DamageScale = 0.68f, Radius = 0.42f, Range = 16f, Speed = 18f, CooldownScale = 3f,
            Tint = new Color(0.66f, 0.95f, 0.3f),
        },
        new()
        {
            Id = "evo_sunlance", WeaponId = "searing_longshot", CatalystId = "sextant_lens",
            Name = "Sunlance", Description = "A hit is no longer the end of the shot. Whatever it touches bursts into flame and takes its neighbours with it.",
            Rider = EvolutionRider.Blast, RiderPower = 0.1f, RiderRadius = 3.4f, DamageScale = 1.6f, ExtraPierce = 1,
            Tint = new Color(1f, 0.82f, 0.4f),
        },
        new()
        {
            Id = "evo_cyclone_eye", WeaponId = "tempest_spindle", CatalystId = "tome_fortune",
            Name = "Eye of the Cyclone", Description = "The spindles stop leaving. They wheel around you at speed, and everything that comes close is cut to ribbons.",
            Delivery = EvolutionDelivery.Orbit, DamageScale = 0.46f, Range = 2.4f, Radius = 0.8f, Speed = 15f,
            Tint = new Color(0.72f, 0.9f, 1f),
        },

        // ---- the expansion and the expedition weapons
        new()
        {
            Id = "evo_stormwing", WeaponId = "gullwing_chakram", CatalystId = "gull_feather",
            Name = "Stormwing", Description = "A full flock of rings leaves your hands at once, and each kill throws splinters of whalebone off the wing.",
            Rider = EvolutionRider.Shards, RiderPower = 0.5f, RiderCount = 6, RiderRadius = 9f, ExtraProjectiles = 3, Spread = 200f, DamageScale = 1.85f,
            Tint = new Color(0.96f, 0.92f, 0.7f),
        },
        new()
        {
            Id = "evo_knife_reef", WeaponId = "circling_shoal", CatalystId = "tome_slack_water",
            Name = "Knife Reef", Description = "More fins, and they do not forgive: whatever they finish breaks apart into glass that keeps on cutting.",
            Rider = EvolutionRider.Shards, RiderPower = 0.45f, RiderCount = 6, RiderRadius = 7f, ExtraProjectiles = 2, DamageScale = 1.5f,
            Tint = new Color(0.72f, 0.95f, 1f),
        },
        new()
        {
            Id = "evo_hundred_forks", WeaponId = "forked_squall", CatalystId = "tome_spray",
            Name = "Hundred Forks", Description = "The squall finds the sky. Bolts still jump between bodies, and now others come straight down on top of them.",
            Rider = EvolutionRider.Meteor, RiderPower = 1.2f, RiderCount = 2, RiderRadius = 2.4f, DamageScale = 1.05f,
            Tint = new Color(0.85f, 0.88f, 1f),
        },
        new()
        {
            Id = "evo_reef_of_teeth", WeaponId = "barnacle_mines", CatalystId = "tome_plenty",
            Name = "Reef of Teeth", Description = "The mines breed and bite. Anything they kill scatters shell in every direction.",
            Rider = EvolutionRider.Shards, RiderPower = 0.6f, RiderCount = 8, RiderRadius = 9f, ExtraProjectiles = 1, DamageScale = 1.45f,
            Tint = new Color(1f, 0.72f, 0.5f),
        },
        new()
        {
            Id = "evo_breaching_gun", WeaponId = "reef_shotgun", CatalystId = "gutting_hook",
            Name = "Breaching Gun", Description = "Every pellet carries a shaped charge. What it hits comes apart in a small, wet explosion.",
            Rider = EvolutionRider.Blast, RiderPower = 0.5f, RiderRadius = 2.2f, RiderEvery = 2, DamageScale = 1.55f,
            Tint = new Color(0.4f, 1f, 0.9f),
        },
        new()
        {
            Id = "evo_twin_horizons", WeaponId = "skyline_swords", CatalystId = "whetstone",
            Name = "Twin Horizons", Description = "The pair strikes, and a moment later the air it passed through strikes again.",
            Rider = EvolutionRider.Echo, RiderPower = 0.8f, DamageScale = 1.05f,
            Tint = new Color(0.9f, 0.95f, 1f),
        },
    };

    private static readonly Dictionary<string, EvolutionDef> ByWeapon = Index(e => e.WeaponId);
    private static readonly Dictionary<string, EvolutionDef> ByCatalyst = Index(e => e.CatalystId);
    private static readonly Dictionary<string, EvolutionDef> ByIdMap = Index(e => e.Id);

    private static Dictionary<string, EvolutionDef> Index(Func<EvolutionDef, string> key)
    {
        var map = new Dictionary<string, EvolutionDef>(StringComparer.Ordinal);
        foreach (EvolutionDef e in All) map.TryAdd(key(e), e);
        return map;
    }

    public static EvolutionDef? ById(string? id) => id is not null && ByIdMap.TryGetValue(id, out EvolutionDef? e) ? e : null;

    public static EvolutionDef? ForWeapon(string? id) => id is not null && ByWeapon.TryGetValue(id, out EvolutionDef? e) ? e : null;

    public static EvolutionDef? ForCatalyst(string? id) => id is not null && ByCatalyst.TryGetValue(id, out EvolutionDef? e) ? e : null;

    /// <summary>A catalyst's display name, tome or item.</summary>
    public static string CatalystName(EvolutionDef e)
        => Tomes.ById(e.CatalystId)?.DisplayName ?? Catalog.ItemById(e.CatalystId)?.DisplayName ?? e.CatalystId;

    public static string WeaponName(EvolutionDef e) => Catalog.WeaponById(e.WeaponId)?.DisplayName ?? e.WeaponId;
}
