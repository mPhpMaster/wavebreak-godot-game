using Godot;
using Wavebreak.Game;
using Wavebreak.Meta;

namespace Wavebreak.UI;

/// <summary>
/// Installs the team-awareness HUD for a run (Main._Ready): minimap, name
/// tags and edge arrows, the boss toast and down alert, and the scoreboard.
///
/// Two layers. The overlay sits on the same layer as the main HUD but just
/// before it in the tree, so the HUD, the level-up panel, the pause menu and
/// the results screen all draw over it (CanvasLayers on one layer stack by
/// their index). The scoreboard has its own high layer, so holding Y works
/// over the pause menu and the results screen.
/// </summary>
public static class AwarenessLayer
{
    public static void Install(Main main)
    {
        RunScoreboard.Begin();
        HudSettings.Load();
        Minimap.DownMarkersDrawn = 0;
        AwarenessAlerts.DownAlertsShown = 0;

        var overlay = new CanvasLayer { Name = "AwarenessUI", Layer = 1 };
        main.AddChild(overlay);
        if (main.GetNodeOrNull<CanvasLayer>("UI") is { } ui) main.MoveChild(overlay, ui.GetIndex());
        overlay.AddChild(new TeamTags());
        overlay.AddChild(new PingMarkers());
        overlay.AddChild(new Minimap());
        overlay.AddChild(new AwarenessAlerts());

        var top = new CanvasLayer { Name = "ScoreboardUI", Layer = 30 };
        main.AddChild(top);
        top.AddChild(new ScoreboardPanel());
    }
}
