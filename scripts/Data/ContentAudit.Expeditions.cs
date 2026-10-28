using System.Linq;
using Wavebreak.Meta;
using Wavebreak.Visuals;

namespace Wavebreak.Data;

public static partial class ContentAudit
{
    private static void AuditExpeditions()
    {
        Check(Characters.All.Count == 26, "expeditions: appended two heroes to the 24-character roster");
        Check(Characters.All[^2].Id == "nori" && Characters.All[^1].Id == "vesper", "expeditions: old roster indices preserved");
        foreach (CharacterDef c in Characters.All.Where(c => Characters.IsExpedition(c.Id)))
        {
            Check(c.UnlockedBy is null && c.IsAvailable && c.Skins[0].IsAvailable, "expeditions: free hero and base skin " + c.Id);
            Check(Catalog.WeaponById(c.StartingWeapon) is not null, "expeditions: existing starter " + c.Id);
            ModelSpec look = PlayerLooks.Get(c.LookId);
            Check(look.Custom is not null && look.Jointed && look.Body == BodyType.Biped, "expeditions: original animated body " + c.Id);
            Check(!Characters.All.Any(other => other.Id != c.Id && other.LookId == c.LookId), "expeditions: unique model " + c.Id);
            Check(Loc.ArabicTable.ContainsKey(c.DisplayName) && Loc.ArabicTable.ContainsKey(c.Blurb), "expeditions: Arabic hero " + c.Id);
        }
        Check(Stages.All.Count == 6 && !Stages.All.Contains(Stages.Bellspire), "expeditions: default map pool preserved");
        Check(Stages.ById("bellspire") == Stages.Bellspire, "expeditions: city lookup");
        Check(Routes.All[^2].Id == "reef-dive" && Routes.All[^1].Id == "skyline-run", "expeditions: saved route indices preserved");
        RouteDef city = Routes.ById("skyline-run")!;
        foreach (int count in new[] { 3, 4, 5 })
        {
            var maps = Routes.Build(city, 48, count);
            Check(maps[0].Id == "bellspire" && maps[^1].Id == "cinder" && maps.Count == count,
                "expeditions: city route at phase count " + count);
            Check(maps.Select(m => m.Id).Distinct().Count() == count, "expeditions: unique route maps");
        }
        foreach (string id in Stages.Bellspire.EnemyIds)
        {
            EnemyDef? giant = Catalog.EnemyById(id);
            Check(giant is not null && CityGiantModels.IsGiant(giant) && giant.Height >= 26f,
                "expeditions: giant definition " + id);
            if (giant is not null) Check(CityGiantModels.For(giant).Height == giant.Height, "expeditions: real model scale " + id);
        }
        Check(Catalog.EnemyById(Stages.Bellspire.BossId) is { IsBoss: true }, "expeditions: city has a portal boss");
    }
}
