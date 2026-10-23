using System;
using Godot;

namespace Wavebreak.Data;

/// <summary>A field an enemy projects onto the allies around it. Read by
/// <c>Actors.Enemy.Expansion</c>; every one is drawn as a ring on the floor so
/// the player can see which crowd is being helped and kill the source first.</summary>
public enum EnemyAura
{
    None,
    /// <summary>Allies inside move faster.</summary>
    Haste,
    /// <summary>Allies inside take less damage.</summary>
    Ward,
    /// <summary>Allies inside regenerate a share of their health per second.</summary>
    Mend,
    /// <summary>Allies inside swing and use abilities faster.</summary>
    Frenzy,
}

/// <summary>
/// The expansion's additions to <see cref="EnemyDef"/>: traits that combine
/// with any <see cref="EnemyBehavior"/> (flying, a frontal shield, an aura),
/// the numbers the new behaviours read, and theme tags for map rosters.
/// </summary>
public partial class EnemyDef
{
    /// <summary>
    /// Free-form tags saying where this enemy belongs: biome words ("shore",
    /// "reef", "deep", "storm", "frost", "ash", "meadow") and role words
    /// ("swarm", "flyer", "tank", "support", "ranged", "elite", "boss"). A map
    /// can prefer enemies with its tags; nothing in the enemy layer reads them.
    /// Base enemies get theirs from <c>Catalog.ThemeTable</c>.
    /// </summary>
    [Export] public string[] Themes { get; set; } = Array.Empty<string>();

    /// <summary>Above 0: flies at this height over the player's altitude,
    /// ignoring terrain (no world collision), dipping lower to strike.</summary>
    [Export] public float Hover { get; set; }

    /// <summary>Above 0: hits arriving within this many degrees of the facing
    /// direction are blocked by <see cref="ShieldBlock"/>.</summary>
    [Export] public float ShieldArc { get; set; }

    /// <summary>Fraction of a frontal hit the shield absorbs.</summary>
    [Export] public float ShieldBlock { get; set; } = 0.8f;

    [Export] public EnemyAura Aura { get; set; } = EnemyAura.None;
    [Export] public float AuraRadius { get; set; } = 6f;

    /// <summary>Haste/Frenzy: +fraction of speed or rate. Ward: fraction of
    /// damage removed. Mend: fraction of max health per second.</summary>
    [Export] public float AuraStrength { get; set; } = 0.3f;

    /// <summary>Healer: fraction of an ally's max health restored per pulse.</summary>
    [Export] public float SupportAmount { get; set; } = 0.15f;

    /// <summary>Artillery: seconds a shell's blast circle fills before it lands.</summary>
    [Export] public float ShellDelay { get; set; } = 1.1f;

    /// <summary>True if <see cref="Themes"/> contains <paramref name="tag"/>
    /// (case-insensitive).</summary>
    public bool HasTheme(string tag)
    {
        foreach (string t in Themes)
            if (string.Equals(t, tag, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
