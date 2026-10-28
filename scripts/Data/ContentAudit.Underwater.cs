using System.Linq;
using Wavebreak.Meta;
using Wavebreak.Visuals;

namespace Wavebreak.Data;

public static partial class ContentAudit
{
    private static void AuditUnderwater()
    {
        StageDef map = Stages.Lumenreef;
        Check(Stages.ById(map.Id) == map, "reef: side map lookup");
        Check(!Stages.All.Any(s => s.Id == map.Id), "reef: excluded from standard draws");
        Check(PropTheme.Get(map.Theme).Id == map.Theme, "reef: underwater theme exists");
        Check(map.Underwater && !Stages.Run.Any(s => s.Underwater), "reef: only the side map enables swimming");
        Check(Catalog.EnemyById(map.BossId) is { IsBoss: true }, "reef: boss exists");
        foreach (string id in map.EnemyIds)
            Check(Catalog.EnemyById(id) is { IsBoss: false }, $"reef: valid wave enemy {id}");
        Check(map.EnemyIds.Any(id => Catalog.EnemyById(id)?.UnlockAtSeconds == 0f),
            "reef: opening wave has an unlocked enemy");
        Check(map.HazardDamage > 0f && map.HazardRadius < map.ClearRadius,
            "reef: readable damaging pools and clear arrival space");
        foreach (string text in new[] { map.DisplayName, map.Tagline, map.HazardName })
            Check(Loc.ArabicTable.ContainsKey(text), $"reef: Arabic for {text}");
        RouteDef? route = Routes.ById("reef-dive");
        Check(route is not null, "reef: selectable route exists");
        if (route is null) return;
        for (int count = 3; count <= 5; count++)
        {
            var stages = Routes.Build(route, 42, count);
            Check(stages.Count == count && stages[0].Id == map.Id && stages[1].Id == "shallows" && stages[2].Id == "stormwatch",
                $"reef: {count}-phase route preserves the Reef/Shallows/Harbour prefix");
            Check(stages.Select(s => s.Id).Distinct().Count() == count, "reef: no repeated maps");
        }
        Check(Routes.Build(Routes.All[0], 42).Select(s => s.Id).SequenceEqual(Stages.Run.Select(s => s.Id)),
            "reef: Classic route unchanged");
    }
}
