using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Wavebreak.Data;
using Wavebreak.Stats;

namespace Wavebreak.Meta;

/// <summary>
/// The content side of <see cref="Loc"/>: every player-facing name and
/// description held in the data tables (weapons, items, tomes, enemies,
/// characters, skins, hats, quests, shop lines, maps, routes, challenges...).
/// The audit checks each has Arabic; <c>--dump-strings=&lt;txt&gt;</c> writes the
/// ones that do not, one per line, ready to translate.
/// </summary>
public static partial class LocContent
{
    /// <summary>(where, English) for every content string. Empty strings skipped.</summary>
    public static IEnumerable<(string Where, string Text)> All()
    {
        foreach (WeaponDef w in Catalog.Weapons) { yield return ($"weapon {w.Id}", w.DisplayName); yield return ($"weapon {w.Id} desc", w.Description); }
        foreach (WeaponDef w in MetaContent.Weapons) { yield return ($"weapon {w.Id}", w.DisplayName); yield return ($"weapon {w.Id} desc", w.Description); }
        foreach (ItemDef i in Catalog.Items) { yield return ($"item {i.Id}", i.DisplayName); yield return ($"item {i.Id} desc", i.Description); }
        foreach (ItemDef i in MetaContent.Items) { yield return ($"item {i.Id}", i.DisplayName); yield return ($"item {i.Id} desc", i.Description); }
        foreach (EnemyDef e in Catalog.Enemies) yield return ($"enemy {e.Id}", e.DisplayName);
        foreach (TomeDef t in Tomes.All) { yield return ($"tome {t.Id}", t.DisplayName); yield return ($"tome {t.Id} desc", t.Description); }

        foreach (CharacterDef c in Characters.All)
        {
            yield return ($"character {c.Id}", c.DisplayName);
            yield return ($"character {c.Id} blurb", c.Blurb);
            yield return ($"character {c.Id} passive", c.PassiveName);
            yield return ($"character {c.Id} passive text", c.PassiveText);
            foreach (SkinDef s in c.Skins) yield return ($"skin {s.Id}", s.Name);
        }
        foreach (HatDef h in Hats.All) yield return ($"hat {h.Id}", h.Name);

        foreach (UnlockDef u in Unlocks.All)
        {
            yield return ($"quest {u.Id}", u.Name);
            yield return ($"quest {u.Id} desc", u.Description);
            yield return ($"quest {u.Id} hint", u.Hint);
        }

        foreach (ShopUpgradeDef s in MetaShop.All) { yield return ($"shop {s.Id}", s.Name); yield return ($"shop {s.Id} blurb", s.Blurb); }
        foreach (ShopUpgradeDef s in ShopExtras.Upgrades) { yield return ($"shop {s.Id}", s.Name); yield return ($"shop {s.Id} blurb", s.Blurb); }
        foreach (DifficultyTier d in DifficultyTiers.All) yield return ($"difficulty {d.Id}", d.Name);
        foreach (ChallengeDef c in Challenges.All) { yield return ($"challenge {c.Id}", c.Name); yield return ($"challenge {c.Id} blurb", c.Blurb); }
        foreach (MapChallengeDef c in MapChallenges.All) { yield return ($"map challenge {c.Id}", c.Name); yield return ($"map challenge {c.Id} blurb", c.Blurb); }
        foreach (RouteDef r in Routes.All) { yield return ($"route {r.Id}", r.Name); yield return ($"route {r.Id} blurb", r.Blurb); }
        foreach (StageDef s in Stages.All)
        {
            yield return ($"map {s.Id}", s.DisplayName);
            yield return ($"map {s.Id} tagline", s.Tagline);
            yield return ($"map {s.Id} hazard", s.HazardName);
        }
        foreach (SecretKindDef k in SecretSpawner.Kinds) { yield return ($"secret {k.Id}", k.DisplayName); yield return ($"secret {k.Id} reward", k.RewardText); }
        foreach (var (action, label) in Keybinds.Actions) yield return ($"keybind {action}", label);

        foreach (Stat s in Enum.GetValues<Stat>()) yield return ($"stat {s}", Catalog.StatName(s));
        foreach (Rarity r in Enum.GetValues<Rarity>()) yield return ($"rarity {r}", Catalog.RarityName(r));
        foreach (MapShrineKind k in Enum.GetValues<MapShrineKind>()) yield return ($"shrine {k}", MapShrines.Name(k));
        foreach (CuteShrineKind k in Enum.GetValues<CuteShrineKind>()) yield return ($"shrine {k}", CuteShrines.Name(k));
        yield return ("pearls", Pearls.Name);
    }

    /// <summary>Content strings with no Arabic, deduplicated, in table order.</summary>
    public static List<(string Where, string Text)> Missing()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var list = new List<(string, string)>();
        foreach (var (where, text) in All())
        {
            if (string.IsNullOrWhiteSpace(text) || text == "???") continue;
            if (!seen.Add(text)) continue;
            if (Loc.ArabicFor(text) is null) list.Add((where, text));
        }
        return list;
    }

    /// <summary>--dump-strings: one "where<TAB>english" line per missing string.</summary>
    public static void Dump(string path)
    {
        List<(string Where, string Text)> missing = Missing();
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        if (f is null)
        {
            GD.PrintErr($"[loc] cannot write {path}");
            return;
        }
        foreach (var (where, text) in missing) f.StoreLine(where + "\t" + text.Replace("\n", "\\n"));
        GD.Print($"[loc] dumped {missing.Count} untranslated content strings to {path}");
    }
}
