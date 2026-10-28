using Godot;

namespace Wavebreak.Data;

public static partial class Catalog
{
    private static EnemyDef CaveGuardianDefinition() => new()
    {
        Id = "reef_cave_shark", DisplayName = "Cave Shark", Height = 2.4f, Radius = 1.1f,
        Health = 280, Damage = 16, MoveSpeed = 2.7f, AttackRange = 2.5f, AttackCooldown = 1.5f,
        KnockbackResist = .65f, XpValue = 20, GoldValue = 12, Cost = 8,
        Tint = new Color(.38f, .53f, .61f), Themes = new[] { "reef", "guardian" },
        UnderwaterOnly = true, DirectorSpawns = false,
    };
}
