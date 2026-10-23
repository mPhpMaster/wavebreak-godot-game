using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Combat;
using Wavebreak.Core;
using Wavebreak.Data;
using Wavebreak.Progression;
using Wavebreak.Stats;

namespace Wavebreak.Game;

/// <summary>
/// The autopilot's level-up decisions. Same crude spirit as its steering: not
/// optimal, but shaped like a reasonable player, so an unattended run builds
/// something coherent and exercises the tome, reroll, banish and skip paths.
///
/// Scoring, roughly: a new weapon while there are fewer than three, upgrades
/// of what it already uses (weapons and tomes), new tomes while slots are free,
/// then items, then supply payouts. A little seeded noise keeps two runs of
/// different seeds from building identically. It rerolls a hand whose best card
/// is weak, banishes an item with a trade-off it dislikes, and skips a hand of
/// pure supply payouts while it has skips (the skip pays gold either way).
/// </summary>
public sealed partial class Autopilot
{
    /// <summary>Below this the best card is not worth taking if a reroll is left.</summary>
    private const float RerollBelow = 1.6f;

    /// <summary>Banish a card scoring below this, once, when it would otherwise
    /// keep turning up. Items with a downside land here.</summary>
    private const float BanishBelow = 1.0f;

    public static void ResolveLevelUp(List<Offer> offers, Rng rng, Action<Offer> choose)
    {
        LevelUpChoices? choices = LevelUpChoices.Current;
        if (offers.Count == 0) return;
        if (choices is null)
        {
            choose(offers[rng.RangeInt(0, offers.Count)]);
            return;
        }

        var hand = new List<Offer>(offers);
        for (int guard = 0; guard < 8; guard++)
        {
            // Banish the worst card first, if it is one the bot never wants.
            int worst = IndexOf(hand, rng, best: false, out float worstScore);
            if (worstScore < BanishBelow && choices.BanishesLeft > 0 && LevelUpChoices.CanBanish(hand[worst]))
            {
                var others = new List<Offer>(hand);
                others.RemoveAt(worst);
                if (choices.TryBanish(hand[worst], others, out Offer replacement))
                {
                    Log($"banish {hand[worst].Title} -> {replacement.Title}");
                    hand[worst] = replacement;
                    continue;
                }
            }

            int best = IndexOf(hand, rng, best: true, out float bestScore);
            if (bestScore < RerollBelow && choices.RerollsLeft > 0 && choices.TryReroll(hand, out List<Offer> fresh))
            {
                Log($"reroll (best was {hand[best].Title} {bestScore:0.00})");
                hand = fresh;
                continue;
            }

            if (hand[best].IsBonus && choices.SkipsLeft > 0 && choices.TrySkip())
            {
                Log("skip (supply-only hand)");
                return;
            }

            choose(hand[best]);
            return;
        }

        choose(hand[0]);
    }

    private static int IndexOf(List<Offer> hand, Rng rng, bool best, out float score)
    {
        int index = 0;
        score = best ? float.MinValue : float.MaxValue;
        for (int i = 0; i < hand.Count; i++)
        {
            // Noise only on the "best" pass: the banish decision should not be a coin flip.
            float s = Score(hand[i]) + (best ? rng.Range(0f, 0.45f) : 0f);
            if (best ? s > score : s < score) { score = s; index = i; }
        }
        return index;
    }

    private static float Score(Offer offer)
    {
        if (offer.Weapon is { } weapon)
        {
            WeaponSystem? ws = Main.Instance?.Weapons;
            int owned = ws?.Weapons.Count ?? 0;
            bool held = ws?.Has(weapon.Id) ?? false;
            if (held)
            {
                // A rolled upgrade: rarity is the size of every line, the line
                // count is breadth, and damage/speed/projectiles are what a
                // bot can rely on to matter.
                if (offer.Upgrade is not { } up) return 2.1f + (int)weapon.Rarity * 0.1f;
                float core = (up.Has(UpgradeStat.Damage) ? 0.15f : 0f)
                           + (up.Has(UpgradeStat.AttackSpeed) ? 0.1f : 0f)
                           + (up.Has(UpgradeStat.Projectiles) ? 0.2f : 0f);
                return 1.9f + (int)up.Rarity * 0.22f + 0.08f * up.Lines.Count + core;
            }
            return owned < 3 ? 3.0f : 1.4f + (int)weapon.Rarity * 0.15f;
        }

        if (offer.Tome is { } tome)
        {
            int level = TomeBook.Current?.LevelOf(tome) ?? 0;
            float bias = tome.Stat switch
            {
                Stat.Damage or Stat.AttackSpeed or Stat.ProjectileCount => 0.35f,
                Stat.MaxHealth or Stat.Armor or Stat.HealthRegen => 0.2f,
                _ => 0f,
            };
            return (level > 0 ? 2.2f : 1.8f) + bias;
        }

        if (offer.Item is { } item)
        {
            bool downside = false;
            foreach (ItemEffect e in item.Effects)
            {
                if (e is null) continue;
                bool lowerIsGood = e.Stat is Stat.DashCooldown or Stat.EnemyScaling;
                float sign = e.Op == ModifierOp.Multiplicative ? e.PerStack - 1f : e.PerStack;
                if ((sign < 0f) != lowerIsGood && MathF.Abs(sign) > 1e-4f) downside = true;
            }
            float s = 1.5f + (int)item.Rarity * 0.25f;
            return downside ? s - 0.8f : s;
        }

        return 0.2f; // supply
    }

    private static void Log(string what)
    {
        if (ContentAudit.Logging) GD.Print($"CONTENT autopilot {what}");
    }
}
