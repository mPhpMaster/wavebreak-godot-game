using System;
using System.Collections.Generic;
using System.Text;
using Godot;
using Wavebreak.Core;
using Wavebreak.Data;
using Wavebreak.Game;
using Wavebreak.Meta;
using Wavebreak.Progression;

namespace Wavebreak.UI;

public sealed partial class LevelUpPanel
{
    // ------------------------------------------------------------- card fill

    /// <summary>
    /// Writes one offer onto one card.
    ///
    /// The offer already carries its own display text — title, mechanical effect,
    /// flavour, and a status line saying whether this is a new pick or an upgrade.
    /// That is the right place for it: whether a weapon offer means "new" or
    /// "level 3 → 4", and what that upgrade is actually worth, is knowledge the
    /// progression system has and the UI would have to reconstruct. This method's
    /// entire job is putting those strings in the right boxes and colouring them.
    /// </summary>
    private void Fill(Card card, Offer offer, int index)
    {
        Rarity rarity = offer.Rarity;
        Color colour = Catalog.RarityColor(rarity);

        // Rarity colours the frame; the definition's own tint colours the name, so
        // an item's card and the thing it produces in the world match. Two signals
        // rather than one, and neither is load-bearing on its own — the rarity is
        // also spelled out in words.
        card.Stripe.Color = colour;
        card.Rarity.Text = $"{Loc.T(Catalog.RarityName(rarity))}   {Loc.T(KindOf(offer))}";
        card.Rarity.AddThemeColorOverride("font_color", colour);

        card.Icon.Set(Icons.For(offer), colour);

        card.Title.Text = Loc.T(offer.Title);
        card.Title.AddThemeColorOverride("font_color", offer.Tint.Lerp(Ink, 0.35f));

        card.Stats.Text = EffectText(offer);
        card.Flavour.Text = Loc.T(offer.Description);
        FlowText(card);
        card.Kind.Text = offer.DetailText;

        // Epics and legendaries get a visibly warmer body. When one appears the
        // player should know it before reading a word of it.
        card.Base = rarity switch
        {
            Rarity.Legendary => new Color(0.17f, 0.11f, 0.05f, 0.98f),
            Rarity.Epic      => new Color(0.13f, 0.09f, 0.18f, 0.98f),
            _                => CardBack,
        };
        Paint(card, card.Button.HasFocus());

        card.Button.TooltipText = $"{index + 1}. {Loc.T(offer.Title)} — {offer.Effect}";
    }

    /// <summary>
    /// Stacks the stats and the flavour under the rule. The stats box is sized to
    /// its own lines and the flavour takes what is left, up to four lines: an
    /// item whose real effect is a rule ("hits have a 5% chance to burst ...")
    /// keeps that rule in its description, and a fixed two-line box cut it off
    /// mid-sentence exactly on the cards that needed it most.
    /// </summary>
    private static void FlowText(Card card)
    {
        const float top = 200f;
        const float lineH = 21f;
        int lines = 0;
        foreach (string line in card.Stats.Text.Split('\n'))
            lines += 1 + line.Length / 34;

        float statsH = Mathf.Clamp(lines * lineH + 4f, 26f, 116f);
        Inset(card.Stats, 16f, top, CardWidth - 32f, statsH);

        float flavourTop = top + statsH + 12f;
        float flavourH = CardHeight - 30f - flavourTop;
        Inset(card.Flavour, 16f, flavourTop, CardWidth - 32f, flavourH);
        card.Flavour.MaxLinesVisible = Mathf.Clamp((int)(flavourH / 19f), 1, 4);
    }

    /// <summary>
    /// What the card says the offer does.
    ///
    /// Weapon and supply offers use the text the offer built for itself — a
    /// weapon upgrade's "18 → 22 damage, 0.71s → 0.66s" is a comparison only the
    /// progression system can make.
    ///
    /// Items are re-rendered here through <see cref="Catalog.DescribeEffect"/>,
    /// for one reason: some stats are stored as fractions. <c>CritChance</c>,
    /// <c>CritDamage</c> and <c>Lifesteal</c> hold 0.06 to mean six percent, so a
    /// formatter that prints a flat modifier verbatim tells the player "+0.06
    /// Crit Chance" for an item that doubles their crit rate. That is not a
    /// rounding difference, it is off by a factor of a hundred, and the catalogue
    /// is the only place that knows which stats are which.
    ///
    /// One effect per line, too: a card is read in a glance and a comma-joined
    /// list of three effects wraps into a paragraph.
    /// </summary>
    private static string EffectText(Offer offer)
    {
        if (offer.Item is not { } item || item.Effects is null || item.Effects.Count == 0)
            return offer.Effect;

        var text = new StringBuilder(96);
        foreach (ItemEffect effect in item.Effects)
        {
            if (effect is null) continue;
            if (text.Length > 0) text.Append('\n');
            text.Append(Catalog.DescribeEffect(effect));
        }

        return text.Length > 0 ? text.ToString() : offer.Effect;
    }

    /// <summary>The one-word category under the rarity: which of the five attack
    /// shapes a weapon is, or that the offer is an item or a supply drop. Shape is
    /// worth the space — "another aura" and "another projectile" are completely
    /// different picks for the same build.</summary>
    private static string KindOf(Offer offer)
    {
        if (offer.Weapon is { } weapon)
            return weapon.Shape switch
            {
                AttackShape.Projectile => "· PROJECTILE",
                AttackShape.Melee      => "· MELEE",
                AttackShape.Aura       => "· AURA",
                AttackShape.Explosive  => "· EXPLOSIVE",
                AttackShape.Homing     => "· HOMING",
                _                      => "· WEAPON",
            };

        if (offer.Tome is not null) return offer.Tome is PerkDef ? "· PERK" : "· TOME";
        return offer.Item is not null ? "· ITEM" : "· SUPPLY";
    }

    // ------------------------------------------------------------- plumbing


}
