using System.Collections.Generic;
using Godot;
using Wavebreak.Meta;
using Wavebreak.Progression;

namespace Wavebreak.UI;

/// <summary>
/// Reroll, Banish and Skip on the level-up panel.
///
/// The charges live in <see cref="LevelUpChoices"/> (as stats, so items and
/// unlocks can add more); this half of the panel only draws three buttons with
/// their remaining counts and routes the keys and clicks.
///
/// All three respect the same arm delay as the cards: a key that was held, or
/// mashed, as the panel opened cannot spend a charge. Reroll and banish re-arm
/// briefly afterwards so a double-tapped number key does not take a card the
/// player has not read yet.
///
/// Banish is two-step: press Banish, then choose the card to remove (click or
/// 1-3). Press Banish again to back out without spending it.
/// </summary>
public sealed partial class LevelUpPanel
{
    public const string RerollAction = "levelup_reroll";
    public const string BanishAction = "levelup_banish";
    public const string SkipAction = "levelup_skip";

    /// <summary>After a reroll or banish, the cards ignore input for this long.</summary>
    private const ulong ReArmMsec = 220;

    private static readonly Color ControlInk = new(0.86f, 0.90f, 0.97f);
    private static readonly Color BanishInk = new(1.00f, 0.50f, 0.45f);

    private Button _rerollButton = null!;
    private Button _banishButton = null!;
    private Button _skipButton = null!;
    private bool _banishMode;

    private void BuildControls()
    {
        EnsureAction(RerollAction, Key.R);
        EnsureAction(BanishAction, Key.B);
        EnsureAction(SkipAction, Key.X);

        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 18);
        Band(row, 720f, 232f, 40f);
        AddChild(row);

        _rerollButton = ControlButton(row);
        _banishButton = ControlButton(row);
        _skipButton = ControlButton(row);

        _rerollButton.Pressed += () => { if (Armed) DoReroll(); };
        _banishButton.Pressed += () => { if (Armed) ToggleBanish(); };
        _skipButton.Pressed += () => { if (Armed) DoSkip(); };
    }

    private static Button ControlButton(HBoxContainer row)
    {
        var b = new Button
        {
            // Not focusable: arrow keys and the gamepad stay on the cards, and
            // ui_accept can never land on a control button by accident.
            FocusMode = FocusModeEnum.None,
            MouseFilter = MouseFilterEnum.Stop,
            CustomMinimumSize = new Vector2(210f, 44f),
        };
        b.AddThemeFontSizeOverride("font_size", 17);
        b.AddThemeColorOverride("font_color", ControlInk);
        // A bright framed button: the old flat dark one was easy to miss under the cards.
        StyleBoxFlat Box(Color bg, Color border)
        {
            var s = new StyleBoxFlat { BgColor = bg, BorderColor = border };
            s.SetBorderWidthAll(2);
            s.SetCornerRadiusAll(6);
            return s;
        }
        b.AddThemeStyleboxOverride("normal", Box(new Color(0.10f, 0.22f, 0.32f, 0.96f), new Color(0.45f, 0.85f, 1.00f)));
        b.AddThemeStyleboxOverride("hover", Box(new Color(0.14f, 0.32f, 0.46f, 0.98f), new Color(0.70f, 0.95f, 1.00f)));
        b.AddThemeStyleboxOverride("pressed", Box(new Color(0.20f, 0.42f, 0.58f, 1.00f), Colors.White));
        b.AddThemeStyleboxOverride("disabled", Box(new Color(0.08f, 0.09f, 0.12f, 0.80f), new Color(0.25f, 0.28f, 0.34f)));
        row.AddChild(b);
        return b;
    }

    /// <summary>Make sure an action exists, so an old project file (or a merge
    /// that dropped the input entries) still has working keys.</summary>
    private static void EnsureAction(string action, Key key)
    {
        if (InputMap.HasAction(action)) return;
        InputMap.AddAction(action);
        InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
    }

    private static string KeyName(string action)
        => InputMap.HasAction(action) ? Keybinds.Describe(Keybinds.Get(action, 0)) : "?";

    /// <summary>Redraw the three buttons from the live charges.</summary>
    private void RefreshControls()
    {
        LevelUpChoices? choices = LevelUpChoices.Current;
        bool any = choices is not null;
        _rerollButton.Visible = _banishButton.Visible = _skipButton.Visible = any;
        if (choices is null) return;

        _rerollButton.Text = Loc.F("REROLL [{0}]   {1}", KeyName(RerollAction), choices.RerollsLeft);
        _banishButton.Text = _banishMode
            ? Loc.F("CANCEL [{0}]", KeyName(BanishAction))
            : Loc.F("BANISH [{0}]   {1}", KeyName(BanishAction), choices.BanishesLeft);
        _skipButton.Text = Loc.F("SKIP [{0}]   {1}", KeyName(SkipAction), choices.SkipsLeft);

        _rerollButton.Disabled = choices.RerollsLeft <= 0;
        _banishButton.Disabled = !_banishMode && choices.BanishesLeft <= 0;
        _skipButton.Disabled = choices.SkipsLeft <= 0;

        _rerollButton.TooltipText = Loc.T("Draw three new cards.");
        _banishButton.TooltipText = Loc.T("Remove one card from the pool for the rest of the run, and draw a replacement.");
        _skipButton.TooltipText = Loc.F("Skip this level for {0} gold.", LevelUpChoices.SkipGold(Progression.Inventory.Current?.Level ?? 1));

        _banishButton.AddThemeColorOverride("font_color", _banishMode ? BanishInk : ControlInk);
    }

    private void UpdateSubheading(string? message = null)
    {
        if (message is not null)
        {
            _subheading.Text = message;
        }
        else if (_banishMode)
        {
            _subheading.Text = Loc.T("Banish which card?  It will not be offered again this run");
        }
        else
        {
            _subheading.Text = _queue.Count > 0
                ? Loc.F("Choose one   ({0} more to come)", _queue.Count)
                : Loc.T("Choose one");
        }
        _subheading.AddThemeColorOverride("font_color", _banishMode ? BanishInk : InkDim);
    }

    /// <summary>Route a reroll/banish/skip key. True when it was one of them.</summary>
    private bool HandleControlKey(InputEvent @event)
    {
        if (@event.IsActionPressed(RerollAction)) { DoReroll(); return true; }
        if (@event.IsActionPressed(BanishAction)) { ToggleBanish(); return true; }
        if (@event.IsActionPressed(SkipAction)) { DoSkip(); return true; }
        return false;
    }

    private void ReArm()
    {
        ulong now = Time.GetTicksMsec();
        ulong back = ArmDelayMsec - ReArmMsec;
        _shownAtMsec = now > back ? now - back : 0;
    }

    private void DoReroll()
    {
        if (!IsOpen || _current.Count == 0 || LevelUpChoices.Current is not { } choices) return;
        if (!choices.TryReroll(_current, out List<Offer> fresh) || fresh.Count == 0) return;

        _banishMode = false;
        _current.Clear();
        _current.AddRange(fresh);
        LayoutCards();
        UpdateSubheading();
        RefreshControls();
        ReArm();
        _cards[0].Button.GrabFocus();
        Paint(_cards[0], true);
    }

    private void ToggleBanish()
    {
        if (!IsOpen || LevelUpChoices.Current is not { } choices) return;
        if (!_banishMode && choices.BanishesLeft <= 0) return;
        _banishMode = !_banishMode;
        UpdateSubheading();
        RefreshControls();
    }

    private void BanishAt(int index)
    {
        _banishMode = false;
        if (LevelUpChoices.Current is not { } choices || index < 0 || index >= _current.Count)
        {
            UpdateSubheading();
            RefreshControls();
            return;
        }

        Offer target = _current[index];
        if (!LevelUpChoices.CanBanish(target))
        {
            UpdateSubheading(Loc.T("Supply payouts cannot be banished"));
            RefreshControls();
            return;
        }

        var others = new List<Offer>(_current);
        others.RemoveAt(index);
        if (choices.TryBanish(target, others, out Offer replacement))
        {
            _current[index] = replacement;
            LayoutCards();
            UpdateSubheading(Loc.F("{0} banished", target.Title));
            ReArm();
        }
        else
        {
            UpdateSubheading();
        }
        RefreshControls();
    }

    private void DoSkip()
    {
        if (!IsOpen || LevelUpChoices.Current is not { } choices) return;
        if (!choices.TrySkip()) return;

        // Same teardown as a pick, minus the callback: the level is simply passed.
        _banishMode = false;
        _current.Clear();
        _callback = null;
        IsOpen = false;
        Present();
    }
}
