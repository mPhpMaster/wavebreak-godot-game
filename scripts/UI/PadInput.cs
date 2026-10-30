using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Meta;

namespace Wavebreak.UI;

/// <summary>
/// What a gamepad needs that the InputMap does not give for free. One node under
/// the root, alive across scenes.
///
/// Which device was used last, read from the root window's input signal: that
/// fires before any panel can swallow the event.
///
/// Menu navigation. The engine moves focus on every motion event past the dead
/// zone, so a stick flies down a menu, and a held D-pad moves one row. While a
/// menu has focus both are taken here and turned into ui_* actions at a steady
/// repeat, so sliders and option lists keep working unchanged. In play a stick
/// is left alone.
///
/// Focus is never lost. A page that rebuilds its buttons, or a button that
/// disappears, leaves nothing focused and a pad with nothing to press; while a
/// menu owns the game and the pad is in use, focus goes back to the last control
/// or to the first one in the top panel.
///
/// Press edges. A stick or trigger bound to an action sends a stream of motion
/// events, and each reads as IsActionPressed; <see cref="Pressed"/> reports only
/// the first.
/// </summary>
public sealed partial class PadInput : Node
{
    private const double RepeatDelay = 0.34;
    private const double RepeatEvery = 0.11;
    private const float StickOn = 0.55f;
    private const float StickOff = 0.35f;
    private const float AxisPressed = 0.5f;
    private const float AxisReleased = 0.3f;
    private const float ScrollSpeed = 620f;

    public static PadInput? Instance { get; private set; }

    /// <summary>True when the last real input came from a pad.</summary>
    public static bool UsingPad { get; private set; }

    /// <summary>Raised when the last-used device changes between pad and
    /// keyboard or mouse.</summary>
    public static event Action? DeviceChanged;

    /// <summary>--check-pad: count injected events the way a real pad counts.</summary>
    internal static bool ForceFocusForTest { get; set; }

    private static readonly Dictionary<(int Device, JoyAxis Axis), int> AxisHeldSign = new();
    private static ulong _edgeEvent;
    private static bool _edge;

    private Vector2 _stick;
    private float _scroll;
    private Vector2I _dpad;
    private Vector2I _navDir;
    private double _navWait;
    private Control? _lastFocus;
    private double _rescueIn;

    /// <summary>Panels whose first control takes focus when focus has to be
    /// rescued, newest last. See <see cref="AddScope"/>.</summary>
    private static readonly List<Control> Scopes = new();

    public static void Install()
    {
        if (Instance is not null && IsInstanceValid(Instance)) return;
        if (Engine.GetMainLoop() is not SceneTree tree) return;
        Instance = new PadInput();
        tree.Root.CallDeferred(Node.MethodName.AddChild, Instance);
        PadSettings.Load();
    }

    public override void _Ready()
    {
        Name = "PadInput";
        ProcessMode = ProcessModeEnum.Always;
        EnsureUiActions();
        Window root = GetTree().Root;
        root.WindowInput += Observe;
        root.GuiFocusChanged += OnFocusChanged;
    }

    public override void _ExitTree()
    {
        Window root = GetTree().Root;
        root.WindowInput -= Observe;
        root.GuiFocusChanged -= OnFocusChanged;
        if (ReferenceEquals(Instance, this)) Instance = null;
    }

    /// <summary>A and B on the engine's ui_accept and ui_cancel, whatever the
    /// engine version ships there.</summary>
    private static void EnsureUiActions()
    {
        void Ensure(string action, JoyButton button)
        {
            foreach (InputEvent ev in InputMap.ActionGetEvents(action))
                if (ev is InputEventJoypadButton b && b.ButtonIndex == button) return;
            InputMap.ActionAddEvent(action, new InputEventJoypadButton { ButtonIndex = button });
        }
        Ensure("ui_accept", JoyButton.A);
        Ensure("ui_cancel", JoyButton.B);
    }

    // --------------------------------------------------------------- device

    private void Observe(InputEvent e)
    {
        TrackNavigation(e);
        bool pad = e is InputEventJoypadButton { Pressed: true }
            || e is InputEventJoypadMotion motion && Mathf.Abs(motion.AxisValue) >= StickOff;
        bool other = e is InputEventKey { Pressed: true } or InputEventMouseButton { Pressed: true }
            || e is InputEventMouseMotion m && m.Relative.LengthSquared() > 4f;
        if (pad == other || pad == UsingPad) return;
        UsingPad = pad;
        DeviceChanged?.Invoke();
    }

    // ------------------------------------------------------------ press edges

    /// <summary>A key or pad button going down, not an echo: what a panel treats
    /// as "a press".</summary>
    public static bool IsPress(InputEvent e)
        => e is InputEventKey { Pressed: true, Echo: false } or InputEventJoypadButton { Pressed: true };

    /// <summary>Esc, or the pad's B: backing out of whatever is open.</summary>
    public static bool IsBack(InputEvent e)
        => e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }
            || e is InputEventJoypadButton { Pressed: true } && e.IsActionPressed("ui_cancel");

    /// <summary><see cref="InputEvent.IsActionPressed"/>, once per press even
    /// when the action sits on a stick or a trigger.</summary>
    public static bool Pressed(InputEvent e, string action)
    {
        if (!InputMap.HasAction(action) || !e.IsActionPressed(action)) return false;
        return e is not InputEventJoypadMotion motion || NewAxisPress(motion);
    }

    /// <summary>This motion is a stick or trigger crossing into a new press.</summary>
    public static bool NewAxisPress(InputEventJoypadMotion motion)
    {
        // Several nodes ask about one event; it is one edge however many do.
        ulong id = motion.GetInstanceId();
        if (id == _edgeEvent) return _edge;
        _edgeEvent = id;
        var key = (motion.Device, motion.Axis);
        AxisHeldSign.TryGetValue(key, out int held);
        float value = motion.AxisValue;
        int sign = value >= AxisPressed ? 1 : value <= -AxisPressed ? -1 : 0;
        _edge = sign != 0 && sign != held;
        if (sign != 0) AxisHeldSign[key] = sign;
        else if (Mathf.Abs(value) <= AxisReleased) AxisHeldSign[key] = 0;
        return _edge;
    }

    /// <summary>In play the D-pad and sticks are the game's. Not while a menu
    /// has focus, and not while the compact hand takes the D-pad for its cards.</summary>
    public static bool PlayHasPad => !MenuActive && !LevelUpPanel.AnyCompactOpen;

    /// <summary>The binding is down on any connected pad.</summary>
    public static bool ButtonHeld(InputEventJoypadButton binding)
    {
        foreach (int device in Input.GetConnectedJoypads())
            if ((binding.Device < 0 || binding.Device == device) && Input.IsJoyButtonPressed(device, binding.ButtonIndex)) return true;
        return false;
    }

    /// <summary>The binding's stick direction is pushed on any connected pad.</summary>
    public static bool AxisHeld(InputEventJoypadMotion binding)
    {
        foreach (int device in Input.GetConnectedJoypads())
            if ((binding.Device < 0 || binding.Device == device)
                && Input.GetJoyAxis(device, binding.Axis) * Mathf.Sign(binding.AxisValue) > AxisPressed) return true;
        return false;
    }

    // -------------------------------------------------------------- menu state

    /// <summary>A control in a menu has focus, so the D-pad and the left stick
    /// navigate rather than doing what they do in play.</summary>
    public static bool MenuActive => Instance?.FocusedControl() is not null;

    private Control? FocusedControl()
    {
        Control? owner = GetViewport()?.GuiGetFocusOwner();
        return owner is not null && owner.IsVisibleInTree() ? owner : null;
    }

    /// <summary>A menu owns the game, so something should have focus: the title
    /// screen, a pause, a level-up, the results screen.</summary>
    private bool MenuOwnsGame()
    {
        if (DevPanel.IsOpen || KeybindPanel.AnyListening) return false;
        if (Game.Main.Instance is not { } main || !IsInstanceValid(main)) return true;
        if (main.Results is { IsShown: true }) return true;
        return Net.Coop.IsPaused(GetTree());
    }

    // -------------------------------------------------------------- navigation

    /// <summary>Held state, from the window signal: a popup list swallows the
    /// release otherwise, and the old direction would repeat forever.</summary>
    private void TrackNavigation(InputEvent e)
    {
        switch (e)
        {
            case InputEventJoypadMotion { Axis: JoyAxis.LeftX or JoyAxis.LeftY } motion:
                if (motion.Axis == JoyAxis.LeftX) _stick.X = motion.AxisValue; else _stick.Y = motion.AxisValue;
                break;
            case InputEventJoypadMotion { Axis: JoyAxis.RightY } scroll:
                _scroll = scroll.AxisValue;
                break;
            case InputEventJoypadButton { ButtonIndex: JoyButton.DpadLeft or JoyButton.DpadRight or JoyButton.DpadUp or JoyButton.DpadDown } button:
                Vector2I step = button.ButtonIndex switch
                {
                    JoyButton.DpadLeft => Vector2I.Left,
                    JoyButton.DpadRight => Vector2I.Right,
                    JoyButton.DpadUp => Vector2I.Up,
                    _ => Vector2I.Down,
                };
                if (button.Pressed) _dpad = step;
                else if (_dpad == step) _dpad = Vector2I.Zero;
                break;
        }
    }

    public override void _Input(InputEvent e)
    {
        bool nav = e is InputEventJoypadMotion { Axis: JoyAxis.LeftX or JoyAxis.LeftY }
            or InputEventJoypadButton { ButtonIndex: JoyButton.DpadLeft or JoyButton.DpadRight or JoyButton.DpadUp or JoyButton.DpadDown };
        if (nav && NavigationWanted()) GetViewport().SetInputAsHandled();
    }

    private bool NavigationWanted()
    {
        if (KeybindPanel.AnyListening || DevPanel.IsOpen || PopupOpen()) return false;
        return FocusedControl() is not null || MenuOwnsGame();
    }

    /// <summary>An option list is open: it walks itself, repeat included.</summary>
    private bool PopupOpen()
    {
        foreach (Window w in GetViewport().GetEmbeddedSubwindows())
            if (w is PopupMenu { Visible: true }) return true;
        return false;
    }

    public override void _Process(double delta)
    {
        Vector2I dir = _dpad != Vector2I.Zero ? _dpad : StickDirection();
        if (dir != _navDir)
        {
            _navDir = dir;
            _navWait = RepeatDelay;
            if (dir != Vector2I.Zero && NavigationWanted()) Navigate(dir);
        }
        else if (dir != Vector2I.Zero)
        {
            _navWait -= delta;
            if (_navWait <= 0.0)
            {
                _navWait = RepeatEvery;
                if (NavigationWanted()) Navigate(dir);
            }
        }

        ScrollList((float)delta);

        _rescueIn -= delta;
        if (_rescueIn > 0.0) return;
        _rescueIn = 0.1;
        if ((UsingPad || ForceFocusForTest) && FocusedControl() is null && MenuOwnsGame()) Rescue();
    }

    private Vector2I StickDirection()
    {
        Vector2 v = _stick;
        float threshold = _navDir == Vector2I.Zero ? StickOn : StickOff;
        if (v.Length() < threshold) return Vector2I.Zero;
        return Mathf.Abs(v.X) >= Mathf.Abs(v.Y)
            ? new Vector2I(Math.Sign(v.X), 0)
            : new Vector2I(0, Math.Sign(v.Y));
    }

    private void Navigate(Vector2I dir)
    {
        if (FocusedControl() is null) Rescue();
        string action = dir.X < 0 ? "ui_left" : dir.X > 0 ? "ui_right" : dir.Y < 0 ? "ui_up" : "ui_down";
        Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = true });
        Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = false });
    }

    /// <summary>The right stick scrolls the list nearest the focused control:
    /// text with nothing focusable in it has no other way to move.</summary>
    private void ScrollList(float dt)
    {
        if (Mathf.Abs(_scroll) < StickOff || FocusedControl() is not { } focus) return;
        for (Node? n = focus; n is not null && n is not Window; n = n.GetParent())
            if (FindScroll(n) is { } scroll)
            {
                scroll.ScrollVertical += Mathf.RoundToInt(_scroll * ScrollSpeed * dt);
                return;
            }
    }

    private static ScrollContainer? FindScroll(Node node)
    {
        if (node is CanvasItem { Visible: false }) return null;
        if (node is ScrollContainer scroll) return scroll;
        foreach (Node child in node.GetChildren())
            if (FindScroll(child) is { } hit) return hit;
        return null;
    }

    // ------------------------------------------------------------------- focus

    private void OnFocusChanged(Control focused)
    {
        _lastFocus = focused;
        // Long lists are scrolled by hand with a mouse; a pad walks them, and
        // the row it lands on has to come into view.
        for (Node? n = focused.GetParent(); n is not null; n = n.GetParent())
            if (n is ScrollContainer scroll)
            {
                Control target = focused;
                Callable.From(() =>
                {
                    if (IsInstanceValid(scroll) && IsInstanceValid(target)) scroll.EnsureControlVisible(target);
                }).CallDeferred();
                break;
            }
    }

    /// <summary>Make <paramref name="root"/> the place focus returns to while it
    /// is on screen. For an overlay above other focusable controls.</summary>
    public static void AddScope(Control root)
    {
        Scopes.RemoveAll(c => !IsInstanceValid(c));
        if (!Scopes.Contains(root)) Scopes.Add(root);
    }

    /// <summary>Focus the first usable control under <paramref name="root"/>;
    /// a disabled one if it holds nothing else. For panels opened with a pad.</summary>
    public static bool FocusFirstIn(Control root)
    {
        BaseControl pick = default;
        Scan(root, ref pick);
        if (pick.Control is null) return false;
        pick.Control.GrabFocus();
        return true;
    }

    private struct BaseControl
    {
        public Control? Control;
        public bool Enabled;
    }

    private static void Scan(Node node, ref BaseControl pick)
    {
        if (pick.Enabled) return;
        if (node is Control c)
        {
            if (!c.Visible) return;
            if (c.FocusMode == Control.FocusModeEnum.All)
            {
                bool enabled = c is not BaseButton { Disabled: true };
                if (enabled || pick.Control is null) pick = new BaseControl { Control = c, Enabled = enabled };
                if (enabled) return;
            }
        }
        else if (node is CanvasItem { Visible: false } or CanvasLayer { Visible: false }) return;
        foreach (Node child in node.GetChildren()) Scan(child, ref pick);
    }

    private void Rescue()
    {
        if (_lastFocus is { } last && IsInstanceValid(last) && last.IsVisibleInTree()
            && last.FocusMode == Control.FocusModeEnum.All && last is not BaseButton { Disabled: true })
        {
            last.GrabFocus();
            return;
        }
        Scopes.RemoveAll(c => !IsInstanceValid(c));
        for (int i = Scopes.Count - 1; i >= 0; i--)
            if (Scopes[i].IsVisibleInTree() && FocusFirstIn(Scopes[i])) return;
        if (GetTree().CurrentScene is { } scene)
        {
            BaseControl pick = default;
            Scan(scene, ref pick);
            pick.Control?.GrabFocus();
        }
    }
}
