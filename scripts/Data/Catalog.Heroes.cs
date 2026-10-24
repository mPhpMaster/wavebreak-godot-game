using System.Collections.Generic;
using Godot;
using Wavebreak.Combat;
using Wavebreak.Combat.Weapons;

namespace Wavebreak.Data;

/// <summary>
/// The three signature weapons of the heroes trio (Meta/Characters.Heroes.cs).
/// Ordinary catalogue entries, delivered by <see cref="Signatures"/>; the offer
/// pool never offers one new, so only the character who starts with it can
/// ever hold it. Same 15-18 paper-dps band as everything else, priced on what
/// lands: the spyglass on one precise body, the anchor on a ring of them, the
/// kite on what its string actually sweeps.
/// </summary>
public static partial class Catalog
{
    /// <summary>Signature ids added by this file, for the heroes audit.</summary>
    public static readonly HashSet<string> SignatureIds = new();

    private static void AddSignatureWeapons()
    {
        foreach (WeaponDef w in SignatureWeapons())
        {
            SignatureIds.Add(w.Id);
            WeaponList.Add(w);
            WeaponsById[w.Id] = w;
        }
    }

    private static List<WeaponDef> SignatureWeapons() => new()
    {
        // Precise: one long thin beam at whatever you are looking at.
        // Range is the beam's reach, Radius its half-width, Pierce the bodies past the first.
        new WeaponDef
        {
            Id = Signatures.Spyglass, DisplayName = "Spyglass Glint",
            Description = "Sunny's brass spyglass, turned round to throw the sun instead of catch it. Whatever you are looking at is struck by a thin beam: a guaranteed critical hit, harder the farther away it is, and harder again for every body it passes through.",
            Rarity = Rarity.Rare, Shape = AttackShape.Projectile, Element = Element.Physical,
            Damage = 30f, Cooldown = 1.8f, Projectiles = 1, ProjectileSpeed = 0f, Range = 28f, Radius = 0.3f,
            Pierce = 2, Knockback = 2f, CritChance = 0.10f, ProcCoefficient = 1.2f, SpreadDegrees = 0f,
            Tint = new Color(1.00f, 0.95f, 0.72f),
            DamagePerLevel = 0.30f, CooldownPerLevel = -0.05f, ProjectilesEveryNLevels = 4, MaxLevel = 8,
        },

        // Control: thrown at the nearest enemy, moors a ring of ground for 3 s,
        // then is hauled home through everything still inside.
        // Radius is the ring's radius, ProjectileSpeed the throw.
        new WeaponDef
        {
            Id = Signatures.Anchor, DisplayName = "Mooring Anchor",
            Description = "Biscuit's anchor on a long chain. Where it lands, the ground is moored: everything inside is slowed and chafed by the ropes, anything walking out is pulled back in, and when the chain is hauled home they all feel it.",
            Rarity = Rarity.Rare, Shape = AttackShape.Explosive, Element = Element.Physical,
            Damage = 24f, Cooldown = 4.2f, Projectiles = 1, ProjectileSpeed = 16f, Range = 13f, Radius = 3.6f,
            Knockback = 1.5f, CritChance = 0.05f, ProcCoefficient = 1f, SpreadDegrees = 0f,
            Tint = new Color(0.78f, 0.84f, 0.95f),
            DamagePerLevel = 0.28f, CooldownPerLevel = -0.05f, ProjectilesEveryNLevels = 3, MaxLevel = 8,
        },

        // Movement: a kite on a string that skims behind you. Range is the
        // string's length, Radius the half-width it cuts; the hit grows with
        // how fast the kite is moving, so it is steered by running.
        new WeaponDef
        {
            Id = Signatures.Kite, DisplayName = "Skimmer Kite",
            Description = "Flick's kite, flown low over the surf on a very long string. You steer it by running: the string cuts whatever it sweeps through, the kite hits harder still, and the faster it flies the deeper both bite.",
            Rarity = Rarity.Rare, Shape = AttackShape.Melee, Element = Element.Physical,
            Damage = 6f, Cooldown = 0.3f, Projectiles = 1, ProjectileSpeed = 0f, Range = 5.5f, Radius = 0.35f,
            Knockback = 1.2f, CritChance = 0.06f, ProcCoefficient = 0.35f, SpreadDegrees = 0f,
            Tint = new Color(1.00f, 0.62f, 0.45f),
            DamagePerLevel = 0.26f, CooldownPerLevel = -0.04f, ProjectilesEveryNLevels = 4, MaxLevel = 8,
        },
    };
}
