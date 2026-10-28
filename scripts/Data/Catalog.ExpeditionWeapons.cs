using System.Collections.Generic;
using Godot;

namespace Wavebreak.Data;

public static partial class Catalog
{
    private static IEnumerable<WeaponDef> ExpeditionWeapons()
    {
        yield return new WeaponDef
        {
            Id = "reef_shotgun", DisplayName = "Reefbreaker Shotgun",
            Description = "A six-pellet underwater shotgun. Fires every 2 seconds and pushes you back; attack-speed upgrades shorten the interval.",
            Shape = AttackShape.Projectile, Rarity = Rarity.Uncommon, Damage = 9, Cooldown = 2,
            Projectiles = 6, Range = 19, ProjectileSpeed = 40, Radius = .16f, SpreadDegrees = 17,
            Knockback = 3, Tint = new Color(.3f, .9f, .92f), MaxLevel = 8, CooldownPerLevel = -.07f,
        };
        yield return new WeaponDef
        {
            Id = "skyline_swords", DisplayName = "Skyline Twin Swords",
            Description = "Two steel blades strike together in a wide arc. Each blade deals its own hit; reach and attack-speed upgrades improve the pair.",
            Shape = AttackShape.Melee, Rarity = Rarity.Uncommon, Damage = 15, Cooldown = .85f,
            Projectiles = 2, Range = 4.2f, Radius = 1, SpreadDegrees = 150, Knockback = 1.5f,
            Tint = new Color(.78f, .9f, 1), MaxLevel = 8, CooldownPerLevel = -.06f,
        };
    }
}
