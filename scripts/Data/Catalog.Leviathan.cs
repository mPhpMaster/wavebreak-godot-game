namespace Wavebreak.Data;

public static partial class Catalog
{
    private static EnemyDef LeviathanDefinition(EnemyDef source)
    {
        var boss = (EnemyDef)source.Duplicate(true);
        boss.Id = "reef_leviathan"; boss.DisplayName = "Reef Leviathan";
        boss.Height = 8; boss.Radius = 3.6f; boss.AttackRange = 6; boss.MoveSpeed = 2;
        boss.Themes = new[] { "reef", "deep", "boss" };
        boss.UnderwaterOnly = true;
        return boss;
    }
}
