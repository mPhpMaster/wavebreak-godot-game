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
    /// <summary>The widgets of one card, gathered so that filling one is a set of
    /// assignments rather than a tree walk.</summary>
    private sealed class Card
    {
        public readonly Control Root;
        public readonly ColorRect Back;
        public readonly ColorRect Stripe;
        public readonly IconView Icon;
        public readonly Label Rarity;
        public readonly Label Title;
        public readonly Label Stats;
        public readonly Label Flavour;
        public readonly Label Kind;
        public readonly Button Button;

        /// <summary>The card's unhighlighted body colour, set per offer from its
        /// rarity. Hover and focus lighten this rather than replacing it.</summary>
        public Color Base = new(0.09f, 0.10f, 0.14f, 0.98f);

        public Card(Control root, ColorRect back, ColorRect stripe, IconView icon, Label rarity, Label title,
                    Label stats, Label flavour, Label kind, Button button)
        {
            Root = root; Back = back; Stripe = stripe; Icon = icon; Rarity = rarity; Title = title;
            Stats = stats; Flavour = flavour; Kind = kind; Button = button;
        }
    }


    /// <summary>One deferred set of offers and the callback that consumes it.</summary>
    private readonly struct Pending
    {
        public readonly List<Offer> Offers;
        public readonly Action<Offer> Callback;
        public Pending(List<Offer> offers, Action<Offer> callback) { Offers = offers; Callback = callback; }
    }

}
