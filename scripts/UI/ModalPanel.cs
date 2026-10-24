using System;
using Godot;
using Wavebreak.Core;

namespace Wavebreak.UI;

/// <summary>
/// Shared shell for the station panels (merchant, microwave): pauses the tree,
/// frees the mouse and hands it back, ignores keys for a moment after opening,
/// and closes on Escape or the interact key.
///
/// The arm delay is the same guard <see cref="LevelUpPanel"/> and
/// <see cref="ChestPrompt"/> rely on: the panel opens from a key press in the
/// middle of play, and the next key the player meant for the game (Space is
/// jump, and ui_accept on a focused button) must not buy something.
/// </summary>
public partial class ModalPanel : Control
{
    public const ulong ArmDelayMsec = 350;

    protected static readonly Color Ink = new(0.94f, 0.96f, 0.99f);
    protected static readonly Color InkDim = new(0.66f, 0.71f, 0.80f);
    protected static readonly Color Gold = new(1f, 0.80f, 0.30f);
    protected static readonly Color Bad = new(1f, 0.38f, 0.34f);
    private static readonly Color Outline = new(0.02f, 0.03f, 0.05f, 0.9f);

    public bool IsOpen { get; private set; }

    private ulong _shownAtMsec;
    private bool _subscribed;

    protected bool Armed => Time.GetTicksMsec() - _shownAtMsec >= ArmDelayMsec;

    /// <summary>The root everything visible hangs from; subclasses fill it.</summary>
    protected VBoxContainer Body { get; private set; } = null!;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        ProcessMode = ProcessModeEnum.Always;
        Visible = false;

        var dim = new ColorRect { Color = new Color(0.02f, 0.03f, 0.05f, 0.7f), MouseFilter = MouseFilterEnum.Stop };
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(dim);

        var centre = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        centre.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(centre);

        var frame = new PanelContainer();
        frame.AddThemeStyleboxOverride("panel", Box(new Color(0.07f, 0.08f, 0.11f, 0.97f), new Color(1f, 1f, 1f, 0.12f), 18));
        centre.AddChild(frame);

        Body = new VBoxContainer();
        Body.AddThemeConstantOverride("separation", 12);
        frame.AddChild(Body);

        EventBus.Subscribe<RunEnded>(OnRunEnded);
        _subscribed = true;
    }

    public override void _ExitTree()
    {
        if (_subscribed) EventBus.Unsubscribe<RunEnded>(OnRunEnded);
        _subscribed = false;
        if (IsOpen) Close();
    }

    private void OnRunEnded(RunEnded e) => Close();

    protected void OpenPanel()
    {
        Actors.PlayerCamera.HoldCursor(this);
        _shownAtMsec = Time.GetTicksMsec();
        IsOpen = true;
        Visible = true;
        if (IsInsideTree()) Net.Coop.SetPaused(GetTree(), true);
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        Visible = false;
        Actors.PlayerCamera.ReleaseCursor(this);
        if (IsInsideTree()) Net.Coop.SetPaused(GetTree(), false);
        OnClosed();
    }

    protected virtual void OnClosed() { }

    /// <summary>A number key, 0-based, after the arm delay. Return true if used.</summary>
    protected virtual bool OnNumber(int index) => false;

    public override void _Input(InputEvent @event)
    {
        if (!IsOpen || DevPanel.IsOpen) return;
        if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;

        bool escape = key.Keycode == Key.Escape;
        if (!Armed)
        {
            // Swallowed, so it cannot reach a button as an activation either.
            GetViewport().SetInputAsHandled();
            return;
        }

        if (escape || (InputMap.HasAction("interact") && key.IsActionPressed("interact")))
        {
            GetViewport().SetInputAsHandled();
            Close();
            return;
        }

        int index = key.Keycode switch
        {
            >= Key.Key1 and <= Key.Key9 => (int)(key.Keycode - Key.Key1),
            >= Key.Kp1 and <= Key.Kp9 => (int)(key.Keycode - Key.Kp1),
            _ => -1,
        };
        if (index >= 0 && OnNumber(index)) GetViewport().SetInputAsHandled();
    }

    // ------------------------------------------------------------- helpers

    protected static StyleBoxFlat Box(Color fill, Color border, int pad = 12)
    {
        var sb = new StyleBoxFlat { BgColor = fill, BorderColor = border };
        sb.SetBorderWidthAll(2);
        sb.SetCornerRadiusAll(6);
        sb.SetContentMarginAll(pad);
        return sb;
    }

    protected static Label MakeLabel(string text, int size, Color colour,
                                     HorizontalAlignment align = HorizontalAlignment.Center)
    {
        var label = new Label { Text = text, HorizontalAlignment = align, MouseFilter = MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", colour);
        label.AddThemeColorOverride("font_outline_color", Outline);
        label.AddThemeConstantOverride("outline_size", 4);
        return label;
    }

    protected Button MakeButton(string text, Action onPress)
    {
        var b = new Button { Text = text, FocusMode = FocusModeEnum.None };
        b.AddThemeFontSizeOverride("font_size", 16);
        b.Pressed += () => { if (Armed) onPress(); };
        return b;
    }
}
