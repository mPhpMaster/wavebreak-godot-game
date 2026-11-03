using System;
using Godot;
using Wavebreak.Meta;

namespace Wavebreak.UI;

/// <summary>
/// The PUZZLES ONLY toggle (the Puzzle Gauntlet, Meta/Routes "puzzles"): on
/// the PLAY page under the puzzle route toggle, and in the CO-OP lobby beside
/// it. On selects the gauntlet and remembers the route it replaced; off goes
/// back to it (Routes.SetGauntlet). Endless and a map challenge block it, as
/// they block the puzzle route.
/// </summary>
public sealed partial class MainMenu
{
    private Button _gauntletToggle = null!;

    private void ToggleGauntlet()
    {
        if (Routes.PuzzleBlockedReason() is not null) return;
        Routes.SetGauntlet(!Routes.GauntletSelected);
        Profile.Save();
        RefreshRouteBar();
    }

    internal static Button GauntletToggle(Action pressed)
    {
        Button b = MenuButton("", pressed);
        b.Name = "GauntletToggle";
        b.Icon = Icons.Texture("puzzle");
        b.ExpandIcon = true;
        b.AddThemeConstantOverride("icon_max_width", 26);
        b.AddThemeConstantOverride("h_separation", 10);
        b.AddThemeFontSizeOverride("font_size", 18);
        b.ClipText = true;
        b.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        return b;
    }

    /// <summary>On or off, or greyed out with the reason it cannot be used.</summary>
    internal static void StyleGauntletToggle(Button b, bool on, string? blocked)
    {
        b.Text = Loc.T(on ? "PUZZLES ONLY: ON" : "PUZZLES ONLY: OFF");
        b.Disabled = blocked is not null;
        b.TooltipText = Loc.T(blocked ?? (on
            ? "Six puzzle maps back to back, no fights. Press to go back to the route you had."
            : "Press for the Puzzle Gauntlet: six puzzle maps back to back, no fights, a medal for each."));
        Color tint = blocked is not null ? Locked : on ? Gold : Ink;
        foreach (string slot in new[] { "font_color", "font_disabled_color" }) b.AddThemeColorOverride(slot, tint);
        foreach (string slot in new[] { "icon_normal_color", "icon_disabled_color", "icon_hover_color", "icon_pressed_color" })
            b.AddThemeColorOverride(slot, tint);
        b.AddThemeColorOverride("icon_focus_color", blocked is not null ? Locked : Gold);
    }

    /// <summary>The gauntlet's line on the route bar: its length and its best.</summary>
    private static string GauntletBarText()
    {
        string line = Loc.F("{0} puzzles, no fights", Routes.GauntletPuzzles);
        if (GauntletRecord.Runs > 0) line += "  ·  " + Loc.F("best {0}/{1}", GauntletRecord.Best, GauntletRecord.MaxScore);
        return line;
    }
}
