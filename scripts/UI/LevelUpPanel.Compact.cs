using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Data;
using Wavebreak.Game;
using Wavebreak.Meta;
using Wavebreak.Progression;

namespace Wavebreak.UI;

/// <summary>
/// The compact level-up hand and the auto-pick toast.
///
/// Compact: small cards docked at the bottom of the screen, no dimmer, no
/// pause and no input hold, so the player keeps walking, jumping and dashing
/// while choosing. Co-op always uses it (the world never stops there anyway);
/// single-player uses it when Settings > Gameplay > Compact level-up is on.
///
/// Input rules while it is open, so picking never fights movement or weapons:
///   - number keys 1-4 pick, unless the player bound that key to a movement
///     action (then it moves, as bound);
///   - reroll / banish / skip keys work the same way;
///   - no card ever takes keyboard focus, so Space (jump) and the arrow keys
///     never activate one;
///   - clicks land on the cards and buttons only (the root ignores the mouse);
///     a click that misses them does not re-capture the mouse while it is open
///     (<see cref="Actors.PlayerCamera"/>), and the right button stays dash;
///   - while another menu has the game (pause menu, shrine, merchant) the hand
///     waits and ignores keys.
///
/// Auto-pick (<see cref="GameplaySettings.AutoPick"/>): the hand is resolved
/// at once by <see cref="Autopilot.PickByRule"/> and a toast names the card.
/// </summary>
public sealed partial class LevelUpPanel
{
    private const float CompactCardW = 212f;
    private const float CompactCardW4 = 176f;
    private const float CompactCardH = 118f;
    private const float CompactIcon = 46f;
    private const float CompactControlsW = 156f;
    private const float CompactBottomGap = 82f;   // clears the dash bar

    private static readonly string[] MoveActions = { "move_left", "move_right", "move_forward", "move_back", "jump", "dash", "slide", "interact", "pause", "stats" };

    private readonly List<Card> _compactCards = new(MaxCards);
    private PanelContainer _compact = null!;
    private HBoxContainer _compactRow = null!;
    private Label _compactHeader = null!;
    private Label _compactSub = null!;
    private Button _cReroll = null!, _cBanish = null!, _cSkip = null!;
    private Control _chrome = null!;
    private bool _compactNow;

    /// <summary>True when the next hand should be the compact one.</summary>
    public static bool WantsCompact => GameplaySettings.CompactLevelUp;
    private bool _nonModalNow;

    /// <summary>True while this hand holds the game paused (the full panel).
    /// The compact hand never does.</summary>
    public bool PausesGame => IsOpen && !_nonModalNow;

    /// <summary>True while the compact hand is on screen.</summary>
    public bool CompactOpen => IsOpen && _compactNow;

    /// <summary>Any compact hand is open (the camera reads this, so a click
    /// that misses a card does not grab the mouse).</summary>
    public static bool AnyCompactOpen { get; private set; }

    // ---------------------------------------------------------------- build

    private void BuildCompact()
    {
        // Everything the full panel drew so far (dimmer, titles, cards, the
        // control row) goes under one switch, so the compact mode can hide it.
        _chrome = new Control { Name = "Full", MouseFilter = MouseFilterEnum.Ignore };
        _chrome.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var full = new List<Node>(GetChildren());
        AddChild(_chrome);
        foreach (Node n in full)
        {
            RemoveChild(n);
            _chrome.AddChild(n);
        }

        _compact = new PanelContainer { Name = "Compact", Visible = false, MouseFilter = MouseFilterEnum.Ignore };
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.03f, 0.04f, 0.07f, 0.50f),
            ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 6, ContentMarginBottom = 8,
        };
        style.SetCornerRadiusAll(8);
        _compact.AddThemeStyleboxOverride("panel", style);
        // Bottom centre; LayoutCompact sizes it to the hand.
        _compact.AnchorLeft = 0.5f; _compact.AnchorRight = 0.5f;
        _compact.AnchorTop = 1f; _compact.AnchorBottom = 1f;
        _compact.GrowHorizontal = GrowDirection.Both;
        _compact.GrowVertical = GrowDirection.Begin;
        AddChild(_compact);

        var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", 4);
        _compact.AddChild(column);

        var head = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        head.AddThemeConstantOverride("separation", 12);
        _compactHeader = MakeLabel(Loc.T("LEVEL UP"), 18, Ink);
        _compactSub = MakeLabel("", 14, InkDim);
        _compactSub.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _compactSub.ClipText = true;
        head.AddChild(_compactHeader);
        head.AddChild(_compactSub);
        Label keys = MakeLabel(Loc.T("1 · 2 · 3 or click"), 13, new Color(0.62f, 0.68f, 0.78f));
        head.AddChild(keys);
        column.AddChild(head);

        var body = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        body.AddThemeConstantOverride("separation", 10);
        column.AddChild(body);

        _compactRow = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        _compactRow.AddThemeConstantOverride("separation", 8);
        body.AddChild(_compactRow);
        for (int i = 0; i < MaxCards; i++) _compactCards.Add(BuildCompactCard(i));

        var controls = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(CompactControlsW, 0f) };
        controls.AddThemeConstantOverride("separation", 4);
        body.AddChild(controls);
        _cReroll = CompactButton(controls, () => { if (Armed) DoReroll(); });
        _cBanish = CompactButton(controls, () => { if (Armed) ToggleBanish(); });
        _cSkip = CompactButton(controls, () => { if (Armed) DoSkip(); });
    }

    private Card BuildCompactCard(int index)
    {
        float w = CompactCardW;
        var root = new Control { MouseFilter = MouseFilterEnum.Ignore, Visible = false, CustomMinimumSize = new Vector2(w, CompactCardH) };
        _compactRow.AddChild(root);

        var back = new ColorRect { Color = CardBack with { A = 0.86f }, MouseFilter = MouseFilterEnum.Ignore };
        back.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        root.AddChild(back);

        var stripe = new ColorRect { Color = Ink, MouseFilter = MouseFilterEnum.Ignore };
        stripe.AnchorRight = 1f;
        stripe.OffsetBottom = 4f;
        root.AddChild(stripe);

        Label key = MakeLabel($"{index + 1}", 16, Ink);
        Inset(key, 6f, 6f, 20f, 22f);
        root.AddChild(key);

        IconView icon = IconView.Make(CompactIcon);
        Inset(icon, 24f, 10f, CompactIcon, CompactIcon);
        root.AddChild(icon);

        Label title = MakeLabel("", 14, Ink);
        title.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        title.MaxLinesVisible = 2;
        title.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        title.VerticalAlignment = VerticalAlignment.Center;
        title.AddThemeConstantOverride("line_spacing", -3);
        Inset(title, 76f, 5f, w - 82f, 40f);
        root.AddChild(title);

        Label rarity = MakeLabel("", 11, Ink);
        rarity.ClipText = true;
        Inset(rarity, 76f, 45f, w - 82f, 16f);
        root.AddChild(rarity);

        Label stats = MakeLabel("", 12, Ink);
        stats.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        stats.MaxLinesVisible = 3;
        stats.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        Inset(stats, 8f, 62f, w - 16f, CompactCardH - 66f);
        root.AddChild(stats);

        // Kept for the shared Card shape; the compact card has no room for them.
        Label flavour = MakeLabel("", 11, InkDim);
        flavour.Visible = false;
        root.AddChild(flavour);
        Label kind = MakeLabel("", 11, InkDim);
        kind.Visible = false;
        root.AddChild(kind);

        var button = new Button
        {
            Flat = true,
            Text = "",
            // Never focusable: Space and the arrows belong to the player's body.
            FocusMode = FocusModeEnum.None,
            MouseFilter = MouseFilterEnum.Stop,
            ButtonMask = MouseButtonMask.Left,
        };
        button.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        root.AddChild(button);

        var card = new Card(root, back, stripe, icon, rarity, title, stats, flavour, kind, button);
        int captured = index;
        button.Pressed += () => { if (Armed) Pick(captured); };
        button.MouseEntered += () => Paint(card, true);
        button.MouseExited += () => Paint(card, false);
        return card;
    }

    private static Button CompactButton(VBoxContainer column, Action pressed)
    {
        var b = new Button
        {
            FocusMode = FocusModeEnum.None,
            MouseFilter = MouseFilterEnum.Stop,
            ButtonMask = MouseButtonMask.Left,
            CustomMinimumSize = new Vector2(CompactControlsW, 34f),
            ClipText = true,
        };
        b.AddThemeFontSizeOverride("font_size", 13);
        b.AddThemeColorOverride("font_color", ControlInk);
        StyleBoxFlat Box(Color bg, Color border)
        {
            var s = new StyleBoxFlat { BgColor = bg, BorderColor = border };
            s.SetBorderWidthAll(1);
            s.SetCornerRadiusAll(5);
            return s;
        }
        b.AddThemeStyleboxOverride("normal", Box(new Color(0.10f, 0.22f, 0.32f, 0.88f), new Color(0.45f, 0.85f, 1.00f)));
        b.AddThemeStyleboxOverride("hover", Box(new Color(0.14f, 0.32f, 0.46f, 0.94f), new Color(0.70f, 0.95f, 1.00f)));
        b.AddThemeStyleboxOverride("pressed", Box(new Color(0.20f, 0.42f, 0.58f, 1.00f), Colors.White));
        b.AddThemeStyleboxOverride("disabled", Box(new Color(0.08f, 0.09f, 0.12f, 0.70f), new Color(0.25f, 0.28f, 0.34f)));
        b.Pressed += pressed;
        column.AddChild(b);
        return b;
    }

    // ---------------------------------------------------------------- layout

    /// <summary>Full or compact chrome for the hand that is opening. The root
    /// stops the mouse only in the full mode (its dimmer catches stray clicks);
    /// the compact root lets every click outside the cards through.</summary>
    private void ApplyLayoutMode()
    {
        _chrome.Visible = !_compactNow;
        _compact.Visible = _compactNow;
        MouseFilter = _nonModalNow ? MouseFilterEnum.Ignore : MouseFilterEnum.Stop;
        _dimmer.Visible = !_nonModalNow;
        foreach (Card card in _cards) card.Button.FocusMode = _nonModalNow ? FocusModeEnum.None : FocusModeEnum.All;
        foreach (Button button in new[] { _rerollButton, _banishButton, _skipButton })
            button.FocusMode = _nonModalNow ? FocusModeEnum.None : FocusModeEnum.All;
    }

    private void LayoutCompact()
    {
        int count = _current.Count;
        float w = count >= 4 ? CompactCardW4 : CompactCardW;
        for (int i = 0; i < _compactCards.Count; i++)
        {
            Card card = _compactCards[i];
            if (i >= count) { card.Root.Visible = false; continue; }
            card.Root.CustomMinimumSize = new Vector2(w, CompactCardH);
            card.Title.OffsetRight = card.Title.OffsetLeft + (w - 82f);
            card.Rarity.OffsetRight = card.Rarity.OffsetLeft + (w - 82f);
            card.Stats.OffsetRight = card.Stats.OffsetLeft + (w - 16f);
            FillCompact(card, _current[i], i);
            card.Root.Visible = true;
        }
        // Hug the content: width of the cards plus the control column.
        float width = count * w + (count - 1) * 8f + 10f + CompactControlsW + 20f;
        float height = CompactCardH + 24f + 4f + 14f;
        _compact.OffsetLeft = -width * 0.5f;
        _compact.OffsetRight = width * 0.5f;
        _compact.OffsetBottom = -CompactBottomGap;
        _compact.OffsetTop = -CompactBottomGap - height;
        SyncCompactControls();
        SyncCompactHeading();
    }

    private void FillCompact(Card card, Offer offer, int index)
    {
        Color colour = Catalog.RarityColor(offer.Rarity);
        card.Stripe.Color = colour;
        card.Icon.Set(Icons.For(offer), colour);
        card.Title.Text = Loc.T(offer.Title);
        card.Title.AddThemeColorOverride("font_color", offer.Tint.Lerp(Ink, 0.35f));
        card.Rarity.Text = $"{Loc.T(Catalog.RarityName(offer.Rarity))}  {Loc.T(KindOf(offer))}";
        card.Rarity.AddThemeColorOverride("font_color", colour);
        card.Stats.Text = EffectText(offer);
        card.Base = offer.Rarity switch
        {
            Rarity.Legendary => new Color(0.17f, 0.11f, 0.05f, 0.90f),
            Rarity.Epic => new Color(0.13f, 0.09f, 0.18f, 0.90f),
            _ => CardBack with { A = 0.86f },
        };
        Paint(card, false);
        card.Button.TooltipText = $"{index + 1}. {Loc.T(offer.Title)} — {offer.Effect}\n{Loc.T(offer.Description)}";
    }

    private void SyncCompactControls()
    {
        if (_cReroll is null) return;
        foreach (var (from, to) in new[] { (_rerollButton, _cReroll), (_banishButton, _cBanish), (_skipButton, _cSkip) })
        {
            to.Visible = from.Visible;
            to.Text = from.Text;
            to.Disabled = from.Disabled;
            to.TooltipText = from.TooltipText;
        }
        _cBanish.AddThemeColorOverride("font_color", _banishMode ? BanishInk : ControlInk);
    }

    private void SyncCompactHeading()
    {
        if (_compactSub is null) return;
        _compactSub.Text = _subheading.Text;
        _compactSub.AddThemeColorOverride("font_color", _banishMode ? BanishInk : InkDim);
    }

    /// <summary>What the confetti start/stop do for the full hand: the static
    /// flag and the HUD's event.</summary>
    private void MarkOpen(bool open)
    {
        AnyOpen = open;
        AnyCompactOpen = open;
        OpenChanged?.Invoke(open);
    }

    // ---------------------------------------------------------------- input

    private void CompactInput(InputEvent @event, InputEventKey key)
    {
        // Another menu has the game (pause menu, shrine, merchant): wait.
        if (Net.Coop.IsPaused(GetTree())) return;
        // A key the player bound to moving, jumping or dashing stays theirs.
        foreach (string action in MoveActions)
            if (InputMap.HasAction(action) && @event.IsAction(action)) return;

        int index = key.Keycode switch
        {
            Key.Key1 or Key.Kp1 => 0,
            Key.Key2 or Key.Kp2 => 1,
            Key.Key3 or Key.Kp3 => 2,
            Key.Key4 or Key.Kp4 => 3,
            _ => -1,
        };
        bool control = @event.IsActionPressed(RerollAction) || @event.IsActionPressed(BanishAction) || @event.IsActionPressed(SkipAction);
        if (index < 0 && !control) return;   // not ours: leave it to the game

        GetViewport().SetInputAsHandled();
        if (!Armed) return;
        if (control) { HandleControlKey(@event); return; }
        if (index < _current.Count) Pick(index);
    }

    // ---------------------------------------------------------------- auto-pick

    private Label _toast = null!;
    private double _toastLeft;

    private void BuildToast()
    {
        _toast = MakeLabel("", 17, new Color(1.00f, 0.90f, 0.55f), HorizontalAlignment.Center);
        _toast.Name = "AutoPickToast";
        _toast.AnchorLeft = 0.5f; _toast.AnchorRight = 0.5f;
        _toast.AnchorTop = 1f; _toast.AnchorBottom = 1f;
        _toast.OffsetLeft = -360f; _toast.OffsetRight = 360f;
        _toast.OffsetTop = -CompactBottomGap - 34f; _toast.OffsetBottom = -CompactBottomGap - 8f;
        _toast.Visible = false;
        _toast.ProcessMode = ProcessModeEnum.Always;
        // A sibling, not a child: the panel itself is hidden when no hand is up.
        Callable.From(() =>
        {
            if (IsInstanceValid(this) && GetParent() is { } parent && IsInstanceValid(_toast)) parent.AddChild(_toast);
        }).CallDeferred();
    }

    private void AutoResolve(List<Offer> hand, Action<Offer> onPick)
    {
        Offer chosen = Autopilot.PickByRule(hand, GameplaySettings.Rule, Main.Instance?.Run.Rng);
        if (Launch.Automated)
            GD.Print($"LEVELUP autopick rule={GameplaySettings.IdOf(GameplaySettings.Rule)} picked={chosen.Title} of={hand.Count}");
        // Same re-entrancy guard as a click: an offer that levels the player
        // again resolves that hand inside this call, which is fine here.
        onPick(chosen);
        ShowToast(Loc.F("Auto-picked: {0} ({1})", chosen.Title, Catalog.RarityName(chosen.Rarity)), Catalog.RarityColor(chosen.Rarity));
    }

    private void ShowToast(string text, Color colour)
    {
        if (_toast is null || !IsInstanceValid(_toast)) return;
        _toast.Text = text;
        _toast.AddThemeColorOverride("font_color", colour.Lerp(Colors.White, 0.35f));
        _toast.Visible = true;
        _toast.Modulate = Colors.White;
        _toastLeft = 2.6;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_toastLeft <= 0 || _toast is null || !IsInstanceValid(_toast)) return;
        _toastLeft -= delta;
        _toast.Modulate = new Color(1f, 1f, 1f, Mathf.Clamp((float)_toastLeft / 0.6f, 0f, 1f));
        if (_toastLeft <= 0) _toast.Visible = false;
    }
}
