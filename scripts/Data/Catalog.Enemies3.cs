using System.Collections.Generic;
using Godot;

namespace Wavebreak.Data;

/// <summary>
/// The fifth wave: eight small, round, big-eyed tidepool creatures that are
/// lovely to look at and rude to meet. Each brings one new verb rather than new
/// numbers: a pot that is not a pot, bubbles that hold you, a puffer that rolls,
/// a crab that sidles and pinches, jellies strung together by stinging tethers,
/// a snail's slime trail, a lantern-fish that turns the lights down, and the
/// Mother Crab elite who lays more crabs.
///
/// Pacing rule, same as the waves before: nothing here unlocks before 175s, so
/// the opening minutes meet exactly the roster they did before. Costs sit on
/// rungs the ladder already has (3-5, elite 13).
///
/// No static field initialisers here, for the reason given in
/// <c>Catalog.Enemies2.cs</c>.
/// </summary>
public static partial class Catalog
{
    /// <summary>Ids of the cute wave, in unlock order. For the audit and the showcase.</summary>
    public static string[] CuteEnemyIds => new[]
    {
        "sidle_crab", "glimmer_snail", "crock_mimic", "bubblegill",
        "roly_puffer", "linkling", "lanternfin", "mother_crab",
    };

    private static List<EnemyDef> BuildCuteEnemies() => new()
    {
        // ---- Pincher, cost 3, at 175s. Comes at you sideways in a zig-zag
        // (hard to line up a straight shot), then clacks a claw at the space in
        // front of it: a small disc that fills for half a second. Caught, you
        // walk 45% slower for a moment.
        new EnemyDef
        {
            Id = "sidle_crab", DisplayName = "Sidle Crab",
            Health = 22f, Damage = 7f, MoveSpeed = 3.7f,
            AttackRange = 1.3f, AttackCooldown = 1.0f,
            Radius = 0.42f, Height = 0.75f,
            XpValue = 3, GoldValue = 1,
            Cost = 3, UnlockAtSeconds = 175f,
            Tint = new Color(1.00f, 0.55f, 0.48f),
            Behavior = EnemyBehavior.Pincher,
            AbilityRange = 2.6f, AbilityCooldown = 3.2f, WindUp = 0.5f,
            AbilityRadius = 1.1f, AbilityDamage = 1.2f, HoldSeconds = 1.3f,
            Themes = T("shore", "reef", "crab", "swarm"),
        },

        // ---- Trail, cost 3, at 195s. A slow, sturdy snail whose slime puddles
        // take 40% off the player's speed for as long as they stand in one.
        // Alone it is a nuisance; with a crowd behind it, it is a wall.
        new EnemyDef
        {
            Id = "glimmer_snail", DisplayName = "Glimmer Snail",
            Health = 48f, Damage = 8f, MoveSpeed = 2.6f,
            AttackRange = 1.4f, AttackCooldown = 1.2f,
            Radius = 0.5f, Height = 0.95f, KnockbackResist = 0.3f,
            XpValue = 4, GoldValue = 3,
            Cost = 3, UnlockAtSeconds = 195f,
            Tint = new Color(0.62f, 0.92f, 0.78f),
            TrailSlow = 0.4f,
            Themes = T("reef", "meadow", "shore", "trail"),
        },

        // ---- Mimic, cost 4, at 215s. Arrives as an ordinary pot and sits
        // still. Its eyes peek out now and then (the tell). Come within 4.5m, or
        // hit it, and it springs: a short lane, then a 14 m/s lunge. Pays out
        // extra gold, like a pot should. Gets bored after 10s and waddles over.
        new EnemyDef
        {
            Id = "crock_mimic", DisplayName = "Crock Mimic",
            Health = 45f, Damage = 11f, MoveSpeed = 4.4f,
            AttackRange = 1.4f, AttackCooldown = 1.0f,
            Radius = 0.45f, Height = 0.95f, KnockbackResist = 0.2f,
            XpValue = 6, GoldValue = 6,
            Cost = 4, UnlockAtSeconds = 215f,
            Tint = new Color(0.86f, 0.62f, 0.46f),
            Behavior = EnemyBehavior.Mimic,
            AbilityRange = 4.5f, AbilityCooldown = 4f, WindUp = 0.45f,
            AbilitySpeed = 14f, AbilityDamage = 1.3f, AbilityRadius = 0.7f,
            Themes = T("shore", "ash", "meadow", "ambush"),
        },

        // ---- Bubbler, cost 4, at 235s. Holds 8m and blows a fan of three slow
        // bubbles (4.2 m/s). One that touches you holds you in place for a
        // second; a dash pops it, and a dash through it never gets caught.
        new EnemyDef
        {
            Id = "bubblegill", DisplayName = "Bubblegill",
            Health = 30f, Damage = 6f, MoveSpeed = 2.8f,
            AttackRange = 1.4f, AttackCooldown = 1.2f,
            Radius = 0.45f, Height = 1.0f,
            XpValue = 4, GoldValue = 2,
            Cost = 4, UnlockAtSeconds = 235f,
            Tint = new Color(0.45f, 0.85f, 0.95f),
            Behavior = EnemyBehavior.Bubbler,
            AbilityRange = 8f, AbilityCooldown = 3.8f, WindUp = 0.7f,
            AbilitySpeed = 4.2f, AbilityCount = 3, AbilitySpread = 40f, AbilityDamage = 0.6f,
            HoldSeconds = 1.0f,
            Themes = T("reef", "deep", "ranged", "bubble"),
        },

        // ---- Puffer, cost 5, at 255s. Swells up over 0.85s (and takes 60%
        // less damage while puffed), then rolls down a painted lane at 13 m/s.
        // Deflated after the roll: that is the moment to hit it.
        new EnemyDef
        {
            Id = "roly_puffer", DisplayName = "Roly Puffer",
            Health = 40f, Damage = 9f, MoveSpeed = 3.0f,
            AttackRange = 1.4f, AttackCooldown = 1.1f,
            Radius = 0.5f, Height = 0.9f, KnockbackResist = 0.2f,
            XpValue = 5, GoldValue = 3,
            Cost = 5, UnlockAtSeconds = 255f,
            Tint = new Color(1.00f, 0.84f, 0.50f),
            Behavior = EnemyBehavior.Puffer,
            AbilityRange = 10f, AbilityCooldown = 4.5f, WindUp = 0.85f,
            AbilitySpeed = 13f, AbilityDamage = 1.4f, AbilityRadius = 0.8f,
            Themes = T("reef", "shore", "charger", "roller"),
        },

        // ---- Tether, cost 3, at 300s. Bobs along at knee height and links
        // itself to a nearby ally (another Linkling first) with a stinging
        // tether drawn on the floor. Crossing it stings; jumping it is free.
        new EnemyDef
        {
            Id = "linkling", DisplayName = "Linkling",
            Health = 26f, Damage = 6f, MoveSpeed = 3.2f,
            AttackRange = 1.4f, AttackCooldown = 1.0f,
            Radius = 0.4f, Height = 0.9f,
            XpValue = 4, GoldValue = 2,
            Cost = 3, UnlockAtSeconds = 300f,
            Tint = new Color(0.96f, 0.62f, 0.95f),
            Hover = 1.0f,
            TetherLength = 8f, TetherDamage = 0.8f,
            Themes = T("deep", "reef", "flyer", "swarm", "tether"),
        },

        // ---- Gloom, cost 5, at 330s. A little angler that turns the lights
        // down: within 16m the view darkens toward the edges. Its own lure
        // shines through the dark, so you can always find the switch.
        new EnemyDef
        {
            Id = "lanternfin", DisplayName = "Lanternfin",
            Health = 34f, Damage = 8f, MoveSpeed = 3.4f,
            AttackRange = 1.5f, AttackCooldown = 1.1f,
            Radius = 0.45f, Height = 1.0f,
            XpValue = 5, GoldValue = 3,
            Cost = 5, UnlockAtSeconds = 330f,
            Tint = new Color(0.38f, 0.48f, 0.85f),
            Hover = 1.6f,
            GloomRadius = 16f,
            Themes = T("deep", "storm", "flyer", "gloom"),
        },

        // ---- Elite, cost 13, at 420s. The mini-boss of the wave: a big
        // Pincher whose claws guard her front (60% blocked), who lays two Sidle
        // Crabs every 9s and leaves three more behind when she falls.
        new EnemyDef
        {
            Id = "mother_crab", DisplayName = "Mother Crab",
            Health = 420f, Damage = 22f, MoveSpeed = 3.0f,
            AttackRange = 2.0f, AttackCooldown = 1.3f,
            Radius = 0.95f, Height = 1.7f, KnockbackResist = 0.85f,
            XpValue = 55, GoldValue = 35,
            Cost = 13, UnlockAtSeconds = 420f, IsElite = true,
            Tint = new Color(0.98f, 0.46f, 0.52f),
            Behavior = EnemyBehavior.Pincher,
            AbilityRange = 3.6f, AbilityCooldown = 3.0f, WindUp = 0.65f,
            AbilityRadius = 1.8f, AbilityDamage = 1.3f, HoldSeconds = 1.5f,
            ShieldArc = 100f, ShieldBlock = 0.6f,
            BroodId = "sidle_crab", BroodCount = 2, BroodEvery = 9f,
            SplitInto = "sidle_crab", SplitCount = 3,
            Themes = T("shore", "reef", "crab", "elite", "brood"),
        },
    };
}
