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
    // ---------------------------------------------------------------- build

    private void BuildChrome()
    {
        var dim = _dimmer = new ColorRect { Color = Dim };
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        // Stop, not Ignore: the dimmer is what prevents a click that misses a card
        // from reaching whatever is underneath it.
        dim.MouseFilter = MouseFilterEnum.Stop;
        AddChild(dim);

        _heading = MakeLabel(Loc.T("LEVEL UP"), 44, Ink, HorizontalAlignment.Center);
        Band(_heading, 720f, -264f, 60f);
        AddChild(_heading);

        _subheading = MakeLabel(Loc.T("Choose one"), 18, InkDim, HorizontalAlignment.Center);
        Band(_subheading, 720f, -208f, 28f);
        AddChild(_subheading);

        Label hint = MakeLabel(Loc.T("press  1  ·  2  ·  3"), 15, new Color(0.50f, 0.55f, 0.64f), HorizontalAlignment.Center);
        Band(hint, 720f, 204f, 22f);
        AddChild(hint);
    }

    /// <summary>
    /// Builds one card and its click target.
    ///
    /// The card is a plain <see cref="ColorRect"/> with labels laid out inside it
    /// and a flat <see cref="Button"/> stretched over the top. A Button cannot
    /// arrange children, and a Control with a manual <c>_GuiInput</c> would have
    /// to reimplement focus, hover and keyboard activation — the overlay gets all
    /// three for free and keeps the layout code layout code.
    /// </summary>
    private Card BuildCard(int index)
    {
        var root = new Control { MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        Band(root, CardWidth, -176f, CardHeight);
        AddChild(root);

        var back = new ColorRect { Color = CardBack, MouseFilter = MouseFilterEnum.Ignore };
        back.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        root.AddChild(back);

        // The rarity stripe is the only element on the card that is pure colour.
        // Colour alone is not an accessible signal, which is why the rarity is
        // also spelled out underneath it.
        var stripe = new ColorRect { Color = Ink, MouseFilter = MouseFilterEnum.Ignore };
        stripe.AnchorLeft = 0f; stripe.AnchorRight = 1f;
        stripe.AnchorTop = 0f; stripe.AnchorBottom = 0f;
        stripe.OffsetTop = 0f; stripe.OffsetBottom = 7f;
        root.AddChild(stripe);

        Label key = MakeLabel($"{index + 1}", 17, InkDim);
        Inset(key, 14f, 14f, 40f, 24f);
        root.AddChild(key);

        Label rarity = MakeLabel("", 14, Ink, HorizontalAlignment.Center);
        Inset(rarity, 0f, 16f, CardWidth, 20f);
        root.AddChild(rarity);

        // The picture comes first: it is what tells three cards apart at a
        // glance, before any of the text is read.
        IconView icon = IconView.Make(IconSize);
        Inset(icon, (CardWidth - IconSize) * 0.5f, 34f, IconSize, IconSize);
        root.AddChild(icon);

        Label title = MakeLabel("", 22, Ink, HorizontalAlignment.Center);
        title.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        title.VerticalAlignment = VerticalAlignment.Center;
        Inset(title, 12f, 136f, CardWidth - 24f, 54f);
        root.AddChild(title);

        var rule = new ColorRect { Color = new Color(1f, 1f, 1f, 0.12f), MouseFilter = MouseFilterEnum.Ignore };
        Inset(rule, 22f, 193f, CardWidth - 44f, 1f);
        root.AddChild(rule);

        Label stats = MakeLabel("", 15, Ink, HorizontalAlignment.Center);
        stats.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        stats.VerticalAlignment = VerticalAlignment.Center;
        Inset(stats, 16f, 180f, CardWidth - 32f, 94f);
        root.AddChild(stats);

        // Stats and flavour are re-flowed per offer (FlowText); the flavour is
        // capped in lines and trimmed with an ellipsis rather than overflowing.
        Label flavour = MakeLabel("", 14, InkDim, HorizontalAlignment.Center);
        flavour.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        flavour.VerticalAlignment = VerticalAlignment.Top;
        flavour.MaxLinesVisible = 2;
        flavour.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        Inset(flavour, 16f, 280f, CardWidth - 32f, 42f);
        root.AddChild(flavour);

        Label kind = MakeLabel("", 13, InkDim, HorizontalAlignment.Center);
        Inset(kind, 0f, CardHeight - 26f, CardWidth, 20f);
        root.AddChild(kind);

        var button = new Button
        {
            Flat = true,
            Text = "",
            FocusMode = FocusModeEnum.All,
            MouseFilter = MouseFilterEnum.Stop,
        };
        button.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        root.AddChild(button);

        var card = new Card(root, back, stripe, icon, rarity, title, stats, flavour, kind, button);

        int captured = index;
        button.Pressed += () => { if (Armed) Pick(captured); };

        // Highlight lightens whatever the card's body already is rather than
        // replacing it with one fixed colour — otherwise focusing a Legendary
        // wipes out the warm background that identifies it, and the card the
        // player is looking at is the one card that stops looking special.
        //
        // Mouse-exit falls back to the focus state, not to plain, so moving the
        // pointer away from a keyboard-focused card does not un-highlight it.
        button.MouseEntered += () => Paint(card, true);
        button.MouseExited += () => Paint(card, button.HasFocus());
        button.FocusEntered += () => Paint(card, true);
        button.FocusExited += () => Paint(card, false);

        return card;
    }

    private static void Paint(Card card, bool highlighted)
        => card.Back.Color = highlighted ? card.Base.Lerp(Colors.White, 0.10f) : card.Base;

    // ----------------------------------------------------------------- show


}
