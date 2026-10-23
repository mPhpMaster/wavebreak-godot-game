using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Core;
using Wavebreak.Data;
using Wavebreak.Game;

namespace Wavebreak.UI;

/// <summary>
/// The charge shrine's blessing choice: three small cards, pick one.
///
/// Built like <see cref="LevelUpPanel"/> and for the same reasons: it pauses the
/// tree so the choice is a decision, frees a captured mouse and hands it back,
/// takes 1/2/3, click, arrows or a gamepad, and ignores input for
/// <see cref="ArmDelayMsec"/> after opening so a jump pressed as the ring fills
/// does not pick a card. It never opens on top of the level-up panel: a blessing
/// that arrives while a level-up is up waits for it to close.
/// </summary>
public sealed partial class ShrinePanel : Control
{
    private const float CardWidth = 250f;
    private const float CardHeight = 190f;
    private const float CardGap = 20f;
    private const int MaxCards = 3;

    /// <summary>Same guard as the level-up panel.</summary>
    private const ulong ArmDelayMsec = 400;

    private static readonly Color Dim = new(0.02f, 0.03f, 0.05f, 0.66f);
    private static readonly Color CardBack = new(0.08f, 0.11f, 0.15f, 0.98f);
    private static readonly Color Ink = new(0.94f, 0.96f, 0.99f);
    private static readonly Color InkDim = new(0.66f, 0.71f, 0.80f);
    private static readonly Color Outline = new(0.02f, 0.03f, 0.05f, 0.9f);

    private readonly List<(Control Root, ColorRect Back, ColorRect Stripe, Label Rarity, Label Title, Label Effect, Button Button)> _cards = new();
    private readonly Queue<(List<ShrineBoon> Boons, Action<ShrineBoon> Pick)> _queue = new();
    private List<ShrineBoon> _current = new();
    private Action<ShrineBoon>? _callback;
    private Label _heading = null!;
    private bool _mouseWasCaptured;
    private ulong _shownAtMsec;
    private float _autoPick;
    private float _autoPickLeft;
    private LevelUpPanel? _levelUp;
    private bool _subscribed;

    public bool IsOpen { get; private set; }

    /// <summary>True while a choice is open or waiting to open.</summary>
    public bool Busy => IsOpen || _queue.Count > 0;

    private bool Armed => Time.GetTicksMsec() - _shownAtMsec >= ArmDelayMsec;

    public override void _Ready()
    {
        Name = "ShrinePanel";
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        ProcessMode = ProcessModeEnum.Always;
        Visible = false;

        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg == "--auto-pick") _autoPick = 0.35f;
            else if (arg.StartsWith("--auto-pick=")) _autoPick = Mathf.Max(0.05f, arg["--auto-pick=".Length..].ToFloat());
        }

        var dim = new ColorRect { Color = Dim, MouseFilter = MouseFilterEnum.Stop };
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(dim);

        _heading = MakeLabel("SHRINE CHARGED", 38, MapShrines.Tint(MapShrineKind.Charge).Lightened(0.3f));
        Band(_heading, 720f, -190f, 54f);
        AddChild(_heading);
        Label sub = MakeLabel("Choose a blessing for this run", 17, InkDim);
        Band(sub, 720f, -136f, 26f);
        AddChild(sub);
        Label hint = MakeLabel("press  1  ·  2  ·  3", 15, new Color(0.50f, 0.55f, 0.64f));
        Band(hint, 720f, 130f, 24f);
        AddChild(hint);

        for (int i = 0; i < MaxCards; i++) BuildCard(i);

        EventBus.Subscribe<RunEnded>(OnRunEnded);
        _subscribed = true;
    }

    public override void _ExitTree()
    {
        if (_subscribed) EventBus.Unsubscribe<RunEnded>(OnRunEnded);
        _subscribed = false;
    }

    private void BuildCard(int index)
    {
        var root = new Control { MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        Band(root, CardWidth, -CardHeight * 0.5f + 10f, CardHeight);
        AddChild(root);

        var back = new ColorRect { Color = CardBack, MouseFilter = MouseFilterEnum.Ignore };
        back.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        root.AddChild(back);

        var stripe = new ColorRect { Color = Ink, MouseFilter = MouseFilterEnum.Ignore };
        Inset(stripe, 0f, 0f, CardWidth, 6f);
        root.AddChild(stripe);

        Label key = MakeLabel($"{index + 1}", 16, InkDim, HorizontalAlignment.Left);
        Inset(key, 12f, 12f, 30f, 22f);
        root.AddChild(key);

        Label rarity = MakeLabel("", 14, Ink);
        Inset(rarity, 0f, 16f, CardWidth, 20f);
        root.AddChild(rarity);

        Label title = MakeLabel("", 22, Ink);
        title.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        Inset(title, 10f, 46f, CardWidth - 20f, 56f);
        root.AddChild(title);

        Label effect = MakeLabel("", 20, Ink);
        Inset(effect, 10f, 112f, CardWidth - 20f, 40f);
        root.AddChild(effect);

        var button = new Button { Flat = true, FocusMode = FocusModeEnum.All, MouseFilter = MouseFilterEnum.Stop };
        button.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        root.AddChild(button);

        int captured = index;
        button.Pressed += () => { if (Armed) Pick(captured); };
        button.MouseEntered += () => back.Color = CardBack.Lerp(Colors.White, 0.10f);
        button.MouseExited += () => back.Color = button.HasFocus() ? CardBack.Lerp(Colors.White, 0.10f) : CardBack;
        button.FocusEntered += () => back.Color = CardBack.Lerp(Colors.White, 0.10f);
        button.FocusExited += () => back.Color = CardBack;

        _cards.Add((root, back, stripe, rarity, title, effect, button));
    }

    /// <summary>Queue a choice; it opens as soon as no level-up panel is up.</summary>
    public void Show(List<ShrineBoon> boons, Action<ShrineBoon> onPick)
    {
        if (boons.Count == 0) return;
        _queue.Enqueue((boons, onPick));
        TryPresent();
    }

    private LevelUpPanel? LevelUp
    {
        get
        {
            if (_levelUp is null || !IsInstanceValid(_levelUp))
                _levelUp = GetTree()?.Root.FindChild("LevelUpPanel", true, false) as LevelUpPanel;
            return _levelUp;
        }
    }

    private void TryPresent()
    {
        if (IsOpen || _queue.Count == 0) return;
        if (LevelUp is { IsOpen: true }) return;

        var (boons, pick) = _queue.Dequeue();
        _current = boons;
        _callback = pick;

        int count = Math.Min(MaxCards, _current.Count);
        float total = count * CardWidth + (count - 1) * CardGap;
        for (int i = 0; i < _cards.Count; i++)
        {
            var c = _cards[i];
            if (i >= count) { c.Root.Visible = false; continue; }
            ShrineBoon b = _current[i];
            c.Stripe.Color = b.Color;
            c.Rarity.Text = Catalog.RarityName(b.Rarity);
            c.Rarity.AddThemeColorOverride("font_color", b.Color);
            c.Title.Text = b.Title;
            c.Effect.Text = b.Effect;
            c.Effect.AddThemeColorOverride("font_color", b.Color.Lerp(Ink, 0.4f));
            c.Root.OffsetLeft = -total * 0.5f + i * (CardWidth + CardGap);
            c.Root.OffsetRight = c.Root.OffsetLeft + CardWidth;
            c.Root.Visible = true;
            c.Button.TooltipText = $"{i + 1}. {b.Title} {b.Effect}";
        }

        if (!IsOpen) _mouseWasCaptured = Input.MouseMode == Input.MouseModeEnum.Captured;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _shownAtMsec = Time.GetTicksMsec();
        _autoPickLeft = _autoPick;
        IsOpen = true;
        Visible = true;
        GetTree().Paused = true;
        _cards[0].Button.GrabFocus();
    }

    private void Close()
    {
        IsOpen = false;
        Visible = false;
        _current = new List<ShrineBoon>();
        _callback = null;
        foreach (var c in _cards) c.Root.Visible = false;
        if (_mouseWasCaptured) Actors.PlayerCamera.TryCaptureMouse();
        _mouseWasCaptured = false;
        // Leave the tree paused if the level-up panel is up underneath.
        if (IsInsideTree() && LevelUp is not { IsOpen: true }) GetTree().Paused = false;
    }

    private void Pick(int index)
    {
        if (!IsOpen || index < 0 || index >= _current.Count) return;
        ShrineBoon chosen = _current[index];
        Action<ShrineBoon>? cb = _callback;
        Close();
        cb?.Invoke(chosen);
        TryPresent();
    }

    private void OnRunEnded(RunEnded e)
    {
        _queue.Clear();
        if (IsOpen) Close();
    }

    public override void _Process(double delta)
    {
        if (!IsOpen)
        {
            if (_queue.Count > 0) TryPresent();
            return;
        }

        // Another panel closing underneath (level-up, pause menu) unpauses the
        // tree; this choice is still owed, so hold the pause.
        if (!GetTree().Paused) GetTree().Paused = true;

        if (_autoPick > 0f)
        {
            _autoPickLeft -= (float)delta;
            if (_autoPickLeft <= 0f) Pick(Main.Instance?.Run.Rng.RangeInt(0, _current.Count) ?? 0);
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (!IsOpen) return;
        if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (!Armed)
        {
            if (key.Keycode != Key.Escape) GetViewport().SetInputAsHandled();
            return;
        }
        int index = key.Keycode switch
        {
            Key.Key1 or Key.Kp1 => 0,
            Key.Key2 or Key.Kp2 => 1,
            Key.Key3 or Key.Kp3 => 2,
            _ => -1,
        };
        if (index < 0 || index >= _current.Count) return;
        GetViewport().SetInputAsHandled();
        Pick(index);
    }

    // ------------------------------------------------------------ plumbing

    private static Label MakeLabel(string text, int size, Color color,
                                   HorizontalAlignment align = HorizontalAlignment.Center)
    {
        var label = new Label { Text = text, HorizontalAlignment = align, MouseFilter = MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeColorOverride("font_outline_color", Outline);
        label.AddThemeConstantOverride("outline_size", 5);
        return label;
    }

    private static void Band(Control control, float width, float top, float height)
    {
        control.AnchorLeft = 0.5f; control.AnchorRight = 0.5f;
        control.AnchorTop = 0.5f; control.AnchorBottom = 0.5f;
        control.OffsetLeft = -width * 0.5f; control.OffsetRight = width * 0.5f;
        control.OffsetTop = top; control.OffsetBottom = top + height;
    }

    private static void Inset(Control control, float left, float top, float width, float height)
    {
        control.AnchorLeft = 0f; control.AnchorRight = 0f;
        control.AnchorTop = 0f; control.AnchorBottom = 0f;
        control.OffsetLeft = left; control.OffsetRight = left + width;
        control.OffsetTop = top; control.OffsetBottom = top + height;
    }
}
