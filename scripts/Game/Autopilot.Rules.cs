using System.Collections.Generic;
using Wavebreak.Core;
using Wavebreak.Meta;
using Wavebreak.Progression;

namespace Wavebreak.Game;

/// <summary>
/// The player's "Auto-pick on level up" (Settings > Gameplay): one card from a
/// hand by a plain rule the player chose, instead of the bot's scoring. The
/// bot's own score (<see cref="Score"/>) breaks ties, so "Prefer weapons" with
/// two weapons on offer still takes the better one. Supply payouts are only
/// taken when nothing else is on offer.
/// </summary>
public sealed partial class Autopilot
{
    public static Offer PickByRule(IReadOnlyList<Offer> hand, AutoPickRule rule, Rng? rng)
    {
        if (hand.Count == 1) return hand[0];
        if (rule == AutoPickRule.Random)
        {
            // Random among real cards; a supply payout only when it is all there is.
            var real = new List<Offer>();
            foreach (Offer o in hand) if (!o.IsBonus) real.Add(o);
            IReadOnlyList<Offer> from = real.Count > 0 ? real : hand;
            return from[rng?.RangeInt(0, from.Count) ?? 0];
        }

        Offer best = hand[0];
        float bestScore = float.MinValue;
        foreach (Offer o in hand)
        {
            float s = RuleScore(o, rule);
            if (s > bestScore) { bestScore = s; best = o; }
        }
        return best;
    }

    /// <summary>Rule first (a large step), then rarity, then the bot's score.</summary>
    private static float RuleScore(Offer o, AutoPickRule rule)
    {
        if (o.IsBonus) return -100f + Score(o);
        float rarity = (int)o.Rarity;
        bool wanted = rule switch
        {
            AutoPickRule.PreferWeapons => o.IsWeapon,
            AutoPickRule.PreferItems => o.IsItem,
            AutoPickRule.PreferTomes => o.IsTome,
            AutoPickRule.UpgradeOwned => Owned(o),
            _ => false,
        };
        return (wanted ? 100f : 0f) + rarity * 10f + Score(o);
    }

    /// <summary>Already held: a weapon level, a tome level, another stack.</summary>
    private static bool Owned(Offer o)
    {
        if (o.Weapon is { } w) return Main.Instance?.Weapons.Has(w.Id) ?? false;
        if (o.Tome is { } t) return (TomeBook.Current?.LevelOf(t) ?? 0) > 0;
        if (o.Item is { } i) return (Inventory.Current?.StacksOf(i) ?? 0) > 0;
        return false;
    }
}
