using System.Collections.Generic;
using Godot;
using Wavebreak.Data;
using Wavebreak.Stats;

namespace Wavebreak.Meta;

/// <summary>One stat tweak a character starts with. Same shape as an item
/// effect, applied once at run start rather than per stack.</summary>
public readonly record struct CharacterStat(Stat Stat, ModifierOp Op, float Value);

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
public static class Characters
{
    public const string DefaultId = "drifter";

    public static readonly IReadOnlyList<CharacterDef> All = new List<CharacterDef>
    {
        new()
        {
            Id = DefaultId,
            DisplayName = "The Drifter",
            Blurb = "Washed up with a rail shard and nothing to lose. Balanced in every way that matters.",
            StartingWeapon = "splinter_rail",
            LookId = "default",
            Tint = new Color(0.62f, 0.86f, 1.00f),
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
            Stats = new[]
            {
                new CharacterStat(Stat.Luck, ModifierOp.Flat, 1f),
                new CharacterStat(Stat.XpGain, ModifierOp.Additive, 0.15f),
                new CharacterStat(Stat.Damage, ModifierOp.Additive, -0.10f),
            },
        },
    };

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
