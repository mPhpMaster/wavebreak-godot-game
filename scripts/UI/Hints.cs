using System;
using System.Collections.Generic;
using Godot;

namespace Wavebreak.UI;

/// <summary>
/// Text that names a control, kept true. A hint built once says whatever was
/// bound when it was built; these follow a rebind, and swap between key and pad
/// names when the player picks up the other device.
///
/// <see cref="Stamp"/> is for text redrawn on a timer (the dash meter): compare
/// it and redraw on a change. <see cref="Follow"/> is for text set once, on a
/// control that lives as long as its panel.
/// </summary>
public static class Hints
{
    private static readonly List<(Control Owner, Action Update)> Followers = new();

    /// <summary>Changes whenever any hint may read differently.</summary>
    public static int Stamp { get; private set; }

    /// <summary>Set <paramref name="label"/> from <paramref name="text"/> now and
    /// again on every change.</summary>
    public static void Follow(Label label, Func<string> text)
    {
        label.Text = text();
        Watch(label, () => label.Text = text());
    }

    /// <summary>The same for a button's caption.</summary>
    public static void Follow(Button button, Func<string> text)
    {
        button.Text = text();
        Watch(button, () => button.Text = text());
    }

    /// <summary>Run <paramref name="update"/> on every change, for as long as
    /// <paramref name="owner"/> exists.</summary>
    public static void Watch(Control owner, Action update)
    {
        Followers.RemoveAll(f => !GodotObject.IsInstanceValid(f.Owner));
        Followers.Add((owner, update));
    }

    /// <summary>The device or a binding changed.</summary>
    internal static void Refresh()
    {
        Stamp++;
        Followers.RemoveAll(f => !GodotObject.IsInstanceValid(f.Owner));
        foreach (var (_, update) in Followers.ToArray()) update();
    }
}
