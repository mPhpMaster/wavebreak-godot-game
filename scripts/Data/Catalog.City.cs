using System.Collections.Generic;
using Godot;

namespace Wavebreak.Data;

public static partial class Catalog
{
    private static IEnumerable<EnemyDef> CityEnemies()
    {
        yield return CityEnemy("city_bellwarden", "Bell Warden", 26f, 3.2f, 650f, false, false);
        yield return CityEnemy("city_gatewarden", "Gate Warden", 32f, 3.8f, 1000f, true, false);
        yield return CityEnemy("city_archon", "The Bellspire Archon", 39f, 4.6f, 2100f, false, true);
    }

    private static EnemyDef CityEnemy(string id, string name, float height, float radius,
        float hp, bool elite, bool boss) => new()
    {
        Id = id, DisplayName = name, Height = height, Radius = radius,
        Health = hp, Damage = boss ? 32f : 19f, MoveSpeed = boss ? 3.1f : 2.4f,
        AttackRange = 14f, AttackCooldown = 4.8f, KnockbackResist = 1f,
        XpValue = boss ? 85 : 30, GoldValue = boss ? 65 : 18,
        Cost = elite ? 14 : 7, IsElite = elite, IsBoss = boss,
        UnlockAtSeconds = elite ? 35f : 0f, Tint = new Color(0.39f, 0.49f, 0.53f),
        Themes = new[] { "city", "tank" },
    };
}
