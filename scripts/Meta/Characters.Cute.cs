using System.Collections.Generic;
using Godot;
using Wavebreak.Stats;
using Wavebreak.Visuals;

namespace Wavebreak.Meta;

/// <summary>
/// The cute crew: six small sea-folk drawn from the Kenney cube pets (an otter
/// tinker, a lighthouse chick, a sea-witch apprentice, a penguin courier, a
/// coral gardener and a pirate cat). Each has its own passive
/// (<see cref="CharacterPassives"/>, Cute partial), a starting weapon nobody
/// else starts with, a stat trade, three skins (base plus two quest skins) and
/// an unlock quest in <see cref="CuteQuests"/>.
///
/// Also carries the pastel "festival" skins added to three of the original
/// characters (<see cref="BonusSkinsFor"/>, read by <see cref="CharacterDef"/>
/// when it builds its skin list).
///
/// No static fields: <see cref="Characters.All"/> is built from the original
/// file's initialiser, whose order against fields in other partial files is
/// not defined.
/// </summary>
public static partial class Characters
{
    /// <summary>Ids of the cute crew, in roster order.</summary>
    public static readonly string[] CuteIds = { "pip", "lumi", "tilly", "pebble", "rosie", "whiskers" };

    /// <summary>True for the cute crew. They carry three skins each (base and two
    /// earned), which the content audit allows for them.</summary>
    public static bool IsCuteCrew(string id) => System.Array.IndexOf(CuteIds, id) >= 0;

    /// <summary>A soft repaint for the cube-pet fur. The pets' fur is a warm hue
    /// that the skin-tone guard would otherwise protect, so it is mostly off.</summary>
    private static SkinRecolor Pastel(Color dye, float amount = 0.80f, float value = 1.05f, float saturation = 0.85f)
        => new(0f, saturation, value, dye, amount, 0.10f);

    private static SkinDef CuteSkin(string id, string name, string look, Color dye, float amount = 0.80f, float value = 1.05f, float saturation = 0.85f)
        => new() { Id = id, Name = name, LookId = look, Tint = dye, Recolor = Pastel(dye, amount, value, saturation) };

    /// <summary>Extra skins for characters defined elsewhere (the original
    /// roster), appended after their own.</summary>
    internal static IReadOnlyList<SkinDef> BonusSkinsFor(string characterId) => characterId switch
    {
        "drifter" => new[] { new SkinDef { Id = "drifter.candyfloss", Name = "Candyfloss", LookId = "default", Tint = new Color(1.00f, 0.72f, 0.86f),
                                           Recolor = SkinRecolor.Dyed(new Color(1.00f, 0.72f, 0.86f), 0.80f, 1.10f, 0.75f) } },
        "gale_courier" => new[] { new SkinDef { Id = "gale_courier.festival", Name = "Kite Festival", LookId = "gale", Tint = new Color(0.62f, 0.88f, 1.00f),
                                                Recolor = SkinRecolor.Dyed(new Color(0.62f, 0.88f, 1.00f), 0.80f, 1.10f, 0.80f) } },
        "kiln_sister" => new[] { new SkinDef { Id = "kiln_sister.marshmallow", Name = "Toasted Marshmallow", LookId = "ember", Tint = new Color(1.00f, 0.90f, 0.78f),
                                               Recolor = SkinRecolor.Dyed(new Color(1.00f, 0.86f, 0.72f), 0.75f, 1.15f, 0.55f) } },
        _ => System.Array.Empty<SkinDef>(),
    };

    private static List<CharacterDef> CuteCrew() => new()
    {
        // Builds little helpers: a turret popped down every few seconds.
        new()
        {
            Id = "pip",
            DisplayName = "Pip the Tinker",
            Blurb = "An otter with a toolbelt and a pocketful of springs. Anything left lying on the beach becomes a turret by teatime.",
            StartingWeapon = "barnacle_mines",
            LookId = "pet-otter",
            Tint = new Color(0.72f, 0.50f, 0.32f),
            Passive = PassiveKind.PocketTurret,
            PassiveName = "Pocket Turret",
            PassiveText = "Every 10 seconds, sets down a little turret that plinks the nearest enemy for 8 seconds.",
            Stats = new[]
            {
                new CharacterStat(Stat.Duration, ModifierOp.Additive, 0.15f),
                new CharacterStat(Stat.AreaSize, ModifierOp.Additive, 0.05f),
                new CharacterStat(Stat.MoveSpeed, ModifierOp.Additive, -0.05f),
            },
            ExtraSkins = new[]
            {
                CuteSkin("pip.mint", "Mint Overalls", "pet-otter", new Color(0.60f, 0.95f, 0.80f)),
                CuteSkin("pip.caramel", "Caramel Swirl", "pet-otter", new Color(1.00f, 0.78f, 0.45f), 0.70f, 1.15f, 0.90f),
            },
        },
        // The lighthouse keeper's kid: a warm little glow that singes the dark.
        new()
        {
            Id = "lumi",
            DisplayName = "Lumi",
            Blurb = "The lighthouse keeper's chick, up long past bedtime. Carries a lantern bigger than herself and is not scared of anything. Mostly.",
            StartingWeapon = "lighthouse_beam",
            LookId = "pet-chick",
            Tint = new Color(1.00f, 0.86f, 0.35f),
            Passive = PassiveKind.NightLight,
            PassiveName = "Night Light",
            PassiveText = "A warm glow surrounds you: enemies within 4 m are singed twice a second, harder as you level.",
            Stats = new[]
            {
                new CharacterStat(Stat.PickupRange, ModifierOp.Additive, 0.30f),
                new CharacterStat(Stat.XpGain, ModifierOp.Additive, 0.10f),
                new CharacterStat(Stat.MaxHealth, ModifierOp.Flat, -10f),
            },
            ExtraSkins = new[]
            {
                CuteSkin("lumi.peach", "Peach Glow", "pet-chick", new Color(1.00f, 0.68f, 0.58f)),
                CuteSkin("lumi.sky", "Sky Lantern", "pet-chick", new Color(0.62f, 0.82f, 1.00f), 0.85f, 1.10f, 0.80f),
            },
        },
        // The sea-witch apprentice: bubbles that pop when something gets too close.
        new()
        {
            Id = "tilly",
            DisplayName = "Tilly the Apprentice",
            Blurb = "A sea-witch apprentice who has mastered exactly one spell, and it is bubbles. Very good bubbles, though.",
            StartingWeapon = "circling_shoal",
            LookId = "pet-polar",
            Tint = new Color(0.82f, 0.74f, 1.00f),
            Passive = PassiveKind.BubbleCharm,
            PassiveName = "Bubble Charm",
            PassiveText = "Keeps up to 3 bubbles, one regrown every 9 s. When hit, a bubble pops: 1.5 s of safety and nearby enemies are chilled.",
            Stats = new[]
            {
                new CharacterStat(Stat.StatusChance, ModifierOp.Flat, 0.05f),
                new CharacterStat(Stat.AreaSize, ModifierOp.Additive, 0.08f),
                new CharacterStat(Stat.Armor, ModifierOp.Flat, -1f),
            },
            ExtraSkins = new[]
            {
                CuteSkin("tilly.rose", "Rose Quartz", "pet-polar", new Color(1.00f, 0.70f, 0.82f)),
                CuteSkin("tilly.moonbeam", "Moonbeam", "pet-polar", new Color(1.00f, 0.95f, 0.65f), 0.75f, 1.10f, 0.80f),
            },
        },
        // The penguin courier: dashes are belly slides that leave a frosty wake.
        new()
        {
            Id = "pebble",
            DisplayName = "Pebble the Courier",
            Blurb = "Delivers parcels across the ice floes on her tummy. Never late, always a little bit cold.",
            StartingWeapon = "sleetbreath_horn",
            LookId = "pet-penguin",
            Tint = new Color(0.25f, 0.30f, 0.40f),
            Passive = PassiveKind.BellySlide,
            PassiveName = "Belly Slide",
            PassiveText = "Each dash chills enemies within 4.5 m and gives +20% move speed for 2 seconds.",
            Stats = new[]
            {
                new CharacterStat(Stat.MoveSpeed, ModifierOp.Additive, 0.10f),
                new CharacterStat(Stat.DashCooldown, ModifierOp.Additive, -0.15f),
                new CharacterStat(Stat.MaxHealth, ModifierOp.Flat, -5f),
            },
            ExtraSkins = new[]
            {
                CuteSkin("pebble.blush", "Blush", "pet-penguin", new Color(0.98f, 0.60f, 0.75f), 0.80f, 1.45f, 0.85f),
                CuteSkin("pebble.icicle", "Icicle", "pet-penguin", new Color(0.55f, 0.82f, 1.00f), 0.80f, 1.20f, 0.85f),
            },
        },
        // The coral gardener: blooms that mend her and sting whatever is close.
        new()
        {
            Id = "rosie",
            DisplayName = "Rosie the Gardener",
            Blurb = "Tends a coral garden in the shallows and talks to every polyp by name. The garden, it turns out, talks back.",
            StartingWeapon = "bloomcap_spores",
            LookId = "pet-pig",
            Tint = new Color(1.00f, 0.66f, 0.72f),
            Passive = PassiveKind.CoralBloom,
            PassiveName = "Coral Bloom",
            PassiveText = "Every 7 seconds, blooms: heals 3% of max health and poisons enemies within 5 m.",
            Stats = new[]
            {
                new CharacterStat(Stat.HealthRegen, ModifierOp.Flat, 10f),
                new CharacterStat(Stat.MaxHealth, ModifierOp.Flat, 15f),
                new CharacterStat(Stat.AttackSpeed, ModifierOp.Additive, -0.05f),
            },
            ExtraSkins = new[]
            {
                CuteSkin("rosie.lagoon", "Lagoon", "pet-pig", new Color(0.45f, 0.88f, 0.85f)),
                CuteSkin("rosie.lavender", "Lavender", "pet-pig", new Color(0.75f, 0.62f, 0.98f)),
            },
        },
        // The pirate cat: lands on her feet, nine times.
        new()
        {
            Id = "whiskers",
            DisplayName = "Captain Whiskers",
            Blurb = "Captain of a very small ship with a very large hat. Has fallen overboard eight times and still has opinions about it.",
            StartingWeapon = "skimstone_sling",
            LookId = "pet-cat",
            Tint = new Color(0.55f, 0.55f, 0.62f),
            Passive = PassiveKind.NineLives,
            PassiveName = "Nine Lives",
            PassiveText = "Dropping below 30% health heals 20% and grants 2.5 s of safety. 9 lives per run, at most one every 20 seconds.",
            Stats = new[]
            {
                new CharacterStat(Stat.CritChance, ModifierOp.Flat, 0.08f),
                new CharacterStat(Stat.Luck, ModifierOp.Flat, 0.5f),
                new CharacterStat(Stat.MaxHealth, ModifierOp.Flat, -10f),
            },
            ExtraSkins = new[]
            {
                CuteSkin("whiskers.calico", "Calico", "pet-cat", new Color(1.00f, 0.66f, 0.38f), 0.80f, 1.20f, 0.90f),
                CuteSkin("whiskers.midnight", "Midnight Mew", "pet-cat", new Color(0.40f, 0.42f, 0.78f), 0.80f, 0.90f, 0.90f),
            },
        },
    };
}
