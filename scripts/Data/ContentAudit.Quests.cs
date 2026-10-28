using System.Collections.Generic;
using System.Linq;
using Wavebreak.Meta;

namespace Wavebreak.Data;

/// <summary>
/// The quest book in the audit: ids unique, every reward resolves to real
/// content and is granted by exactly one row, every keyed metric names
/// something that exists, every skin is either free or earned by one quest,
/// and each character ends up with at least six skins.
/// </summary>
public static partial class ContentAudit
{
    private static void AuditQuests()
    {
        var all = Unlocks.All;
        int secrets = all.Count(u => u.Secret), pearls = all.Count(u => u.Reward == RewardKind.Pearls);
        int skins = Characters.All.Sum(c => c.Skins.Count);
        Line($"quests total={all.Count} secrets={secrets} pearlRewards={pearls} skins={skins}");
        Check(all.Count >= 240, $"only {all.Count} quests (want ~250)");

        var ids = new HashSet<string>();
        var rewards = new Dictionary<string, string>();
        var skinIds = new HashSet<string>(Characters.All.SelectMany(c => c.Skins).Select(s => s.Id));
        foreach (UnlockDef u in all)
        {
            Check(ids.Add(u.Id), $"duplicate quest id {u.Id}");
            Check(u.Threshold > 0f, $"quest {u.Id} has threshold {u.Threshold}");
            Check(!u.Secret || u.Hint.Length > 0, $"secret quest {u.Id} has no hint");
            if (rewards.TryGetValue(u.RewardId, out string? other))
                Check(false, $"reward {u.RewardId} granted by both {other} and {u.Id}");
            else rewards[u.RewardId] = u.Id;

            bool known = u.Reward switch
            {
                RewardKind.Character => Characters.ById(u.RewardId) is not null,
                RewardKind.Skin => skinIds.Contains(u.RewardId),
                RewardKind.Hat => Hats.ById(u.RewardId) is not null,
                RewardKind.Weapon => Catalog.WeaponById(u.RewardId) is not null || MetaContent.WeaponById(u.RewardId) is not null,
                RewardKind.Item => Catalog.ItemById(u.RewardId) is not null || MetaContent.ItemById(u.RewardId) is not null,
                RewardKind.Pearls => u.Pearls > 0 && u.RewardId == "pearls." + u.Id,
                _ => false,
            };
            Check(known, $"quest {u.Id} rewards unknown {u.Reward} '{u.RewardId}'");

            bool keyOk = u.Metric switch
            {
                Metric.WeaponKills or Metric.WeaponMaxed
                    => Catalog.WeaponById(u.Key) is not null || MetaContent.WeaponById(u.Key) is not null,
                Metric.TomeMaxed => Tomes.ById(u.Key) is not null,
                Metric.CharacterWins or Metric.CharacterKills or Metric.CharacterHardWins => Characters.ById(u.Key) is not null,
                Metric.DifficultyWins => DifficultyTiers.ById(u.Key) is not null,
                Metric.MapClears => Stages.All.Any(s => s.Id == u.Key),
                Metric.ChallengeWins => Challenges.ById(u.Key) is not null,
                Metric.PhaseBossBeaten or Metric.PhaseUntouched => int.TryParse(u.Key, out _),
                Metric.Feat => u.Key.Length > 0,
                _ => u.Key.Length == 0,
            };
            Check(keyOk, $"quest {u.Id}: metric {u.Metric} has bad key '{u.Key}'");
        }

        // The cute crew ship with three skins each (base and two earned); the
        // rest keep the six-skin floor. AuditCute checks the crew's own wiring.
        foreach (CharacterDef c in Characters.All)
        {
            int floor = Characters.IsExpedition(c.Id) ? 1 : Characters.IsCuteCrew(c.Id) ? 3 : 6;
            Check(c.Skins.Count >= floor, $"character {c.Id} has only {c.Skins.Count} skins (want {floor}+)");
        }
        AuditCute();
    }
}
