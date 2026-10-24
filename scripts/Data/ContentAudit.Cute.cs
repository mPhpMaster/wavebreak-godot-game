using System.Collections.Generic;
using System.Linq;
using Wavebreak.Meta;

namespace Wavebreak.Data;

/// <summary>
/// The cute crew in the audit (Meta/Characters.Cute.cs, Hats.Cute.cs,
/// Quests.Cute.cs): roster, skin and hat totals are where they were aimed,
/// every crew member has a passive nobody else has, a starting weapon nobody
/// else starts with, a gate, a look that resolves, and skins that are each
/// earned by a quest; every hat builds a mesh and can be obtained.
/// </summary>
public static partial class ContentAudit
{
    private static void AuditCute()
    {
        int skins = Characters.All.Sum(c => c.Skins.Count);
        Line($"cute characters={Characters.All.Count} skins={skins} hats={Hats.All.Count} crew={string.Join(",", Characters.CuteIds)}");
        Check(Characters.All.Count >= 21, $"only {Characters.All.Count} characters (want 21)");
        // +18 with the heroes trio (Meta/Characters.Heroes.cs): about 151.
        Check(skins is >= 140 and <= 165, $"{skins} skins (want about 151)");
        Check(Hats.All.Count >= 22, $"only {Hats.All.Count} hats (want 22)");

        var passives = new Dictionary<PassiveKind, string>();
        var starts = new Dictionary<string, string>();
        foreach (CharacterDef c in Characters.All)
        {
            if (c.Passive != PassiveKind.None && !passives.TryAdd(c.Passive, c.Id))
                Check(false, $"passive {c.Passive} shared by {passives[c.Passive]} and {c.Id}");
            if (!starts.TryAdd(c.StartingWeapon, c.Id) && Characters.IsCuteCrew(c.Id))
                Check(false, $"cute {c.Id} starts with {c.StartingWeapon}, as does {starts[c.StartingWeapon]}");
        }

        foreach (string id in Characters.CuteIds)
        {
            CharacterDef? c = Characters.ById(id);
            Check(c is not null, $"cute crew {id} is not in the roster");
            if (c is null) continue;
            Check(c.Passive != PassiveKind.None && c.PassiveName.Length > 0 && c.PassiveText.Length > 0, $"cute {id} has no passive");
            Check(Catalog.WeaponById(c.StartingWeapon) is not null, $"cute {id} starts with unknown weapon {c.StartingWeapon}");
            Check(c.UnlockedBy is not null, $"cute {id} is not gated by a quest");
            Check(c.Stats.Count > 0, $"cute {id} has no stat profile");
            foreach (SkinDef s in c.Skins.Skip(1))
                Check(Unlocks.SourceOf(s.Id) is not null, $"cute skin {s.Id} is not earned by any quest");
            if (Visuals.ArtAssets.UseImported)
                Check(Visuals.PlayerLooks.Get(c.LookId).IsImported, $"cute {id} look {c.LookId} did not load its model");
        }

        foreach (HatDef h in Hats.All)
        {
            if (h.Id == Hats.NoneId) continue;
            Check(Visuals.HatModels.Get(h.Id) is not null, $"hat {h.Id} builds no mesh");
            Check(h.UnlockedBy is not null || h.PearlCost >= 0, $"hat {h.Id} cannot be obtained");
        }
        foreach (string b in new[] { "drifter.candyfloss", "gale_courier.festival", "kiln_sister.marshmallow" })
            Check(Unlocks.SourceOf(b) is not null, $"pastel skin {b} is not earned by any quest");
    }
}
