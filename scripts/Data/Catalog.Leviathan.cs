namespace Wavebreak.Data;

public static partial class Catalog
{
    private static EnemyDef LeviathanDefinition(EnemyDef source)
    {
        var boss = (EnemyDef)source.Duplicate(true);
        boss.Id = "reef_leviathan"; boss.DisplayName = "Reef Leviathan";
        boss.Height = 32; boss.Radius = 15; boss.AttackRange = 18; boss.MoveSpeed = 1.5f;
        boss.UnlockAtSeconds = 0;
        foreach (BossAttack move in boss.BossMoves)
        {
            move.Radius = System.MathF.Max(19, move.Radius * 2.5f);
            move.MaxRange = System.MathF.Max(boss.Radius + 18, move.MaxRange);
            move.WindUp = System.MathF.Max(1.2f, move.WindUp);
        }
        boss.Themes = new[] { "reef", "deep", "boss" };
        boss.UnderwaterOnly = true;
        return boss;
    }
}
