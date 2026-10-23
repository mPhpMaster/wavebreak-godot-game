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

/// <summary>
/// The level-up choice: three cards, pick one, carry on.
///
/// This screen is where a survivors-like actually happens, so three things are
/// non-negotiable and are the reason the class is shaped the way it is.
///
/// It pauses the tree. Choosing under fire is not a decision, it is a reflex,
/// and the whole point of the offer is that the player weighs it.
/// <see cref="Node.ProcessModeEnum.Always"/> on this subtree is what lets it keep
/// taking input while everything else is stopped.
///
/// It queues. Two levels can land in the same frame — a boss dies, a hundred orbs
/// arrive at once — and the naive implementation shows the second set of offers
/// over the first, silently discarding a choice the player was owed. Offers go
/// into a queue and are presented one after another.
///
/// It is keyboard-first as well as mouse-first. 1/2/3 select; the cards are also
/// ordinary focusable buttons, so arrow keys, gamepad and click all work without
/// three separate code paths.
/// </summary>
public sealed partial class LevelUpPanel : Control
{
    private const int MaxCards = 4;
    private const float CardWidth = 300f;
    private const float CardHeight = 376f;
    private const float IconSize = 100f;
    private const float CardGap = 24f;

    private static readonly Color Dim = new(0.02f, 0.03f, 0.05f, 0.74f);
    private static readonly Color CardBack = new(0.09f, 0.10f, 0.14f, 0.98f);
    private static readonly Color CardHover = new(0.16f, 0.18f, 0.24f, 0.99f);
    private static readonly Color Ink = new(0.94f, 0.96f, 0.99f);
    private static readonly Color InkDim = new(0.66f, 0.71f, 0.80f);
    private static readonly Color Outline = new(0.02f, 0.03f, 0.05f, 0.9f);

    private readonly List<Card> _cards = new(MaxCards);
    private readonly List<Offer> _current = new(MaxCards);
    private readonly Queue<Pending> _queue = new();

    private Label _heading = null!;
    private Label _subheading = null!;
    private Action<Offer>? _callback;
    private bool _subscribed;
    private bool _resolving;

    /// <summary>
    /// Seconds before an open panel chooses for itself. Zero — the default —
    /// means it waits forever, which is what a human wants.
    ///
    /// This exists for the capture harness and <c>tests/smoke.ps1</c>. Without it
    /// an unattended run stops dead at the first level-up: the tree pauses, no
    /// input ever arrives, and every "20 second" smoke run silently reports
    /// whatever the game looked like fifteen seconds in. A test that cannot get
    /// past minute one cannot tell you anything about minute ten.
    ///
    /// Set with <c>--auto-pick=0.4</c> after the double dash.
    /// </summary>
    public float AutoPickSeconds { get; set; }

    private float _autoPickLeft;

    /// <summary>True while the player owes the game a decision.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>One deferred set of offers and the callback that consumes it.</summary>
    private readonly struct Pending
    {
        public readonly List<Offer> Offers;
        public readonly Action<Offer> Callback;
        public Pending(List<Offer> offers, Action<Offer> callback) { Offers = offers; Callback = callback; }
    }

    // ------------------------------------------------------------ lifecycle

    public override void _Ready()
    {
        Name = "LevelUpPanel";
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // Input and processing must survive the pause this screen itself applies.
        ProcessMode = ProcessModeEnum.Always;

        // Hidden panels still hit-test in Godot unless they are actually hidden,
        // so visibility is the switch rather than a transparency fade.
        Visible = false;

        ParseAutoPick();

        BuildChrome();
        BuildConfetti();
        for (int i = 0; i < MaxCards; i++) _cards.Add(BuildCard(i));
        BuildControls();

        // A run can end while a choice is open — a damage-over-time tick, or a
        // contact hit resolved in the same frame the level landed. Closing on
        // RunEnded stops a paused tree with a dead player sitting behind a menu
        // that nothing will ever dismiss.
        EventBus.Subscribe<RunEnded>(OnRunEnded);
        _subscribed = true;
    }

    public override void _ExitTree()
    {
        AnyOpen = false;     // a scene change under an open hand must not leave it set
        if (!_subscribed) return;
        _subscribed = false;
        EventBus.Unsubscribe<RunEnded>(OnRunEnded);
    }

    // ---------------------------------------------------------------- build

    private void BuildChrome()
    {
        var dim = new ColorRect { Color = Dim };
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

    /// <summary>
    /// Present a set of offers and call back with the one chosen.
    ///
    /// Takes <see cref="IEnumerable{T}"/> rather than a list because the offer
    /// pool's return type is its own business; the sequence is copied on entry so
    /// nothing the caller does to it afterwards can change what is on screen.
    /// </summary>
    /// <param name="offers">What to show. More than <c>MaxCards</c> entries are
    /// ignored; zero entries resolve immediately without pausing.</param>
    /// <param name="onPick">Invoked once, with the chosen offer, before the game
    /// resumes.</param>
    public void Show(IEnumerable<Offer>? offers, Action<Offer> onPick)
    {
        ArgumentNullException.ThrowIfNull(onPick);
        if (offers is null) return;

        var list = new List<Offer>(MaxCards);
        foreach (Offer offer in offers)
        {
            list.Add(offer);
            if (list.Count >= MaxCards) break;
        }

        // An empty roll means the pool had nothing left to give. Pausing the game
        // to show the player zero choices is strictly worse than not stopping.
        if (list.Count == 0) return;

        _queue.Enqueue(new Pending(list, onPick));

        // _resolving guards the re-entrant case: an offer whose effect grants
        // experience levels the player from inside the callback, which calls
        // straight back into Show. Without the guard that inner call presents the
        // next set, and the outer Pick then presents a *second* set on top of it,
        // burning a choice the player never saw.
        if (!IsOpen && !_resolving) Present();
    }

    /// <summary>Close without choosing and drop anything still queued. Used when
    /// the run ends underneath an open panel.</summary>
    public void Cancel()
    {
        _queue.Clear();
        Close();
    }

    private void Present()
    {
        if (_queue.Count == 0)
        {
            Close();
            return;
        }

        Pending next = _queue.Dequeue();
        _current.Clear();
        _current.AddRange(next.Offers);
        _callback = next.Callback;

        LayoutCards();

        _banishMode = false;
        UpdateSubheading();
        RefreshControls();

        // The camera keeps the mouse captured for steering, which hides the
        // cursor -- so a mouse player could see the cards but not click them.
        // Free it while the panel is up and hand it back on close, but only if
        // it was captured to begin with: a player who had pressed Escape to get
        // the cursor back should not have it grabbed away by a level-up.
        if (!IsOpen) _mouseWasCaptured = Input.MouseMode == Input.MouseModeEnum.Captured;
        _shownAtMsec = Time.GetTicksMsec();
        Input.MouseMode = Input.MouseModeEnum.Visible;

        if (!IsOpen) StartConfetti();
        IsOpen = true;
        Visible = true;
        _autoPickLeft = AutoPickSeconds;
        GetTree().Paused = true;

        // Focus the first card so that the keyboard and a gamepad have somewhere
        // to start; the number keys work regardless. Painted explicitly because a
        // second queued level-up re-presents onto a button that already has focus,
        // so FocusEntered does not fire again.
        _cards[0].Button.GrabFocus();
        Paint(_cards[0], true);
    }

    private bool _mouseWasCaptured;

    /// <summary>Fill and position one card per current offer; hide the rest.
    /// Shared by opening the panel and by reroll/banish replacing cards.</summary>
    private void LayoutCards()
    {
        int count = _current.Count;
        float totalWidth = count * CardWidth + (count - 1) * CardGap;
        float left = -totalWidth * 0.5f;

        for (int i = 0; i < _cards.Count; i++)
        {
            Card card = _cards[i];
            if (i >= count)
            {
                card.Root.Visible = false;
                continue;
            }

            Fill(card, _current[i], i);
            card.Root.OffsetLeft = left + i * (CardWidth + CardGap);
            card.Root.OffsetRight = card.Root.OffsetLeft + CardWidth;
            card.Root.Visible = true;
        }
    }

    private void Close()
    {
        if (!IsOpen && !Visible)
        {
            // Still make sure the tree is not left paused by a panel that never
            // opened — cheap, and the failure it prevents is a frozen game.
            if (IsInsideTree()) GetTree().Paused = false;
            return;
        }

        if (IsOpen) StopConfetti();
        IsOpen = false;
        Visible = false;
        _banishMode = false;
        _current.Clear();
        _callback = null;

        for (int i = 0; i < _cards.Count; i++) _cards[i].Root.Visible = false;

        if (_mouseWasCaptured) Actors.PlayerCamera.TryCaptureMouse();
        _mouseWasCaptured = false;

        if (IsInsideTree()) GetTree().Paused = false;
    }

    private void OnRunEnded(RunEnded e) => Cancel();

    // ------------------------------------------------------------ auto-pick

    private void ParseAutoPick()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg == "--auto-pick") { AutoPickSeconds = 0.35f; continue; }
            if (!arg.StartsWith("--auto-pick=")) continue;

            float seconds = arg["--auto-pick=".Length..].ToFloat();
            AutoPickSeconds = Mathf.Max(0.05f, seconds);
        }
    }

    /// <summary>
    /// Runs the auto-pick countdown. Nothing else needs a frame callback here —
    /// the panel is entirely event-driven otherwise — so the whole method is a
    /// no-op in an ordinary game.
    /// </summary>
    public override void _Process(double delta)
    {
        if (!IsOpen || AutoPickSeconds <= 0f || _current.Count == 0) return;

        _autoPickLeft -= (float)delta;
        if (_autoPickLeft > 0f) return;

        // Chosen from the run's seeded source rather than always taking card one:
        // a capture that picks the first offer every time builds the same run
        // every time, and never exercises a weapon that happens to roll third.
        Rng? rng = Main.Instance?.Run.Rng;
        _banishMode = false;
        Pick(rng?.RangeInt(0, _current.Count) ?? 0);
    }

    // ---------------------------------------------------------------- input

    /// <summary>
    /// The panel ignores input for this long after it opens. A level-up lands in
    /// the middle of a fight, while Space (jump, and also ui_accept on the
    /// focused card) and the number keys are in use for other things; without
    /// the delay, a jump pressed a frame after the panel appears picks the
    /// first card on the player's behalf.
    /// </summary>
    private const ulong ArmDelayMsec = 400;

    private ulong _shownAtMsec;

    private bool Armed => Time.GetTicksMsec() - _shownAtMsec >= ArmDelayMsec;

    /// <summary>For the input-guard check's failure line.</summary>
    public string DebugState => $"armed={Armed} sinceShow={Time.GetTicksMsec() - _shownAtMsec}ms banish={_banishMode} cards={_current.Count} queued={_queue.Count}";

    public override void _Input(InputEvent @event)
    {
        if (!IsOpen || DevPanel.IsOpen) return;
        if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;

        if (!Armed)
        {
            // Swallow it, so it cannot reach the focused card as an activation
            // either. Escape is left alone: it frees the mouse.
            if (key.Keycode != Key.Escape) GetViewport().SetInputAsHandled();
            return;
        }

        // Reroll / banish / skip. After the arm check, so a key held (or
        // mashed) as the panel opens cannot spend a charge.
        if (HandleControlKey(@event))
        {
            GetViewport().SetInputAsHandled();
            return;
        }

        int index = key.Keycode switch
        {
            Key.Key1 or Key.Kp1 => 0,
            Key.Key2 or Key.Kp2 => 1,
            Key.Key3 or Key.Kp3 => 2,
            Key.Key4 or Key.Kp4 => 3,
            _ => -1,
        };

        if (index < 0 || index >= _current.Count) return;

        // Consume it, or the same press also reaches the focused button as an
        // activation and the player spends two offers on one keystroke.
        GetViewport().SetInputAsHandled();
        Pick(index);
    }

    private void Pick(int index)
    {
        if (!IsOpen || index < 0 || index >= _current.Count) return;

        // In banish mode the card the player points at is removed from the pool
        // and replaced, rather than taken.
        if (_banishMode)
        {
            BanishAt(index);
            return;
        }

        Offer chosen = _current[index];
        Action<Offer>? callback = _callback;

        // Clear the current selection *before* invoking, so a callback that
        // levels the player again queues cleanly instead of re-entering into a
        // half-torn-down state.
        _current.Clear();
        _callback = null;
        IsOpen = false;

        _resolving = true;
        try
        {
            callback?.Invoke(chosen);
        }
        finally
        {
            _resolving = false;
        }

        Present();
    }

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

        if (offer.Tome is not null) return "· TOME";
        return offer.Item is not null ? "· ITEM" : "· SUPPLY";
    }

    // ------------------------------------------------------------- plumbing

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

    private static Label MakeLabel(string text, int size, Color color,
                                   HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var label = new Label
        {
            Text = text,
            HorizontalAlignment = align,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeColorOverride("font_outline_color", Outline);
        label.AddThemeConstantOverride("outline_size", 5);
        return label;
    }

    /// <summary>Centre a control on the screen's middle and give it an explicit
    /// size. Anchoring to the centre rather than to the top-left is what keeps the
    /// card row centred at any window size without a container.</summary>
    private static void Band(Control control, float width, float top, float height)
    {
        control.AnchorLeft = 0.5f;
        control.AnchorRight = 0.5f;
        control.AnchorTop = 0.5f;
        control.AnchorBottom = 0.5f;
        control.OffsetLeft = -width * 0.5f;
        control.OffsetRight = width * 0.5f;
        control.OffsetTop = top;
        control.OffsetBottom = top + height;
    }

    /// <summary>Place a control at a fixed rectangle inside its parent's top-left.</summary>
    private static void Inset(Control control, float left, float top, float width, float height)
    {
        control.AnchorLeft = 0f; control.AnchorRight = 0f;
        control.AnchorTop = 0f; control.AnchorBottom = 0f;
        control.OffsetLeft = left;
        control.OffsetRight = left + width;
        control.OffsetTop = top;
        control.OffsetBottom = top + height;
    }
}
