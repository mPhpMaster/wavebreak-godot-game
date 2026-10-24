using System.Collections.Generic;
using Godot;
using Wavebreak.Stats;

namespace Wavebreak.Meta;

/// <summary>
/// The expansion crew: eight more characters, each with its own passive
/// (<see cref="CharacterPassives"/>, Expansion partial), a starting weapon
/// nobody else starts with, a stat trade and a Kenney blocky-character look.
/// Every one is gated by a quest in <see cref="ExpansionQuests"/>, so a fresh
/// profile still sees the original roster first.
///
/// No static fields here: <see cref="Characters.All"/> is built through
/// <see cref="WithExpansion"/> from the original file's initialiser.
/// </summary>
public static partial class Characters
{
    private static IReadOnlyList<CharacterDef> WithExpansion(List<CharacterDef> baseList)
    {
        baseList.AddRange(ExpansionCrew());
        baseList.AddRange(CuteCrew());   // Meta/Characters.Cute.cs
        baseList.AddRange(HeroCrew());   // Meta/Characters.Heroes.cs
        return baseList;
    }

    private static SkinDef Skin(string id, string name, string look, Color tint)
        => new() { Id = id, Name = name, LookId = look, Tint = tint };

    private static SkinDef Skin(string id, string name, string look, Color tint, Visuals.SkinRecolor recolor)
        => new() { Id = id, Name = name, LookId = look, Tint = tint, Recolor = recolor };

    private static List<CharacterDef> ExpansionCrew() => new()
    {
        // Big-game hunter: the bigger the target, the deeper the barb.
        new()
        {
            Id = "harpooner",
            DisplayName = "The Harpooner",
            Blurb = "Followed the whales until the whales started following her. Built for the big ones.",
            StartingWeapon = "brinespike",
            LookId = "blocky-a",
            Tint = new Color(0.30f, 0.55f, 0.75f),
            Passive = PassiveKind.Harpoon,
            PassiveName = "Barbed",
            PassiveText = "Hits on elites and bosses tear for an extra 30% of the damage dealt.",
            Stats = new[]
            {
                new CharacterStat(Stat.Damage, ModifierOp.Additive, 0.10f),
                new CharacterStat(Stat.MaxHealth, ModifierOp.Flat, 10f),
                new CharacterStat(Stat.MoveSpeed, ModifierOp.Additive, -0.05f),
            },
            ExtraSkins = new[] { Skin("harpooner.whaler", "Whaler", "proc-vanguard", new Color(0.55f, 0.40f, 0.30f)),
                Skin("harpooner.blackwater", "Blackwater", "blocky-a", new Color(0.12f, 0.14f, 0.20f), Visuals.SkinRecolor.Dyed(new Color(0.12f, 0.14f, 0.20f), 0.85f, 0.75f, 1.00f)),
                Skin("harpooner.leviathan", "Leviathan", "blocky-a", new Color(0.15f, 0.50f, 0.45f), Visuals.SkinRecolor.Dyed(new Color(0.15f, 0.50f, 0.45f), 0.90f, 1.00f, 1.00f)),
                Skin("harpooner.narwhal", "Narwhal", "blocky-a", new Color(0.85f, 0.88f, 0.95f), Visuals.SkinRecolor.Dyed(new Color(0.85f, 0.88f, 0.95f), 0.70f, 1.15f, 0.60f)),
                Skin("harpooner.icebreaker", "Icebreaker", "blocky-a", new Color(0.55f, 0.85f, 1.00f), Visuals.SkinRecolor.Dyed(new Color(0.55f, 0.85f, 1.00f), 0.85f, 1.00f, 1.00f)),
                Skin("harpooner.bonepicker", "Bonepicker", "blocky-a", new Color(0.85f, 0.78f, 0.60f), Visuals.SkinRecolor.Dyed(new Color(0.85f, 0.78f, 0.60f), 0.80f, 1.00f, 1.00f)) },
        },
        // Sustain through growth: every level is a little light in the dark.
        new()
        {
            Id = "lamplighter",
            DisplayName = "The Lamplighter",
            Blurb = "Kept the harbour lamps lit through every storm. Each lesson learned burns a little brighter.",
            StartingWeapon = "searing_longshot",
            LookId = "blocky-b",
            Tint = new Color(1.00f, 0.85f, 0.45f),
            Passive = PassiveKind.Beacon,
            PassiveName = "Beacon",
            PassiveText = "Every level gained restores 20% of your max health.",
            Stats = new[]
            {
                new CharacterStat(Stat.PickupRange, ModifierOp.Additive, 0.25f),
                new CharacterStat(Stat.HealthRegen, ModifierOp.Flat, 6f),
                new CharacterStat(Stat.MoveSpeed, ModifierOp.Additive, -0.05f),
            },
            ExtraSkins = new[] { Skin("lamplighter.wick", "Wick", "proc-arcanist", new Color(1.00f, 0.60f, 0.25f)),
                Skin("lamplighter.hearth", "Hearthkeeper", "blocky-b", new Color(0.85f, 0.35f, 0.15f), Visuals.SkinRecolor.Dyed(new Color(0.85f, 0.35f, 0.15f), 0.85f, 1.00f, 1.00f)),
                Skin("lamplighter.lighthouse", "Lighthouse Red", "blocky-b", new Color(0.90f, 0.15f, 0.15f), Visuals.SkinRecolor.Dyed(new Color(0.90f, 0.15f, 0.15f), 0.90f, 1.00f, 1.00f)),
                Skin("lamplighter.flare", "Flare", "blocky-b", new Color(1.00f, 0.70f, 0.30f), Visuals.SkinRecolor.Shifted(0.08f, 1.30f, 1.10f)),
                Skin("lamplighter.foglamp", "Foglamp", "blocky-b", new Color(0.70f, 0.75f, 0.80f), Visuals.SkinRecolor.Dyed(new Color(0.70f, 0.75f, 0.80f), 0.80f, 1.00f, 0.60f)),
                Skin("lamplighter.ember", "Ember Watch", "blocky-b", new Color(1.00f, 0.50f, 0.10f), Visuals.SkinRecolor.Dyed(new Color(1.00f, 0.50f, 0.10f), 0.90f, 1.00f, 1.00f)) },
        },
        // Rhythm: the crowd is fuel, every thirtieth kill a crest.
        new()
        {
            Id = "riptide_dancer",
            DisplayName = "Riptide Dancer",
            Blurb = "Learned to dance on a heaving deck. Moves with the swell and strikes on the crest.",
            StartingWeapon = "glasswire_needles",
            LookId = "blocky-c",
            Tint = new Color(0.40f, 0.85f, 0.85f),
            Passive = PassiveKind.Surge,
            PassiveName = "Crest",
            PassiveText = "Every 30 kills, gain +40% attack speed for 4 seconds.",
            Stats = new[]
            {
                new CharacterStat(Stat.AttackSpeed, ModifierOp.Additive, 0.08f),
                new CharacterStat(Stat.MaxHealth, ModifierOp.Flat, -10f),
            },
            ExtraSkins = new[] { Skin("riptide_dancer.spray", "Seaspray", "proc-wildling", new Color(0.75f, 0.95f, 1.00f)),
                Skin("riptide_dancer.coral", "Coral Step", "blocky-c", new Color(1.00f, 0.50f, 0.45f), Visuals.SkinRecolor.Dyed(new Color(1.00f, 0.50f, 0.45f), 0.85f, 1.00f, 1.00f)),
                Skin("riptide_dancer.moonwake", "Moonwake", "blocky-c", new Color(0.55f, 0.55f, 0.90f), Visuals.SkinRecolor.Dyed(new Color(0.55f, 0.55f, 0.90f), 0.85f, 0.85f, 1.00f)),
                Skin("riptide_dancer.glasswing", "Glasswing", "blocky-c", new Color(0.85f, 0.55f, 0.60f), Visuals.SkinRecolor.Shifted(0.50f, 0.90f, 1.10f)),
                Skin("riptide_dancer.undertow", "Undertow", "blocky-c", new Color(0.10f, 0.25f, 0.40f), Visuals.SkinRecolor.Dyed(new Color(0.10f, 0.25f, 0.40f), 0.85f, 0.80f, 1.00f)),
                Skin("riptide_dancer.foamstep", "Foamstep", "blocky-c", new Color(0.90f, 0.97f, 1.00f), Visuals.SkinRecolor.Dyed(new Color(0.90f, 0.97f, 1.00f), 0.75f, 1.20f, 0.50f)) },
        },
        // The slow wall that only gets thicker.
        new()
        {
            Id = "reef_hermit",
            DisplayName = "Reef Hermit",
            Blurb = "Lived so long in a sunken hull that the reef grew over him. Slow to start, impossible to stop.",
            StartingWeapon = "forgefall_maul",
            LookId = "blocky-d",
            Tint = new Color(0.55f, 0.60f, 0.45f),
            Passive = PassiveKind.BarnacleSkin,
            PassiveName = "Barnacle Skin",
            PassiveText = "Gain +1 armour for every minute survived, up to +15.",
            Stats = new[]
            {
                new CharacterStat(Stat.MaxHealth, ModifierOp.Flat, 30f),
                new CharacterStat(Stat.Armor, ModifierOp.Flat, 2f),
                new CharacterStat(Stat.MoveSpeed, ModifierOp.Additive, -0.12f),
            },
            ExtraSkins = new[] { Skin("reef_hermit.coral", "Coralback", "proc-golem", new Color(0.95f, 0.50f, 0.45f)),
                Skin("reef_hermit.kelpbeard", "Kelpbeard", "blocky-d", new Color(0.25f, 0.50f, 0.20f), Visuals.SkinRecolor.Dyed(new Color(0.25f, 0.50f, 0.20f), 0.85f, 1.00f, 1.00f)),
                Skin("reef_hermit.abyssal", "Abyssal", "blocky-d", new Color(0.12f, 0.10f, 0.30f), Visuals.SkinRecolor.Dyed(new Color(0.12f, 0.10f, 0.30f), 0.85f, 0.70f, 1.00f)),
                Skin("reef_hermit.anemone", "Anemone", "blocky-d", new Color(0.95f, 0.40f, 0.75f), Visuals.SkinRecolor.Dyed(new Color(0.95f, 0.40f, 0.75f), 0.85f, 1.00f, 1.00f)),
                Skin("reef_hermit.urchin", "Urchin", "blocky-d", new Color(0.40f, 0.15f, 0.45f), Visuals.SkinRecolor.Dyed(new Color(0.40f, 0.15f, 0.45f), 0.85f, 1.00f, 1.00f)),
                Skin("reef_hermit.driftshell", "Driftshell", "blocky-d", new Color(0.90f, 0.80f, 0.65f), Visuals.SkinRecolor.Dyed(new Color(0.90f, 0.80f, 0.65f), 0.80f, 1.00f, 1.00f)) },
        },
        // Treasure is power: every chest makes the next fight shorter.
        new()
        {
            Id = "wreck_diver",
            DisplayName = "Wreck Diver",
            Blurb = "Brings up what the sea took. Every crate cracked open makes her hit a little harder.",
            StartingWeapon = "mortar_shell",
            LookId = "blocky-e",
            Tint = new Color(0.85f, 0.65f, 0.30f),
            Passive = PassiveKind.Salvage,
            PassiveName = "Salvage",
            PassiveText = "Each chest opened this run grants +3% damage, up to +45%.",
            Stats = new[]
            {
                new CharacterStat(Stat.GoldGain, ModifierOp.Additive, 0.20f),
                new CharacterStat(Stat.Luck, ModifierOp.Flat, 0.5f),
                new CharacterStat(Stat.MaxHealth, ModifierOp.Flat, -5f),
            },
            ExtraSkins = new[] { Skin("wreck_diver.brass", "Brasshelm", "proc-vanguard", new Color(0.80f, 0.60f, 0.25f)),
                Skin("wreck_diver.rust", "Rustbucket", "blocky-e", new Color(0.60f, 0.25f, 0.10f), Visuals.SkinRecolor.Dyed(new Color(0.60f, 0.25f, 0.10f), 0.85f, 1.00f, 1.00f)),
                Skin("wreck_diver.goldleaf", "Goldleaf", "blocky-e", new Color(1.00f, 0.82f, 0.25f), Visuals.SkinRecolor.Dyed(new Color(1.00f, 0.82f, 0.25f), 0.90f, 1.10f, 1.00f)),
                Skin("wreck_diver.anglerlight", "Anglerlight", "blocky-e", new Color(0.15f, 0.35f, 0.50f), Visuals.SkinRecolor.Dyed(new Color(0.15f, 0.35f, 0.50f), 0.85f, 0.80f, 1.00f)),
                Skin("wreck_diver.saltstained", "Saltstained", "blocky-e", new Color(0.85f, 0.85f, 0.80f), Visuals.SkinRecolor.Dyed(new Color(0.85f, 0.85f, 0.80f), 0.70f, 1.10f, 0.50f)),
                Skin("wreck_diver.copperhelm", "Copperhelm", "blocky-e", new Color(0.75f, 0.45f, 0.25f), Visuals.SkinRecolor.Dyed(new Color(0.75f, 0.45f, 0.25f), 0.85f, 1.00f, 1.00f)) },
        },
        // Height is an advantage: crits from the air.
        new()
        {
            Id = "gull_rider",
            DisplayName = "Gull Rider",
            Blurb = "Raised by the cliff gulls, and never quite convinced she was not one. Strikes truest from the air.",
            StartingWeapon = "hailstone_cluster",
            LookId = "blocky-f",
            Tint = new Color(0.95f, 0.95f, 0.90f),
            Passive = PassiveKind.GullsEye,
            PassiveName = "Gull's Eye",
            PassiveText = "While airborne, gain +20% critical hit chance.",
            Stats = new[]
            {
                new CharacterStat(Stat.JumpHeight, ModifierOp.Additive, 0.20f),
                new CharacterStat(Stat.MoveSpeed, ModifierOp.Additive, 0.05f),
                new CharacterStat(Stat.MaxHealth, ModifierOp.Flat, -10f),
            },
            ExtraSkins = new[] { Skin("gull_rider.tern", "Tern", "proc-default", new Color(0.30f, 0.32f, 0.38f)),
                Skin("gull_rider.puffin", "Puffin", "blocky-f", new Color(0.12f, 0.12f, 0.15f), Visuals.SkinRecolor.Dyed(new Color(0.12f, 0.12f, 0.15f), 0.70f, 0.60f, 1.00f)),
                Skin("gull_rider.albatross", "Albatross", "blocky-f", new Color(0.95f, 0.95f, 1.00f), Visuals.SkinRecolor.Dyed(new Color(0.95f, 0.95f, 1.00f), 0.60f, 1.20f, 0.40f)),
                Skin("gull_rider.sleet", "Sleet", "blocky-f", new Color(0.60f, 0.70f, 0.85f), Visuals.SkinRecolor.Dyed(new Color(0.60f, 0.70f, 0.85f), 0.85f, 1.00f, 1.00f)),
                Skin("gull_rider.skua", "Skua", "blocky-f", new Color(0.45f, 0.30f, 0.20f), Visuals.SkinRecolor.Dyed(new Color(0.45f, 0.30f, 0.20f), 0.85f, 0.85f, 1.00f)),
                Skin("gull_rider.cloudrunner", "Cloudrunner", "blocky-f", new Color(0.70f, 0.85f, 1.00f), Visuals.SkinRecolor.Dyed(new Color(0.70f, 0.85f, 1.00f), 0.85f, 1.10f, 1.00f)) },
        },
        // Danger as fuel: strongest when the tide is lowest.
        new()
        {
            Id = "ebb_witch",
            DisplayName = "Ebb Witch",
            Blurb = "Draws her power from the falling tide. The lower the water, the closer she is to drowning you.",
            StartingWeapon = "rimeheart",
            LookId = "blocky-g",
            Tint = new Color(0.35f, 0.45f, 0.70f),
            Passive = PassiveKind.LowTide,
            PassiveName = "Low Tide",
            PassiveText = "Below 40% health, gain +30% damage and +15% move speed.",
            Stats = new[]
            {
                new CharacterStat(Stat.Damage, ModifierOp.Additive, 0.05f),
                new CharacterStat(Stat.Lifesteal, ModifierOp.Flat, 0.02f),
                new CharacterStat(Stat.MaxHealth, ModifierOp.Flat, -5f),
            },
            ExtraSkins = new[] { Skin("ebb_witch.slack", "Slack Water", "proc-arcanist", new Color(0.25f, 0.30f, 0.45f)),
                Skin("ebb_witch.brackish", "Brackish", "blocky-g", new Color(0.35f, 0.40f, 0.25f), Visuals.SkinRecolor.Dyed(new Color(0.35f, 0.40f, 0.25f), 0.85f, 1.00f, 1.00f)),
                Skin("ebb_witch.spring_tide", "Spring Tide", "blocky-g", new Color(0.30f, 0.85f, 0.75f), Visuals.SkinRecolor.Dyed(new Color(0.30f, 0.85f, 0.75f), 0.85f, 1.10f, 1.00f)),
                Skin("ebb_witch.rime", "Rimeveil", "blocky-g", new Color(0.80f, 0.92f, 1.00f), Visuals.SkinRecolor.Dyed(new Color(0.80f, 0.92f, 1.00f), 0.80f, 1.15f, 1.00f)),
                Skin("ebb_witch.neap", "Neap", "blocky-g", new Color(0.30f, 0.20f, 0.45f), Visuals.SkinRecolor.Dyed(new Color(0.30f, 0.20f, 0.45f), 0.85f, 0.80f, 1.00f)),
                Skin("ebb_witch.moonpull", "Moonpull", "blocky-g", new Color(0.85f, 0.85f, 0.60f), Visuals.SkinRecolor.Dyed(new Color(0.85f, 0.85f, 0.60f), 0.80f, 1.00f, 1.00f)) },
        },
        // Hit and run: every dash loads the next blow.
        new()
        {
            Id = "squall_runner",
            DisplayName = "Squall Runner",
            Blurb = "Runs ahead of the weather and lets it catch the ones who stood still.",
            StartingWeapon = "blightwasp_swarm",
            LookId = "blocky-h",
            Tint = new Color(0.60f, 0.55f, 0.85f),
            Passive = PassiveKind.SquallStep,
            PassiveName = "Squall Step",
            PassiveText = "Each dash grants +25% damage for 2.5 seconds.",
            Stats = new[]
            {
                new CharacterStat(Stat.DashCooldown, ModifierOp.Additive, -0.15f),
                new CharacterStat(Stat.MoveSpeed, ModifierOp.Additive, 0.08f),
                new CharacterStat(Stat.Armor, ModifierOp.Flat, -2f),
            },
            ExtraSkins = new[] { Skin("squall_runner.gust", "Gust", "proc-wildling", new Color(0.70f, 0.80f, 0.95f)),
                Skin("squall_runner.thunder", "Thunderclap", "blocky-h", new Color(0.95f, 0.85f, 0.20f), Visuals.SkinRecolor.Dyed(new Color(0.95f, 0.85f, 0.20f), 0.85f, 1.00f, 1.00f)),
                Skin("squall_runner.downpour", "Downpour", "blocky-h", new Color(0.20f, 0.30f, 0.55f), Visuals.SkinRecolor.Dyed(new Color(0.20f, 0.30f, 0.55f), 0.85f, 0.80f, 1.00f)),
                Skin("squall_runner.wasp", "Waspwing", "blocky-h", new Color(0.85f, 0.60f, 0.10f), Visuals.SkinRecolor.Dyed(new Color(0.85f, 0.60f, 0.10f), 0.90f, 0.80f, 1.00f)),
                Skin("squall_runner.hailrunner", "Hailrunner", "blocky-h", new Color(0.85f, 0.90f, 1.00f), Visuals.SkinRecolor.Dyed(new Color(0.85f, 0.90f, 1.00f), 0.75f, 1.15f, 0.60f)),
                Skin("squall_runner.static", "Static", "blocky-h", new Color(0.55f, 0.80f, 0.95f), Visuals.SkinRecolor.Shifted(0.60f, 1.30f, 1.10f)) },
        },
    };
}
