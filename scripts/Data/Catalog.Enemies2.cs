using System.Collections.Generic;
using Godot;

namespace Wavebreak.Data;

/// <summary>
/// The fourth wave of enemies: twenty monsters built on the expansion traits
/// (flying, burrowing, shields, auras, healing, artillery, blinking) and two
/// bosses with the new moves, plus theme tags for the whole roster.
///
/// Pacing rule, same as the third wave: nothing here unlocks before 160s, so the
/// opening minutes tuned against the starter weapon meet exactly the roster they
/// did before. Costs sit on rungs the ladder already has, next to an older enemy
/// of similar threat, so a minute's budget buys a mix rather than a takeover.
///
/// No static field initialisers in this file: it is a partial of
/// <see cref="Catalog"/>, and initialiser order across partial files is not
/// defined, so everything is built inside the methods that need it.
/// </summary>
public static partial class Catalog
{
    /// <summary>Every enemy carrying <paramref name="tag"/> in its
    /// <see cref="EnemyDef.Themes"/>. For map rosters.</summary>
    public static List<EnemyDef> EnemiesWithTheme(string tag)
    {
        var list = new List<EnemyDef>();
        foreach (EnemyDef e in EnemyList) if (e.HasTheme(tag)) list.Add(e);
        return list;
    }

    private static List<EnemyDef> WithExpansionEnemies(List<EnemyDef> baseList)
    {
        Dictionary<string, string[]> themes = ThemeTable();
        foreach (EnemyDef e in baseList)
            if (e.Themes.Length == 0 && themes.TryGetValue(e.Id, out string[]? t)) e.Themes = t;
        baseList.AddRange(BuildExpansionEnemies());
        baseList.AddRange(BuildCuteEnemies());   // Data/Catalog.Enemies3.cs
        baseList.AddRange(CityEnemies());
        return baseList;
    }

    private static string[] T(params string[] tags) => tags;

    /// <summary>Theme tags for the enemies that predate the tag field.</summary>
    private static Dictionary<string, string[]> ThemeTable() => new()
    {
        ["mote"] = T("meadow", "shore", "swarm"),
        ["husk"] = T("meadow", "ash", "grunt"),
        ["skitterling"] = T("meadow", "ash", "swarm"),
        ["gravel_brute"] = T("meadow", "frost", "tank"),
        ["cinderling"] = T("ash", "runner"),
        ["pale_sentinel"] = T("frost", "tank"),
        ["rift_maw"] = T("deep", "ash", "tank"),
        ["ash_warden"] = T("ash", "elite"),
        ["frost_herald"] = T("frost", "elite"),
        ["thresher_prime"] = T("meadow", "boss"),
        ["brine_leech"] = T("shore", "reef", "swarm"),
        ["kelp_shambler"] = T("reef", "shore", "grunt"),
        ["gloom_lurcher"] = T("deep", "runner"),
        ["tidewall_colossus"] = T("shore", "frost", "tank"),
        ["gale_revenant"] = T("storm", "elite"),
        ["breaker_matriarch"] = T("shore", "storm", "boss"),
        ["brinespitter"] = T("shore", "reef", "ranged"),
        ["bloat_polyp"] = T("reef", "meadow", "exploder"),
        ["reefhorn"] = T("reef", "charger"),
        ["surge_leaper"] = T("storm", "shore", "leaper"),
        ["shoalmother"] = T("reef", "deep", "splitter"),
        ["shoal_fry"] = T("reef", "deep", "swarm"),
        ["deepcaller"] = T("deep", "ranged"),
        ["undertow_oracle"] = T("deep", "boss"),
        ["knell_leviathan"] = T("deep", "storm", "boss"),
    };

    private static List<EnemyDef> BuildExpansionEnemies() => new()
    {
        // ---- Swarm runner, cost 2, at 160s. Frailer than a Skitterling and a
        // touch slower than the player: comes in numbers, dies to anything.
        new EnemyDef
        {
            Id = "spume_mite", DisplayName = "Spume Mite",
            Health = 11f, Damage = 5f, MoveSpeed = 4.3f,
            AttackRange = 1.2f, AttackCooldown = 0.7f,
            Radius = 0.28f, Height = 0.6f,
            XpValue = 2, GoldValue = 1,
            Cost = 2, UnlockAtSeconds = 160f,
            Tint = new Color(0.86f, 0.95f, 0.98f),
            Themes = T("shore", "reef", "swarm"),
        },

        // ---- Flying swarm, cost 3, at 185s. Hovers over walls and hills, so
        // terrain stops being cover; dips to head height to peck.
        new EnemyDef
        {
            Id = "squall_gull", DisplayName = "Squall Gull",
            Health = 16f, Damage = 7f, MoveSpeed = 4.4f,
            AttackRange = 1.4f, AttackCooldown = 0.9f,
            Radius = 0.36f, Height = 0.9f,
            XpValue = 3, GoldValue = 1,
            Cost = 3, UnlockAtSeconds = 185f,
            Tint = new Color(0.92f, 0.92f, 0.88f),
            Hover = 2.4f,
            Themes = T("storm", "shore", "flyer", "swarm"),
        },

        // ---- Flying shooter, cost 4, at 200s. A Brinespitter that floats:
        // one slow glowing shot from 10m, from above the crowd.
        new EnemyDef
        {
            Id = "lantern_jelly", DisplayName = "Lantern Jelly",
            Health = 26f, Damage = 7f, MoveSpeed = 2.7f,
            AttackRange = 1.5f, AttackCooldown = 1.2f,
            Radius = 0.45f, Height = 1.2f,
            XpValue = 4, GoldValue = 2,
            Cost = 4, UnlockAtSeconds = 200f,
            Tint = new Color(0.55f, 0.85f, 1.00f),
            Behavior = EnemyBehavior.Shooter,
            AbilityRange = 10f, AbilityCooldown = 3.4f, WindUp = 0.75f,
            AbilityDamage = 1f, AbilitySpeed = 7f, AbilityCount = 1,
            Hover = 2.8f,
            Themes = T("deep", "reef", "flyer", "ranged"),
        },

        // ---- Burrower, cost 4, at 240s. Crosses the field under the sand (a
        // ripple, untouchable), then the ring fills under the player for 0.7s
        // and it erupts. Once up it fights in the open for a while.
        new EnemyDef
        {
            Id = "sand_scuttler", DisplayName = "Sand Scuttler",
            Health = 40f, Damage = 9f, MoveSpeed = 3.0f,
            AttackRange = 1.5f, AttackCooldown = 1.1f,
            Radius = 0.45f, Height = 0.8f, KnockbackResist = 0.2f,
            XpValue = 4, GoldValue = 3,
            Cost = 4, UnlockAtSeconds = 240f,
            Tint = new Color(0.95f, 0.55f, 0.35f),
            Behavior = EnemyBehavior.Burrower,
            AbilityRange = 3.2f, AbilityCooldown = 6f, WindUp = 0.7f,
            AbilityRadius = 2.2f, AbilityDamage = 1.4f,
            Themes = T("shore", "reef", "burrower"),
        },

        // ---- Healer, cost 5, at 260s. Stays behind the pack and pulses 18%
        // health into every injured ally within 6m. The green ring is the tell;
        // the answer is to reach it first.
        new EnemyDef
        {
            Id = "tidepool_mender", DisplayName = "Tidepool Mender",
            Health = 35f, Damage = 6f, MoveSpeed = 2.7f,
            AttackRange = 1.4f, AttackCooldown = 1.3f,
            Radius = 0.45f, Height = 1.3f,
            XpValue = 6, GoldValue = 4,
            Cost = 5, UnlockAtSeconds = 260f,
            Tint = new Color(0.45f, 0.95f, 0.65f),
            Behavior = EnemyBehavior.Healer,
            AbilityRange = 8f, AbilityCooldown = 4f, WindUp = 0.8f,
            AbilityRadius = 6f, SupportAmount = 0.18f,
            Themes = T("reef", "shore", "support"),
        },

        // ---- Shielded, cost 6, at 280s. Blocks 80% of anything that hits its
        // front 120 degrees; go round it, or let area damage do the work.
        new EnemyDef
        {
            Id = "shellback_guard", DisplayName = "Shellback Guard",
            Health = 58f, Damage = 12f, MoveSpeed = 2.9f,
            AttackRange = 1.8f, AttackCooldown = 1.25f,
            Radius = 0.52f, Height = 1.75f, KnockbackResist = 0.4f,
            XpValue = 7, GoldValue = 4,
            Cost = 6, UnlockAtSeconds = 280f,
            Tint = new Color(0.62f, 0.58f, 0.42f),
            ShieldArc = 120f, ShieldBlock = 0.8f,
            Themes = T("reef", "shore", "shield", "tank"),
        },

        // ---- Buffer, cost 5, at 285s. Chases like anything else, but every
        // ally inside its 5.5m ring walks 30% faster.
        new EnemyDef
        {
            Id = "conch_drummer", DisplayName = "Conch Drummer",
            Health = 50f, Damage = 10f, MoveSpeed = 2.6f,
            AttackRange = 1.6f, AttackCooldown = 1.2f,
            Radius = 0.5f, Height = 1.5f, KnockbackResist = 0.2f,
            XpValue = 6, GoldValue = 4,
            Cost = 5, UnlockAtSeconds = 285f,
            Tint = new Color(1.00f, 0.78f, 0.45f),
            Aura = EnemyAura.Haste, AuraRadius = 5.5f, AuraStrength = 0.3f,
            Themes = T("reef", "shore", "support"),
        },

        // ---- Teleporter, cost 5, at 300s. From up to 16m it marks a spot
        // beside the player in violet, flickers for 0.65s and is there.
        new EnemyDef
        {
            Id = "mistwalker", DisplayName = "Mistwalker",
            Health = 42f, Damage = 12f, MoveSpeed = 3.2f,
            AttackRange = 1.6f, AttackCooldown = 1.0f,
            Radius = 0.45f, Height = 1.6f,
            XpValue = 6, GoldValue = 4,
            Cost = 5, UnlockAtSeconds = 300f,
            Tint = new Color(0.70f, 0.55f, 0.95f),
            Behavior = EnemyBehavior.Blinker,
            AbilityRange = 16f, AbilityCooldown = 5.5f, WindUp = 0.65f,
            Themes = T("storm", "deep", "teleporter"),
        },

        // ---- Artillery, cost 6, at 310s. Three shells from 13m: circles fill
        // for 1.2s on and around the player. Standing still is the mistake.
        new EnemyDef
        {
            Id = "brine_mortar", DisplayName = "Brine Mortar",
            Health = 45f, Damage = 10f, MoveSpeed = 2.4f,
            AttackRange = 1.5f, AttackCooldown = 1.3f,
            Radius = 0.55f, Height = 1.3f, KnockbackResist = 0.3f,
            XpValue = 7, GoldValue = 4,
            Cost = 6, UnlockAtSeconds = 310f,
            Tint = new Color(0.50f, 0.62f, 0.70f),
            Behavior = EnemyBehavior.Artillery,
            AbilityRange = 13f, AbilityCooldown = 4.5f, WindUp = 0.8f,
            AbilityCount = 3, AbilityRadius = 2.0f, AbilityDamage = 1.3f, ShellDelay = 1.2f,
            Themes = T("shore", "ranged", "artillery"),
        },

        // ---- Flying charger, cost 7, at 340s. Glides in over everything and
        // runs a painted lane at 19 m/s; the lane does not care about hills.
        new EnemyDef
        {
            Id = "gloomwing_ray", DisplayName = "Gloomwing Ray",
            Health = 60f, Damage = 13f, MoveSpeed = 4.2f,
            AttackRange = 1.7f, AttackCooldown = 1.2f,
            Radius = 0.6f, Height = 1.2f, KnockbackResist = 0.3f,
            XpValue = 8, GoldValue = 5,
            Cost = 7, UnlockAtSeconds = 340f,
            Tint = new Color(0.30f, 0.28f, 0.52f),
            Behavior = EnemyBehavior.Charger,
            AbilityRange = 13f, AbilityCooldown = 4f, WindUp = 0.8f,
            AbilitySpeed = 19f, AbilityDamage = 1.5f, AbilityRadius = 0.9f,
            Hover = 2.0f,
            Themes = T("deep", "storm", "flyer", "charger"),
        },

        // ---- Burrower, cost 9, at 430s. The big one: a 3m eruption, and 95
        // health once it is up.
        new EnemyDef
        {
            Id = "silt_eel", DisplayName = "Silt Eel",
            Health = 95f, Damage = 18f, MoveSpeed = 3.4f,
            AttackRange = 1.9f, AttackCooldown = 1.2f,
            Radius = 0.55f, Height = 1.4f, KnockbackResist = 0.4f,
            XpValue = 12, GoldValue = 7,
            Cost = 9, UnlockAtSeconds = 430f,
            Tint = new Color(0.55f, 0.48f, 0.36f),
            Behavior = EnemyBehavior.Burrower,
            AbilityRange = 3.6f, AbilityCooldown = 7f, WindUp = 0.75f,
            AbilityRadius = 3.0f, AbilityDamage = 1.6f,
            Themes = T("deep", "reef", "burrower"),
        },

        // ---- Flying artillery, cost 11, at 520s. Four shells from 14m,
        // from somewhere the melee weapons cannot reach.
        new EnemyDef
        {
            Id = "thunderhead", DisplayName = "Thunderhead",
            Health = 90f, Damage = 16f, MoveSpeed = 2.5f,
            AttackRange = 1.6f, AttackCooldown = 1.3f,
            Radius = 0.7f, Height = 1.6f, KnockbackResist = 0.5f,
            XpValue = 15, GoldValue = 9,
            Cost = 11, UnlockAtSeconds = 520f,
            Tint = new Color(0.52f, 0.55f, 0.66f),
            Behavior = EnemyBehavior.Artillery,
            AbilityRange = 14f, AbilityCooldown = 5f, WindUp = 0.85f,
            AbilityCount = 4, AbilityRadius = 2.3f, AbilityDamage = 1.2f, ShellDelay = 1.15f,
            Hover = 3.2f,
            Themes = T("storm", "flyer", "ranged", "artillery"),
        },

        // ---- Tank, cost 12, at 480s. Between the Rift Maw and the Colossus:
        // slow, very hard to shove, crusted with barnacles.
        new EnemyDef
        {
            Id = "barnacle_hulk", DisplayName = "Barnacle Hulk",
            Health = 170f, Damage = 30f, MoveSpeed = 2.5f,
            AttackRange = 2.4f, AttackCooldown = 1.5f,
            Radius = 0.95f, Height = 2.7f, KnockbackResist = 0.88f,
            XpValue = 22, GoldValue = 13,
            Cost = 12, UnlockAtSeconds = 480f,
            Tint = new Color(0.48f, 0.52f, 0.46f),
            Themes = T("shore", "reef", "tank"),
        },

        // ============================================================ elites

        // ---- Elite, cost 10, at 260s. A flying herald that hastes its wave by
        // 35% inside a 7m ring. Kill it and the wave slows down.
        new EnemyDef
        {
            Id = "tempest_herald", DisplayName = "Tempest Herald",
            Health = 280f, Damage = 20f, MoveSpeed = 4.0f,
            AttackRange = 1.9f, AttackCooldown = 1.0f,
            Radius = 0.7f, Height = 2.0f, KnockbackResist = 0.5f,
            XpValue = 40, GoldValue = 25,
            Cost = 10, UnlockAtSeconds = 260f, IsElite = true,
            Tint = new Color(0.65f, 0.85f, 1.00f),
            Aura = EnemyAura.Haste, AuraRadius = 7f, AuraStrength = 0.35f,
            Hover = 2.6f,
            Themes = T("storm", "flyer", "elite", "aura"),
        },

        // ---- Elite, cost 13, at 380s. Shield in front, a ward ring around it:
        // everything inside takes 35% less damage, including itself.
        new EnemyDef
        {
            Id = "carapace_bastion", DisplayName = "Carapace Bastion",
            Health = 520f, Damage = 28f, MoveSpeed = 2.8f,
            AttackRange = 2.3f, AttackCooldown = 1.4f,
            Radius = 0.9f, Height = 2.5f, KnockbackResist = 0.9f,
            XpValue = 60, GoldValue = 38,
            Cost = 13, UnlockAtSeconds = 380f, IsElite = true,
            Tint = new Color(0.85f, 0.45f, 0.40f),
            ShieldArc = 140f, ShieldBlock = 0.85f,
            Aura = EnemyAura.Ward, AuraRadius = 6.5f, AuraStrength = 0.35f,
            Themes = T("reef", "shield", "elite", "aura", "tank"),
        },

        // ---- Elite, cost 12, at 400s. Blinks every four seconds from 18m.
        new EnemyDef
        {
            Id = "fogbound_stalker", DisplayName = "Fogbound Stalker",
            Health = 360f, Damage = 28f, MoveSpeed = 4.2f,
            AttackRange = 2.0f, AttackCooldown = 0.95f,
            Radius = 0.7f, Height = 2.3f, KnockbackResist = 0.6f,
            XpValue = 50, GoldValue = 32,
            Cost = 12, UnlockAtSeconds = 400f, IsElite = true,
            Tint = new Color(0.55f, 0.60f, 0.72f),
            Behavior = EnemyBehavior.Blinker,
            AbilityRange = 18f, AbilityCooldown = 4f, WindUp = 0.55f,
            Themes = T("storm", "deep", "teleporter", "elite"),
        },

        // ---- Elite, cost 14, at 460s. Leaps like a Surge Leaper, and its ring
        // knits every ally inside back together at 4% a second.
        new EnemyDef
        {
            Id = "coral_warlord", DisplayName = "Coral Warlord",
            Health = 560f, Damage = 30f, MoveSpeed = 3.4f,
            AttackRange = 2.3f, AttackCooldown = 1.3f,
            Radius = 0.85f, Height = 2.6f, KnockbackResist = 0.85f,
            XpValue = 65, GoldValue = 40,
            Cost = 14, UnlockAtSeconds = 460f, IsElite = true,
            Tint = new Color(1.00f, 0.50f, 0.55f),
            Behavior = EnemyBehavior.Leaper,
            AbilityRange = 13f, AbilityCooldown = 5f, WindUp = 0.6f,
            AbilityRadius = 2.8f, AbilityDamage = 1.4f,
            Aura = EnemyAura.Mend, AuraRadius = 7f, AuraStrength = 0.04f,
            Themes = T("reef", "elite", "aura", "leaper"),
        },

        // ---- Elite, cost 15, at 560s. A floating eye that fires five-shot
        // fans and drives everything in its 7m ring 40% faster.
        new EnemyDef
        {
            Id = "maelstrom_eye", DisplayName = "Maelstrom Eye",
            Health = 600f, Damage = 26f, MoveSpeed = 3.0f,
            AttackRange = 2.0f, AttackCooldown = 1.3f,
            Radius = 0.9f, Height = 2.2f, KnockbackResist = 0.85f,
            XpValue = 70, GoldValue = 45,
            Cost = 15, UnlockAtSeconds = 560f, IsElite = true,
            Tint = new Color(0.30f, 0.80f, 0.85f),
            Behavior = EnemyBehavior.Shooter,
            AbilityRange = 9f, AbilityCooldown = 3.8f, WindUp = 0.8f,
            AbilityDamage = 0.7f, AbilitySpeed = 8.5f, AbilityCount = 5, AbilitySpread = 70f,
            Aura = EnemyAura.Frenzy, AuraRadius = 7f, AuraStrength = 0.4f,
            Hover = 3.0f,
            Themes = T("deep", "storm", "flyer", "elite", "aura", "ranged"),
        },

        // ============================================================ bosses
        //
        // Neither is the default pick of the stage director (it takes the
        // highest-cost fresh boss): cost 39 sits under the Breaker Matriarch's
        // 40, and 54 under the Knell Leviathan's 55, so the tuned campaign keeps
        // its bosses. A map that wants these names them in StageDef.BossId; the
        // timed-boss fallback and repeat fights draw them by weight.

        // ---- Boss, tier 2. Rolls riptides down painted lanes, rings the player
        // with whirlpool blasts, and calls gulls. Phase two adds a ring burst.
        new EnemyDef
        {
            Id = "gyre_matron", DisplayName = "Gyre Matron",
            Health = 3300f, Damage = 44f, MoveSpeed = 2.7f,
            AttackRange = 3.2f, AttackCooldown = 1.4f,
            Radius = 1.5f, Height = 4.4f, KnockbackResist = 0.96f,
            XpValue = 520, GoldValue = 340,
            Cost = 39, UnlockAtSeconds = 470f, IsBoss = true,
            Tint = new Color(0.40f, 0.70f, 0.95f),
            BossTier = 2,
            AbilityCooldown = 2.2f,
            BossMoves = Moves(
                new BossAttack { Move = BossMove.Riptide, Weight = 1.3f, WindUp = 0.95f, Count = 7, Radius = 1.7f, DamageScale = 0.8f, MaxRange = 16f },
                new BossAttack { Move = BossMove.Maelstrom, Weight = 1.1f, WindUp = 0.8f, Count = 8, Radius = 2.2f, DamageScale = 0.8f, MaxRange = 18f },
                new BossAttack { Move = BossMove.Slam, Weight = 0.8f, WindUp = 1.0f, Radius = 4.2f, Count = 1, Speed = 10f, DamageScale = 0.9f, MaxRange = 8f },
                new BossAttack { Move = BossMove.Summon, Weight = 0.6f, WindUp = 0.8f, Count = 5, SummonId = "squall_gull", MaxRange = 16f },
                new BossAttack { Move = BossMove.RadialBurst, Weight = 1.0f, WindUp = 0.7f, Count = 20, Speed = 7f, DamageScale = 0.35f, MinPhase = 2, MaxRange = 14f }),
            Themes = T("deep", "reef", "boss"),
        },

        // ---- Boss, tier 3. Sinks into the storm and erupts under the player,
        // fans shots, rains lightning; phase two adds riptides and whirlpools.
        new EnemyDef
        {
            Id = "squall_tyrant", DisplayName = "Squall Tyrant",
            Health = 4700f, Damage = 54f, MoveSpeed = 3.3f,
            AttackRange = 3.6f, AttackCooldown = 1.4f,
            Radius = 1.8f, Height = 5.0f, KnockbackResist = 0.98f,
            XpValue = 700, GoldValue = 450,
            Cost = 54, UnlockAtSeconds = 650f, IsBoss = true,
            Tint = new Color(0.55f, 0.50f, 0.95f),
            BossTier = 3,
            AbilityCooldown = 2.0f,
            BossMoves = Moves(
                new BossAttack { Move = BossMove.Surface, Weight = 1.3f, WindUp = 1.2f, Radius = 4.0f, Count = 1, Speed = 10f, DamageScale = 1.0f, MinRange = 5f, MaxRange = 24f },
                new BossAttack { Move = BossMove.Barrage, Weight = 1.0f, WindUp = 0.6f, Count = 3, Speed = 9.5f, DamageScale = 0.4f, MaxRange = 16f },
                new BossAttack { Move = BossMove.MeteorRain, Weight = 1.0f, WindUp = 0.5f, Count = 5, Radius = 2.6f, Speed = 1.3f, DamageScale = 0.8f },
                new BossAttack { Move = BossMove.Summon, Weight = 0.6f, WindUp = 0.8f, Count = 3, SummonId = "mistwalker", MaxRange = 16f },
                new BossAttack { Move = BossMove.Riptide, Weight = 1.1f, WindUp = 0.8f, Count = 8, Radius = 1.9f, DamageScale = 0.8f, MinPhase = 2, MaxRange = 18f },
                new BossAttack { Move = BossMove.Maelstrom, Weight = 1.0f, WindUp = 0.7f, Count = 10, Radius = 2.6f, DamageScale = 0.85f, MinPhase = 2, MaxRange = 20f }),
            Themes = T("storm", "deep", "boss"),
        },
    };
}
