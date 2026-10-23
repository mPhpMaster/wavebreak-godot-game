using System.Collections.Generic;
using Godot;
using Wavebreak.Data;
using Wavebreak.Stats;

namespace Wavebreak.Meta;

/// <summary>Which unique rule a character plays with. Behaviour lives in
/// <see cref="CharacterPassives"/>, keyed by this.</summary>
public enum PassiveKind { None, Momentum, Kindling, Tailwind, GildedFortune, Bulwark, Ambush, Overcharge,
    // expansion (Meta/CharacterPassives.Expansion.cs)
    Harpoon, Beacon, Surge, BarnacleSkin, Salvage, GullsEye, LowTide, SquallStep }

/// <summary>One stat tweak a character starts with. Same shape as an item
/// effect, applied once at run start rather than per stack.</summary>
public readonly record struct CharacterStat(Stat Stat, ModifierOp Op, float Value);

/// <summary>
/// One look for a character. Every character has a free base skin (first in
/// its list) and more that achievements unlock; a skin is cosmetic only, so it
/// can be the reward for anything without touching balance.
/// <see cref="Id"/> is "characterId.skin" so the unlock table can name it.
/// </summary>
public sealed class SkinDef
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    /// <summary>Model look id for the visuals library.</summary>
    public required string LookId { get; init; }
    /// <summary>Body colour, also used wherever a model is not available yet.</summary>
    public required Color Tint { get; init; }

    /// <summary>Explicit repaint of the model's own colours. Null means
    /// "derive one from <see cref="Tint"/>" (see <see cref="Paint"/>).</summary>
    public Visuals.SkinRecolor? Recolor { get; init; }

    /// <summary>
    /// What the model is actually repainted with. A base skin is the model as
    /// authored; any other skin without an explicit <see cref="Recolor"/> takes
    /// a moderate dye of its <see cref="Tint"/>, so even the older skins that
    /// only named a tint now look the colour they claim. (Imported models
    /// ignore a tint on its own: their colours live in a texture.)
    /// </summary>
    public Visuals.SkinRecolor Paint => Recolor ?? (Id.EndsWith(".base")
        ? Visuals.SkinRecolor.None
        : Visuals.SkinRecolor.Dyed(Tint, 0.45f));

    public bool IsAvailable => Unlocks.IsContentUnlocked(Id);
}

/// <summary>
/// A playable character, as data: a name, a starting weapon, and a handful of
/// stat modifiers applied through the normal <see cref="StatSheet"/> pipeline
/// with the definition itself as the source tag. Nothing about a character is
/// special-cased in the run — which is the point: a character is "an item you
/// cannot sell, held from second zero".
/// </summary>
public sealed class CharacterDef
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string Blurb { get; init; }

    /// <summary>Weapon id handed out at run start. Looked up in the base catalogue
    /// first, then the meta content.</summary>
    public required string StartingWeapon { get; init; }

    public IReadOnlyList<CharacterStat> Stats { get; init; } = System.Array.Empty<CharacterStat>();

    /// <summary>Which player model to wear. Resolved by the visuals library
    /// (scripts/Visuals) at merge time; this layer only carries the id.</summary>
    public string LookId { get; init; } = "default";

    public Color Tint { get; init; } = new(0.6f, 0.85f, 1f);

    /// <summary>The character's unique rule, beyond its stats. Implemented in
    /// <see cref="CharacterPassives"/>; <see cref="PassiveKind.None"/> plays
    /// exactly like the stats alone.</summary>
    public PassiveKind Passive { get; init; } = PassiveKind.None;

    /// <summary>Short name of the passive, for the character page.</summary>
    public string PassiveName { get; init; } = "";

    /// <summary>One plain sentence saying what the passive does.</summary>
    public string PassiveText { get; init; } = "";

    /// <summary>Base skin first. Built from <see cref="LookId"/>/<see cref="Tint"/>
    /// plus <see cref="ExtraSkins"/>, so a character that defines none still has one.</summary>
    public IReadOnlyList<SkinDef> Skins => _skins ??= BuildSkins();
    private IReadOnlyList<SkinDef>? _skins;

    public IReadOnlyList<SkinDef> ExtraSkins { get; init; } = System.Array.Empty<SkinDef>();

    private IReadOnlyList<SkinDef> BuildSkins()
    {
        var list = new List<SkinDef> { new() { Id = Id + ".base", Name = "Original", LookId = LookId, Tint = Tint } };
        list.AddRange(ExtraSkins);
        return list;
    }

    /// <summary>The skin this character will wear: the saved choice if it is
    /// still unlocked, else the base skin.</summary>
    public SkinDef SelectedSkin
    {
        get
        {
            string? id = Profile.SelectedSkin(Id);
            foreach (SkinDef s in Skins) if (s.Id == id && s.IsAvailable) return s;
            return Skins[0];
        }
    }

    /// <summary>The unlock that grants this character, or null if it is free.</summary>
    public string? UnlockedBy => Unlocks.SourceOf(Id);

    public bool IsAvailable => UnlockedBy is null || Profile.IsUnlocked(UnlockedBy);

    public WeaponDef StartingWeaponDef
        => Catalog.WeaponById(StartingWeapon) ?? MetaContent.WeaponById(StartingWeapon) ?? Catalog.Weapons[0];

    /// <summary>Apply this character's modifiers. Tagged with the def, so
    /// <c>RemoveAllFrom(def)</c> would undo exactly this and nothing else.</summary>
    public void ApplyTo(StatSheet sheet)
    {
        foreach (CharacterStat s in Stats) sheet.Add(s.Stat, s.Op, s.Value, this);
    }
}

/// <summary>
/// The roster. The default character is exactly the pre-meta game — no
/// modifiers, the catalogue's starting weapon — so a fresh profile plays as it
/// always has, and every automated run (which never picks one) is unchanged.
/// </summary>
public static partial class Characters
{
    public const string DefaultId = "drifter";

    public static readonly IReadOnlyList<CharacterDef> All = WithExpansion(new List<CharacterDef>
    {
        new()
        {
            Id = DefaultId,
            DisplayName = "The Drifter",
            Blurb = "Washed up with a rail shard and nothing to lose. Balanced in every way that matters.",
            StartingWeapon = "splinter_rail",
            LookId = "default",
            Tint = new Color(0.62f, 0.86f, 1.00f),
            ExtraSkins = new SkinDef[]
            {
                new() { Id = "drifter.tidecaller", Name = "Tidecaller", LookId = "default_tidecaller", Tint = new Color(0.25f, 0.55f, 0.95f) },
                new() { Id = "drifter.wavecutter", Name = "Wavecutter", LookId = "scout", Tint = new Color(0.30f, 0.80f, 0.85f) },
                new() { Id = "drifter.driftwood", Name = "Driftwood", LookId = "proc-default", Tint = new Color(0.62f, 0.50f, 0.36f) },
                new() { Id = "drifter.saltrunner", Name = "Saltrunner", LookId = "rogue", Tint = new Color(0.85f, 0.88f, 0.92f) },
                new() { Id = "drifter.lighthouse", Name = "Lighthouse Keeper", LookId = "vanguard", Tint = new Color(1.00f, 0.92f, 0.55f) },
                new() { Id = "drifter.foam", Name = "Seafoam", LookId = "wildling", Tint = new Color(0.70f, 0.95f, 0.90f) },
                new() { Id = "drifter.stormglass", Name = "Stormglass", LookId = "default", Tint = new Color(0.30f, 0.38f, 0.62f),
                        Recolor = Visuals.SkinRecolor.Dyed(new Color(0.30f, 0.38f, 0.62f), 0.85f, 0.85f, 1.00f) },
            },
            Passive = PassiveKind.Momentum,
            PassiveName = "Momentum",
            PassiveText = "Moving faster than a walk builds up to +30% damage. The bonus fades slowly once you stop, so keep sliding and dashing.",
        },
        // Slow and hard to kill: trades the movement layer for the health layer,
        // and starts with the weapon that rewards standing your ground.
        new()
        {
            Id = "kiln_sister",
            DisplayName = "Kiln Sister",
            Blurb = "Tended the furnaces until the sea put them out. Heavier, slower, and very difficult to put out herself.",
            StartingWeapon = "ember_halo",
            LookId = "ember",
            Tint = new Color(1.00f, 0.56f, 0.30f),
            ExtraSkins = new SkinDef[]
            {
                new() { Id = "kiln_sister.forgeborn", Name = "Forgeborn", LookId = "ember_forgeborn", Tint = new Color(0.85f, 0.22f, 0.15f) },
                new() { Id = "kiln_sister.ashmother", Name = "Ashmother", LookId = "medic", Tint = new Color(0.55f, 0.52f, 0.50f) },
                new() { Id = "kiln_sister.slag", Name = "Slagheart", LookId = "golem", Tint = new Color(0.45f, 0.30f, 0.25f) },
                new() { Id = "kiln_sister.cinder", Name = "Cinderwork", LookId = "proc-golem", Tint = new Color(1.00f, 0.40f, 0.10f) },
                new() { Id = "kiln_sister.lamplighter", Name = "Lamplighter", LookId = "scout", Tint = new Color(1.00f, 0.80f, 0.45f) },
                new() { Id = "kiln_sister.glassblower", Name = "Glassblower", LookId = "arcanist", Tint = new Color(0.55f, 0.85f, 0.95f) },
                new() { Id = "kiln_sister.blackglass", Name = "Blackglass", LookId = "ember", Tint = new Color(0.20f, 0.16f, 0.25f),
                        Recolor = Visuals.SkinRecolor.Dyed(new Color(0.20f, 0.16f, 0.25f), 0.80f, 0.70f, 1.00f) },
            },
            Passive = PassiveKind.Kindling,
            PassiveName = "Kindling",
            PassiveText = "Weapon hits can set enemies alight: they burn for a quarter of the hit every second, for 3 seconds.",
            Stats = new[]
            {
                new CharacterStat(Stat.MaxHealth, ModifierOp.Flat, 25f),
                new CharacterStat(Stat.Armor, ModifierOp.Flat, 5f),
                new CharacterStat(Stat.MoveSpeed, ModifierOp.Additive, -0.08f),
            },
        },
        // The opposite trade: fragile, fast, dashes often. Starts with the
        // spread weapon because a courier is always in the middle of things.
        new()
        {
            Id = "gale_courier",
            DisplayName = "Gale Courier",
            Blurb = "Never delivered a message late, never stayed to hear the reply. Fast and paper-thin.",
            StartingWeapon = "sparkfan",
            LookId = "gale",
            Tint = new Color(0.70f, 1.00f, 0.80f),
            ExtraSkins = new SkinDef[]
            {
                new() { Id = "gale_courier.stormchaser", Name = "Stormchaser", LookId = "gale_stormchaser", Tint = new Color(0.55f, 0.50f, 1.00f) },
                new() { Id = "gale_courier.zephyr", Name = "Zephyr", LookId = "scout", Tint = new Color(0.80f, 1.00f, 0.95f) },
                new() { Id = "gale_courier.kite", Name = "Kitetail", LookId = "wildling", Tint = new Color(1.00f, 0.60f, 0.35f) },
                new() { Id = "gale_courier.postrider", Name = "Postrider", LookId = "rogue", Tint = new Color(0.40f, 0.55f, 0.80f) },
                new() { Id = "gale_courier.squall", Name = "Squall", LookId = "proc-wildling", Tint = new Color(0.50f, 0.60f, 0.70f) },
                new() { Id = "gale_courier.dawn", Name = "Dawnrunner", LookId = "medic", Tint = new Color(1.00f, 0.75f, 0.70f) },
                new() { Id = "gale_courier.nightpost", Name = "Night Post", LookId = "gale", Tint = new Color(0.20f, 0.25f, 0.45f),
                        Recolor = Visuals.SkinRecolor.Dyed(new Color(0.20f, 0.25f, 0.45f), 0.85f, 0.80f, 1.00f) },
            },
            Passive = PassiveKind.Tailwind,
            PassiveName = "Tailwind",
            PassiveText = "One extra air jump. Every kill takes 0.3 s off the dash cooldown.",
            Stats = new[]
            {
                new CharacterStat(Stat.MoveSpeed, ModifierOp.Additive, 0.15f),
                new CharacterStat(Stat.DashCooldown, ModifierOp.Additive, -0.20f),
                new CharacterStat(Stat.MaxHealth, ModifierOp.Flat, -20f),
            },
        },
        // The secret one. Luck and learning at the cost of a little bite,
        // with the homing weapon that does not need a steady hand.
        new()
        {
            Id = "gilded_augur",
            DisplayName = "Gilded Augur",
            Blurb = "Reads the shrines nobody else can find. Fortune listens to her; her blows land a little softer.",
            StartingWeapon = "wisp_seeker",
            LookId = "augur",
            Tint = new Color(1.00f, 0.86f, 0.42f),
            ExtraSkins = new SkinDef[]
            {
                new() { Id = "gilded_augur.eclipse", Name = "Eclipse", LookId = "augur_eclipse", Tint = new Color(0.35f, 0.30f, 0.45f) },
                new() { Id = "gilded_augur.oracle", Name = "Oracle", LookId = "arcanist", Tint = new Color(0.95f, 0.80f, 0.40f) },
                new() { Id = "gilded_augur.prophet", Name = "Prophet of Salt", LookId = "proc-arcanist", Tint = new Color(0.90f, 0.90f, 0.80f) },
                new() { Id = "gilded_augur.seer", Name = "Tideseer", LookId = "medic", Tint = new Color(0.60f, 0.80f, 1.00f) },
                new() { Id = "gilded_augur.gilt", Name = "All That Glitters", LookId = "golem", Tint = new Color(1.00f, 0.80f, 0.20f) },
                new() { Id = "gilded_augur.moth", Name = "Mothlight", LookId = "default_tidecaller", Tint = new Color(0.80f, 0.75f, 0.55f) },
                new() { Id = "gilded_augur.verdigris", Name = "Verdigris", LookId = "augur", Tint = new Color(0.35f, 0.75f, 0.60f),
                        Recolor = Visuals.SkinRecolor.Dyed(new Color(0.35f, 0.75f, 0.60f), 0.85f, 1.00f, 1.00f) },
            },
            Passive = PassiveKind.GildedFortune,
            PassiveName = "Gilded Fortune",
            PassiveText = "Gold held turns into damage: +1% per 10 gold, up to +40% at 400 gold.",
            Stats = new[]
            {
                new CharacterStat(Stat.Luck, ModifierOp.Flat, 1f),
                new CharacterStat(Stat.XpGain, ModifierOp.Additive, 0.15f),
                new CharacterStat(Stat.Damage, ModifierOp.Additive, -0.10f),
            },
        },
        // Earned by beating the second phase boss. The anchor of the roster:
        // slow, armoured, and angrier every time something lands a hit.
        new()
        {
            Id = "breakwater_warden",
            DisplayName = "Breakwater Warden",
            Blurb = "Kept the harbour wall for thirty winters. The sea has not got past her yet, and it has stopped asking politely.",
            StartingWeapon = "cleaver_arc",
            Passive = PassiveKind.Bulwark,
            PassiveName = "Retaliation",
            PassiveText = "When hit, gain +35% damage and thorns equal to your armour for 4 seconds.",
            LookId = "vanguard",
            Tint = new Color(0.72f, 0.74f, 0.80f),
            ExtraSkins = new SkinDef[]
            {
                new() { Id = "breakwater_warden.bastion", Name = "Bastion", LookId = "proc-vanguard", Tint = new Color(0.60f, 0.62f, 0.70f) },
                new() { Id = "breakwater_warden.rustplate", Name = "Rustplate", LookId = "golem", Tint = new Color(0.70f, 0.40f, 0.25f) },
                new() { Id = "breakwater_warden.harbor", Name = "Harbourmaster", LookId = "default", Tint = new Color(0.30f, 0.45f, 0.70f) },
                new() { Id = "breakwater_warden.ironwake", Name = "Ironwake", LookId = "augur_eclipse", Tint = new Color(0.30f, 0.32f, 0.40f) },
                new() { Id = "breakwater_warden.seawall", Name = "Seawall", LookId = "ember_forgeborn", Tint = new Color(0.50f, 0.60f, 0.65f) },
                new() { Id = "breakwater_warden.tidewall", Name = "Tidewall", LookId = "proc-golem", Tint = new Color(0.35f, 0.55f, 0.65f) },
                new() { Id = "breakwater_warden.deepwatch", Name = "Deepwatch", LookId = "vanguard", Tint = new Color(0.15f, 0.35f, 0.45f),
                        Recolor = Visuals.SkinRecolor.Dyed(new Color(0.15f, 0.35f, 0.45f), 0.80f, 1.00f, 1.00f) },
            },
            Stats = new[]
            {
                new CharacterStat(Stat.Armor, ModifierOp.Flat, 8f),
                new CharacterStat(Stat.MaxHealth, ModifierOp.Flat, 15f),
                new CharacterStat(Stat.MoveSpeed, ModifierOp.Additive, -0.10f),
            },
        },
        // Earned by opening chests. Crits feed speed, and speed finds more crits.
        new()
        {
            Id = "wreckers_knave",
            DisplayName = "Wrecker's Knave",
            Blurb = "Lit false lights on the cliffs and picked the hulls clean. Quick hands, quicker feet, no friends.",
            StartingWeapon = "riptide_blades",
            Passive = PassiveKind.Ambush,
            PassiveName = "Opportunist",
            PassiveText = "Critical kills grant +4% move and attack speed for 5 seconds, stacking up to 5 times.",
            LookId = "rogue",
            Tint = new Color(0.40f, 0.36f, 0.48f),
            ExtraSkins = new SkinDef[]
            {
                new() { Id = "wreckers_knave.gutter", Name = "Guttersnipe", LookId = "scout", Tint = new Color(0.45f, 0.42f, 0.38f) },
                new() { Id = "wreckers_knave.smuggler", Name = "Smuggler", LookId = "default_tidecaller", Tint = new Color(0.25f, 0.30f, 0.45f) },
                new() { Id = "wreckers_knave.inkblot", Name = "Inkblot", LookId = "proc-default", Tint = new Color(0.12f, 0.12f, 0.18f) },
                new() { Id = "wreckers_knave.cutpurse", Name = "Cutpurse", LookId = "gale_stormchaser", Tint = new Color(0.55f, 0.35f, 0.60f) },
                new() { Id = "wreckers_knave.wreckling", Name = "Wreckling", LookId = "wildling", Tint = new Color(0.55f, 0.45f, 0.30f) },
                new() { Id = "wreckers_knave.bilge", Name = "Bilgewater", LookId = "rogue", Tint = new Color(0.40f, 0.45f, 0.25f),
                        Recolor = Visuals.SkinRecolor.Dyed(new Color(0.40f, 0.45f, 0.25f), 0.80f, 0.85f, 1.00f) },
                new() { Id = "wreckers_knave.redtide", Name = "Red Tide", LookId = "rogue", Tint = new Color(0.75f, 0.18f, 0.20f),
                        Recolor = Visuals.SkinRecolor.Dyed(new Color(0.75f, 0.18f, 0.20f), 0.85f, 1.00f, 1.00f) },
            },
            Stats = new[]
            {
                new CharacterStat(Stat.CritChance, ModifierOp.Flat, 0.10f),
                new CharacterStat(Stat.MaxHealth, ModifierOp.Flat, -10f),
            },
        },
        // Earned with critical hits. Movement is the trigger: dash and air-jump
        // to charge a lightning burst around you.
        new()
        {
            Id = "stormwright",
            DisplayName = "Stormwright",
            Blurb = "Bottles weather for the fishing fleets. Every step she takes leaves a little static behind.",
            StartingWeapon = "static_field",
            Passive = PassiveKind.Overcharge,
            PassiveName = "Overcharge",
            PassiveText = "Every 4th dash or air jump releases a lightning burst that strikes everything within 7 m.",
            LookId = "arcanist",
            Tint = new Color(0.45f, 0.28f, 0.72f),
            ExtraSkins = new SkinDef[]
            {
                new() { Id = "stormwright.thunderhead", Name = "Thunderhead", LookId = "proc-arcanist", Tint = new Color(0.30f, 0.30f, 0.55f) },
                new() { Id = "stormwright.rainmaker", Name = "Rainmaker", LookId = "augur", Tint = new Color(0.45f, 0.65f, 0.85f) },
                new() { Id = "stormwright.tempest", Name = "Tempest", LookId = "gale_stormchaser", Tint = new Color(0.55f, 0.50f, 1.00f) },
                new() { Id = "stormwright.calm", Name = "Eye of Calm", LookId = "medic", Tint = new Color(0.80f, 0.90f, 1.00f) },
                new() { Id = "stormwright.overcast", Name = "Overcast", LookId = "golem", Tint = new Color(0.55f, 0.58f, 0.62f) },
                new() { Id = "stormwright.squallglass", Name = "Squallglass", LookId = "arcanist", Tint = new Color(0.60f, 0.80f, 0.95f),
                        Recolor = Visuals.SkinRecolor.Dyed(new Color(0.60f, 0.80f, 0.95f), 0.80f, 1.10f, 1.00f) },
                new() { Id = "stormwright.aurora", Name = "Aurora", LookId = "arcanist", Tint = new Color(0.40f, 0.95f, 0.70f),
                        Recolor = Visuals.SkinRecolor.Shifted(0.33f, 1.20f, 1.10f) },
            },
            Stats = new[]
            {
                new CharacterStat(Stat.AttackSpeed, ModifierOp.Additive, 0.10f),
                new CharacterStat(Stat.DashCooldown, ModifierOp.Additive, -0.10f),
                new CharacterStat(Stat.MaxHealth, ModifierOp.Flat, -10f),
            },
        },
    });

    public static CharacterDef Default => All[0];

    public static CharacterDef? ById(string? id)
    {
        foreach (CharacterDef c in All) if (c.Id == id) return c;
        return null;
    }

    /// <summary>
    /// Who this run is played as. --character wins over the saved pick; a locked
    /// or unknown id falls back to the default rather than failing the launch —
    /// a stale profile must never be able to stop the game starting.
    /// </summary>
    public static CharacterDef ForThisRun()
    {
        Profile.EnsureLoaded();
        string? wanted = Launch.CharacterId ?? (Launch.Automated ? null : Profile.SelectedCharacter);
        CharacterDef? def = ById(wanted);
        if (def is null || !def.IsAvailable)
        {
            if (wanted is not null && wanted != DefaultId)
                GD.Print($"[meta] character '{wanted}' is {(def is null ? "unknown" : "locked")}; playing {DefaultId}");
            return Default;
        }
        return def;
    }

    /// <summary>"+25 Max Health, -8% Move Speed" for menus.</summary>
    public static string Describe(CharacterDef def)
    {
        if (def.Stats.Count == 0) return "No modifiers";
        var parts = new List<string>();
        foreach (CharacterStat s in def.Stats)
        {
            string name = Catalog.StatName(s.Stat);
            parts.Add(s.Op switch
            {
                ModifierOp.Additive => $"{(s.Value >= 0 ? "+" : "")}{s.Value * 100f:0}% {name}",
                ModifierOp.Multiplicative => $"x{s.Value:0.##} {name}",
                _ => $"{(s.Value >= 0 ? "+" : "")}{s.Value:0.##} {name}",
            });
        }
        return string.Join(",  ", parts);
    }
}
