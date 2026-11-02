using System.Collections.Generic;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Combat;
using Wavebreak.Core;
using Wavebreak.Data;
using Wavebreak.Progression;
using Wavebreak.Stats;

namespace Wavebreak.Game.Puzzle;

/// <summary>One reward pedestal's prize, rolled when the vault opens so the
/// pedestal can say exactly what it gives.</summary>
public sealed class PuzzleRewardOffer
{
    public PuzzleRewardKind Kind;
    public Offer? Offer;
    public EvolutionDef? Evolution;
    public int Rerolls;
    public string Title = "";
    public string Detail = "";
    public Color Tint = Colors.White;
}

/// <summary>
/// What a puzzle map pays, and paying it. Every reward feeds the build the
/// next action map will test: an item from a better-than-usual chest, the
/// catalyst one of your weapons evolves with, a weapon (new or a level), or
/// level-up rerolls. The tier decides how many pedestals there are and how
/// good they are; each player claims one, on their own machine, for their own
/// build (co-op shares the tier, never the prize).
///
///   Gold    epic chest · evolution catalyst · weapon pick   (+2 rerolls)
///   Silver  rare chest · weapon pick · 3 rerolls             (+1 reroll)
///   Bronze  chest · 2 rerolls
///   Skipped                                                    +1 reroll, a little gold
/// </summary>
public static class PuzzleRewards
{
    /// <summary>The stat modifiers' owner for every reroll a puzzle gives.</summary>
    private static readonly object RerollSource = new();

    public static PuzzleRewardKind[] KindsFor(RewardTier tier) => tier switch
    {
        RewardTier.Gold => new[] { PuzzleRewardKind.EpicChest, PuzzleRewardKind.Catalyst, PuzzleRewardKind.WeaponPick },
        RewardTier.Silver => new[] { PuzzleRewardKind.RareChest, PuzzleRewardKind.WeaponPick, PuzzleRewardKind.Rerolls },
        RewardTier.Bronze => new[] { PuzzleRewardKind.Chest, PuzzleRewardKind.Rerolls },
        _ => System.Array.Empty<PuzzleRewardKind>(),
    };

    /// <summary>Rerolls added on top of the pedestal, just for the medal.</summary>
    public static int BonusRerolls(RewardTier tier) => tier switch
    {
        RewardTier.Gold => 2,
        RewardTier.Silver => 1,
        RewardTier.Skipped => 1,
        _ => 0,
    };

    public static int PedestalRerolls(RewardTier tier) => tier == RewardTier.Bronze ? 2 : 3;

    public static List<PuzzleRewardOffer> Roll(RewardTier tier, Rng rng)
    {
        var list = new List<PuzzleRewardOffer>();
        foreach (PuzzleRewardKind k in KindsFor(tier)) list.Add(RollOne(k, tier, rng));
        return list;
    }

    public static PuzzleRewardOffer RollOne(PuzzleRewardKind kind, RewardTier tier, Rng rng)
    {
        var r = new PuzzleRewardOffer { Kind = kind };
        Inventory? inv = Inventory.Current;
        StatSheet? stats = Player.Instance?.Stats;
        switch (kind)
        {
            case PuzzleRewardKind.Catalyst:
                if (CatalystFor() is { } e)
                {
                    r.Evolution = e;
                    r.Title = Evolutions.CatalystName(e);
                    r.Detail = Meta.Loc.F("Evolves {0}", Meta.Loc.T(Evolutions.WeaponName(e)));
                    r.Tint = e.Tint;
                    return r;
                }
                return RollOne(PuzzleRewardKind.WeaponPick, tier, rng);
            case PuzzleRewardKind.WeaponPick:
                if (LevelUpChoices.Current is { } choices && Main.Instance?.Weapons is { } weapons)
                    foreach (Offer o in choices.Pool.Roll(6, weapons, inv, stats))
                        if (o.IsWeapon)
                        {
                            r.Offer = o;
                            r.Title = o.Title;
                            r.Detail = o.Detail;
                            r.Tint = Catalog.RarityColor(o.Rarity);
                            return r;
                        }
                return RollOne(PuzzleRewardKind.RareChest, tier, rng);
            case PuzzleRewardKind.Rerolls:
                r.Rerolls = PedestalRerolls(tier);
                r.Title = Meta.Loc.F("+{0} REROLL", r.Rerolls);
                r.Detail = Meta.Loc.T(PuzzleText.RewardBlurb(kind));
                r.Tint = new Color(0.72f, 0.92f, 1f);
                return r;
            default:
                Rarity floor = kind switch
                {
                    PuzzleRewardKind.EpicChest => Rarity.Epic,
                    PuzzleRewardKind.RareChest => Rarity.Rare,
                    _ => Rarity.Common,
                };
                float luck = kind == PuzzleRewardKind.Chest ? ChestSpawner.StandardLuckBonus : ChestSpawner.GildedLuckBonus;
                if (OfferPool.Current is { } pool)
                {
                    Offer o = pool.RollChest(inv, stats, luck, floor, rng);
                    r.Offer = o;
                    r.Title = o.Title;
                    r.Detail = o.IsBonus ? o.Description : Catalog.RarityName(o.Rarity);
                    r.Tint = o.IsBonus ? o.Tint : Catalog.RarityColor(o.Rarity);
                }
                else
                {
                    r.Rerolls = 1;
                    r.Title = Meta.Loc.F("+{0} REROLL", 1);
                }
                return r;
        }
    }

    /// <summary>The evolution whose catalyst would help most: a held weapon,
    /// not evolved, whose catalyst is not held (and, for a tome, fits).</summary>
    public static EvolutionDef? CatalystFor()
    {
        if (Main.Instance?.Weapons is not { } weapons) return null;
        EvolutionDef? best = null;
        int bestLevel = -1;
        foreach (WeaponRuntime w in weapons.Weapons)
        {
            if (w.Evolution is not null || Evolutions.ForWeapon(w.Def.Id) is not { } e) continue;
            if (EvolutionOffers.HasCatalyst(e, Inventory.Current, TomeBook.Current)) continue;
            bool fits = Tomes.ById(e.CatalystId) is { } tome ? TomeBook.Current?.CanTake(tome) == true : Catalog.ItemById(e.CatalystId) is not null;
            if (!fits || w.Level <= bestLevel) continue;
            best = e;
            bestLevel = w.Level;
        }
        return best;
    }

    /// <summary>Give this player the prize. Returns the line to pop over the pedestal.</summary>
    public static string Grant(PuzzleRewardOffer r)
    {
        Inventory? inv = Inventory.Current;
        Player? player = Player.Instance;
        string said = r.Title;
        if (r.Evolution is { } e)
        {
            if (Tomes.ById(e.CatalystId) is { } tome) TomeBook.Current?.Add(tome);
            else if (Catalog.ItemById(e.CatalystId) is { } item) inv?.Add(item);
        }
        else if (r.Offer is { } o)
        {
            if (o.Weapon is not null) Main.Instance?.Weapons.Add(o.Weapon, o.Upgrade);
            else if (o.Item is not null) inv?.Add(o.Item);
            else if (o.Tome is not null) TomeBook.Current?.Add(o.Tome);
            else o.Apply(player, inv);
        }
        if (r.Rerolls > 0) AddRerolls(r.Rerolls);
        GD.Print($"[puzzle] reward claimed kind={r.Kind} \"{r.Title}\"");
        Granted++;
        // A catalyst can complete a pair: offer the evolution now, like a chest does.
        Main.Instance?.OfferEvolutionAtChest();
        return said;
    }

    /// <summary>The medal's extra rerolls, and the skip's consolation.</summary>
    public static void GrantBonus(RewardTier tier)
    {
        int n = BonusRerolls(tier);
        if (n > 0) AddRerolls(n);
        if (tier == RewardTier.Skipped) Inventory.Current?.GrantGold(15);
        GD.Print($"[puzzle] bonus tier={tier} rerolls=+{n}");
    }

    public static void AddRerolls(int n) => Player.Instance?.Stats.Add(Stat.Rerolls, ModifierOp.Flat, n, RerollSource);

    /// <summary>Pedestal prizes claimed this run (self-check, summary).</summary>
    public static int Granted { get; set; }
}
