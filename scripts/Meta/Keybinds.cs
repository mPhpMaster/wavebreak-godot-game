using System.Collections.Generic;
using Godot;

namespace Wavebreak.Meta;

/// <summary>
/// Player key rebinding, persisted in <c>user://keybinds.cfg</c>.
///
/// Each action has one PRIMARY binding the player can change (the first key or
/// mouse button in its project.godot entry); any further bindings — the arrow
/// keys beside WASD, C beside Ctrl — are kept as they are. That keeps the screen
/// a single column of "action: key" rows people can scan, while the alternates
/// still work for anyone used to them.
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

    private static readonly Dictionary<string, InputEvent?> Defaults = new();
    private static bool _loaded;

    /// <summary>Capture the shipped defaults, then apply the saved overrides.
    /// Idempotent: the title screen and a run may both call it.</summary>
    public static void Load()
    {
        if (_loaded) return;
        _loaded = true;

        foreach (var (action, _) in Actions)
            if (InputMap.HasAction(action)) Defaults[action] = Primary(action)?.Duplicate() as InputEvent;

        // Automated runs (tests, captures) must behave the same on every machine.
        if (Launch.Automated) return;

        var cfg = new ConfigFile();
        if (cfg.Load(FilePath) != Error.Ok) return;
        foreach (var (action, _) in Actions)
        {
            if (!InputMap.HasAction(action) || !cfg.HasSectionKey("keys", action)) continue;
            if (Decode(cfg.GetValue("keys", action).AsString()) is { } ev) SetPrimary(action, ev);
        }
    }

    public static void Save()
    {
        // A test pressing keys must never rewrite the player's real bindings.
        if (Launch.Automated) return;
        var cfg = new ConfigFile();
        foreach (var (action, _) in Actions)
            if (InputMap.HasAction(action) && Primary(action) is { } ev)
                cfg.SetValue("keys", action, Encode(ev));
        cfg.Save(FilePath);
    }

    /// <summary>The binding the player edits: first key or mouse button.</summary>
    public static InputEvent? Primary(string action)
    {
        foreach (InputEvent ev in InputMap.ActionGetEvents(action))
            if (ev is InputEventKey or InputEventMouseButton) return ev;
        return null;
    }

    /// <summary>Bind <paramref name="ev"/> as the primary for <paramref name="action"/>.
    /// Returns the action that previously had this input, which now has the old
    /// primary of <paramref name="action"/> (a swap), or null.</summary>
    public static string? Rebind(string action, InputEvent ev)
    {
        InputEvent? old = Primary(action);
        string? swapped = null;
        foreach (var (other, _) in Actions)
        {
            if (other == action || !InputMap.HasAction(other)) continue;
            if (Primary(other) is { } theirs && Same(theirs, ev))
            {
                if (old is not null) SetPrimary(other, old);
                swapped = other;
                break;
            }
        }
        SetPrimary(action, ev);
        Save();
        return swapped;
    }

    public static void ResetAll()
    {
        foreach (var (action, ev) in Defaults)
            if (ev is not null) SetPrimary(action, (InputEvent)ev.Duplicate());
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

    /// <summary>All bindings for an action, primary first, for the hint text.</summary>
    public static string DescribeAll(string action)
    {
        var parts = new List<string>();
        foreach (InputEvent ev in InputMap.ActionGetEvents(action))
            if (ev is InputEventKey or InputEventMouseButton) parts.Add(Describe(ev));
        return string.Join(" / ", parts);
    }

    // --------------------------------------------------------------- helpers

    private static void SetPrimary(string action, InputEvent ev)
    {
        var events = new List<InputEvent>(InputMap.ActionGetEvents(action));
        int index = events.FindIndex(e => e is InputEventKey or InputEventMouseButton);
        // Drop any alternate that is now the same input, or it would show twice.
        events.RemoveAll(e => e != (index >= 0 ? events[index] : null) && Same(e, ev));
        index = events.FindIndex(e => e is InputEventKey or InputEventMouseButton);
        if (index >= 0) events[index] = ev; else events.Insert(0, ev);

        InputMap.ActionEraseEvents(action);
        foreach (InputEvent e in events) InputMap.ActionAddEvent(action, e);
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
