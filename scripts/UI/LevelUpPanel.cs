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
    private ColorRect _dimmer = null!;
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
        BuildCompact();      // LevelUpPanel.Compact.cs: the docked, non-pausing hand
        BuildToast();
        GameplaySettings.Load();

        // A run can end while a choice is open — a damage-over-time tick, or a
        // contact hit resolved in the same frame the level landed. Closing on
        // RunEnded stops a paused tree with a dead player sitting behind a menu
        // that nothing will ever dismiss.
        EventBus.Subscribe<RunEnded>(OnRunEnded);
        _subscribed = true;
    }

    public override void _ExitTree()
    {
        if (AnyOpen || AnyCompactOpen) MarkOpen(false);
        if (_holdingMouse) Actors.PlayerCamera.ReleaseCursor(this);
        _holdingMouse = false;
        if (!_subscribed) return;
        _subscribed = false;
        EventBus.Unsubscribe<RunEnded>(OnRunEnded);
    }

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

        // Settings > Gameplay > Auto-pick: resolved at once by the player's
        // rule, with a toast saying what was taken (LevelUpPanel.Compact.cs).
        if (GameplaySettings.AutoPickAt(Progression.Inventory.Current?.Level ?? 1) && AutoPickSeconds <= 0f)
        {
            AutoResolve(list, onPick);
            return;
        }

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

        // Chosen when a hand opens, not per card: a queue keeps one layout.
        if (!IsOpen)
        {
            _compactNow = WantsCompact;
            _nonModalNow = _compactNow || Net.Coop.Active && Net.Coop.InRun;
        }
        ApplyLayoutMode();
        LayoutCards();

        _banishMode = false;
        UpdateSubheading();
        RefreshControls();

        // The camera keeps the mouse captured for steering, which hides the
        // cursor -- so a mouse player could see the cards but not click them.
        // Free it while the panel is up and hand it back on close, but only if
        // it was captured to begin with: a player who had pressed Escape to get
        // the cursor back should not have it grabbed away by a level-up.
        //
        // Recorded once per stretch of hands, not per hand: a queued second
        // hand (or a pick that levels the player again) presents while IsOpen
        // is briefly false, and by then this panel has already freed the
        // cursor, so re-reading it would forget the capture and leave the
        // camera dead until the player clicked.
        // The hold (Actors/PlayerCamera) remembers the capture across panels.
        Actors.PlayerCamera.HoldCursor(this);
        _holdingMouse = true;
        _shownAtMsec = Time.GetTicksMsec();

        if (!IsOpen)
        {
            if (_nonModalNow) MarkOpen(true);
            else StartConfetti();
        }
        IsOpen = true;
        Visible = true;
        _autoPickLeft = AutoPickSeconds;
        // The compact hand never pauses or holds: the player walks on while
        // choosing. The full hand pauses (co-op: holds only this player).
        if (!_nonModalNow) Net.Coop.SetPaused(GetTree(), true);

        FocusFirstCard();
    }

    /// <summary>Focus the first card so that the keyboard and a gamepad have
    /// somewhere to start; the number keys work regardless. Painted explicitly
    /// because a second queued level-up re-presents onto a button that already
    /// has focus, so FocusEntered does not fire again. The compact hand takes no
    /// focus at all: Space (jump) and the arrow keys (movement) must never land
    /// on a card while the player is walking.</summary>
    private void FocusFirstCard()
    {
        if (_nonModalNow) return;
        _cards[0].Button.GrabFocus();
        Paint(_cards[0], true);
    }

    private bool _holdingMouse;

    /// <summary>Fill and position one card per current offer; hide the rest.
    /// Shared by opening the panel and by reroll/banish replacing cards.</summary>
    private void LayoutCards()
    {
        if (_compactNow)
        {
            LayoutCompact();
            return;
        }
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
            if (IsInsideTree() && !_nonModalNow) Net.Coop.SetPaused(GetTree(), false);
            return;
        }

        if (IsOpen || AnyOpen)
        {
            if (_nonModalNow) MarkOpen(false);
            else StopConfetti();
        }
        IsOpen = false;
        Visible = false;
        _banishMode = false;
        _current.Clear();
        _callback = null;

        for (int i = 0; i < _cards.Count; i++) _cards[i].Root.Visible = false;

        // Mouse-look works again the moment the hand closes (key, click,
        // skip or a run that ended): hand the cursor back if it was captured.
        if (_holdingMouse) Actors.PlayerCamera.ReleaseCursor(this);
        _holdingMouse = false;

        // The compact hand never paused, so it must not unpause either: the
        // pause menu or a shrine may hold the game right now.
        if (IsInsideTree() && !_nonModalNow) Net.Coop.SetPaused(GetTree(), false);
        _nonModalNow = false;
        _compactNow = false;
        _compact.Visible = false;
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
        if (_nonModalNow)
        {
            CompactInput(@event, key);   // LevelUpPanel.Compact.cs
            return;
        }

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
