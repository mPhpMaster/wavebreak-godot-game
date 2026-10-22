using System.Collections.Generic;
using Godot;

namespace Wavebreak.Meta;

/// <summary>
/// Player key rebinding, persisted in <c>user://keybinds.cfg</c>.
///
/// Each action has two editable bindings, a primary and a secondary (WASD and
/// the arrow keys, Ctrl and C, Shift and right mouse), as in the shipped map.
/// Either can be rebound or cleared; gamepad events on the action are kept.
///
/// Rebinding to a key another action already uses SWAPS the two, rather than
/// leaving two actions on one key (where one silently stops working) or refusing
/// (which forces a three-step dance through a spare key).
///
/// Defaults are captured from the project's InputMap before any override is
/// applied, so "reset" always means what ships, even after the file has changed.
/// </summary>
public static class Keybinds
{
    private const string FilePath = "user://keybinds.cfg";

    /// <summary>The rebindable actions, in display order.</summary>
    public static readonly IReadOnlyList<(string Action, string Label)> Actions = new[]
    {
        ("move_forward", "Move forward"),
        ("move_back", "Move back"),
        ("move_left", "Move left"),
        ("move_right", "Move right"),
        ("jump", "Jump"),
        ("dash", "Dash"),
        ("slide", "Slide / slam"),
        ("interact", "Interact (chests, shrines)"),
        ("stats", "Character sheet (hold)"),
        ("pause", "Pause"),
        ("volume_down", "Volume down"),
        ("volume_up", "Volume up"),
        ("mute", "Mute"),
        ("sfx_volume_down", "Effects down"),
        ("sfx_volume_up", "Effects up"),
        ("music_volume_down", "Music down"),
        ("music_volume_up", "Music up"),
    };

    /// <summary>Bindings per action the player can edit: a primary and a secondary.</summary>
    public const int Slots = 2;

    private static readonly Dictionary<string, InputEvent?[]> Defaults = new();
    private static bool _loaded;

    /// <summary>Capture the shipped defaults, then apply the saved overrides.
    /// Idempotent: the title screen and a run may both call it.</summary>
    public static void Load()
    {
        if (_loaded) return;
        _loaded = true;

        foreach (var (action, _) in Actions)
        {
            if (!InputMap.HasAction(action)) continue;
            var slots = new InputEvent?[Slots];
            for (int i = 0; i < Slots; i++) slots[i] = Get(action, i)?.Duplicate() as InputEvent;
            Defaults[action] = slots;
        }

        // Automated runs (tests, captures) must behave the same on every machine.
        if (Launch.Automated) return;

        var cfg = new ConfigFile();
        if (cfg.Load(FilePath) != Error.Ok) return;
        foreach (var (action, _) in Actions)
        {
            if (!InputMap.HasAction(action) || !cfg.HasSectionKey("keys", action)) continue;
            // "key:87,key:4194320" -- one entry per slot, empty for an unbound slot.
            string[] parts = cfg.GetValue("keys", action).AsString().Split(',');
            var slots = new InputEvent?[Slots];
            for (int i = 0; i < Slots && i < parts.Length; i++) slots[i] = Decode(parts[i]);
            SetAll(action, slots);
        }
    }

    public static void Save()
    {
        // A test pressing keys must never rewrite the player's real bindings.
        if (Launch.Automated) return;
        var cfg = new ConfigFile();
        foreach (var (action, _) in Actions)
        {
            if (!InputMap.HasAction(action)) continue;
            var parts = new string[Slots];
            for (int i = 0; i < Slots; i++) parts[i] = Get(action, i) is { } ev ? Encode(ev) : "";
            cfg.SetValue("keys", action, string.Join(",", parts));
        }
        cfg.Save(FilePath);
    }

    /// <summary>The key or mouse button in <paramref name="slot"/> (0 = primary,
    /// 1 = secondary), or null when that slot is unbound.</summary>
    public static InputEvent? Get(string action, int slot)
    {
        int i = 0;
        foreach (InputEvent ev in InputMap.ActionGetEvents(action))
        {
            if (ev is not (InputEventKey or InputEventMouseButton)) continue;
            if (i++ == slot) return ev;
        }
        return null;
    }

    /// <summary>Kept for callers that only care about the first binding.</summary>
    public static InputEvent? Primary(string action) => Get(action, 0);

    /// <summary>
    /// Bind <paramref name="ev"/> into a slot. If another action (or this
    /// action's other slot) already uses that input, it receives this slot's old
    /// binding instead -- a swap -- so no key ever drives two actions and nothing
    /// is silently lost. Returns the action that gave the input up, or null.
    /// Passing null clears the slot.
    /// </summary>
    public static string? Rebind(string action, int slot, InputEvent? ev)
    {
        InputEvent?[] mine = Read(action);
        InputEvent? old = mine[slot];
        string? swapped = null;

        if (ev is not null)
        {
            foreach (var (other, _) in Actions)
            {
                if (!InputMap.HasAction(other)) continue;
                InputEvent?[] theirs = other == action ? mine : Read(other);
                for (int i = 0; i < Slots; i++)
                {
                    if (other == action && i == slot) continue;
                    if (theirs[i] is { } e && Same(e, ev))
                    {
                        theirs[i] = old;
                        if (other != action) SetAll(other, theirs);
                        swapped = other;
                        goto found;
                    }
                }
            }
        }
        found:
        mine[slot] = ev;
        SetAll(action, mine);
        Save();
        return swapped;
    }

    public static void ResetAll()
    {
        foreach (var (action, slots) in Defaults)
        {
            var copy = new InputEvent?[Slots];
            for (int i = 0; i < Slots; i++) copy[i] = slots[i]?.Duplicate() as InputEvent;
            SetAll(action, copy);
        }
        Save();
    }

    /// <summary>"Space", "Shift", "Mouse Right" — what a row shows.</summary>
    public static string Describe(InputEvent? ev) => ev switch
    {
        InputEventKey k => OS.GetKeycodeString(k.PhysicalKeycode != Key.None
                               ? DisplayServer.KeyboardGetKeycodeFromPhysical(k.PhysicalKeycode)
                               : k.Keycode) is { Length: > 0 } s ? s : "?",
        InputEventMouseButton m => m.ButtonIndex switch
        {
            MouseButton.Left => "Mouse Left",
            MouseButton.Right => "Mouse Right",
            MouseButton.Middle => "Mouse Middle",
            MouseButton.Xbutton1 => "Mouse 4",
            MouseButton.Xbutton2 => "Mouse 5",
            _ => $"Mouse {(int)m.ButtonIndex}",
        },
        _ => "—",
    };

    // --------------------------------------------------------------- helpers

    private static InputEvent?[] Read(string action)
    {
        var slots = new InputEvent?[Slots];
        for (int i = 0; i < Slots; i++) slots[i] = Get(action, i);
        return slots;
    }

    /// <summary>Rewrite an action's key/mouse bindings to exactly these slots,
    /// keeping any other event kinds (gamepad) it has.</summary>
    private static void SetAll(string action, InputEvent?[] slots)
    {
        var keep = new List<InputEvent>();
        foreach (InputEvent e in InputMap.ActionGetEvents(action))
            if (e is not (InputEventKey or InputEventMouseButton)) keep.Add(e);

        InputMap.ActionEraseEvents(action);
        foreach (InputEvent? e in slots) if (e is not null) InputMap.ActionAddEvent(action, e);
        foreach (InputEvent e in keep) InputMap.ActionAddEvent(action, e);
    }

    private static bool Same(InputEvent a, InputEvent b) => (a, b) switch
    {
        (InputEventKey x, InputEventKey y) => KeyOf(x) == KeyOf(y),
        (InputEventMouseButton x, InputEventMouseButton y) => x.ButtonIndex == y.ButtonIndex,
        _ => false,
    };

    private static Key KeyOf(InputEventKey k) => k.PhysicalKeycode != Key.None ? k.PhysicalKeycode : k.Keycode;

    // Stored as "key:<physical keycode>" or "mouse:<button index>": readable in
    // the file, and immune to keyboard-layout changes for keys.
    private static string Encode(InputEvent ev) => ev switch
    {
        InputEventKey k => $"key:{(long)KeyOf(k)}",
        InputEventMouseButton m => $"mouse:{(long)m.ButtonIndex}",
        _ => "",
    };

    private static InputEvent? Decode(string s)
    {
        string[] p = s.Split(':');
        if (p.Length != 2 || !long.TryParse(p[1], out long v)) return null;
        return p[0] switch
        {
            "key" => new InputEventKey { PhysicalKeycode = (Key)v },
            "mouse" => new InputEventMouseButton { ButtonIndex = (MouseButton)v },
            _ => null,
        };
    }
}
