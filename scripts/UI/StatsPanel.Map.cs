using Godot;
using Wavebreak.Game;
using Wavebreak.Meta;

namespace Wavebreak.UI;

/// <summary>
/// The big map in the middle of the Tab sheet: a framed box with the map's
/// name and how much of it the team has explored over a square
/// <see cref="BigMap"/>, sized to whatever the stats and the build box leave
/// (about 470 px at 1280x720, up to 780 px at 1920x1080).
/// </summary>
public sealed partial class StatsPanel
{
    /// <summary>From this sheet width up the stats get two columns.</summary>
    private const float WideSheet = 1600f;
    private const float MapMax = 780f;
    private const float BuildBoxWidth = 400f;   // 372 + its margins

    private BigMap _bigMap = null!;
    private Label _mapTitle = null!;

    private Control MapBox()
    {
        var box = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.10f, 0.12f, 0.18f, 0.96f),
            BorderColor = new Color(0.30f, 0.55f, 0.70f, 0.5f),
            BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8, CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
            ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 8, ContentMarginBottom = 10,
        });
        var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", 6);
        box.AddChild(column);
        _mapTitle = Text("", 15, Heading, HorizontalAlignment.Center);
        _mapTitle.AutoTranslateMode = AutoTranslateModeEnum.Disabled;
        column.AddChild(_mapTitle);
        _bigMap = new BigMap { CustomMinimumSize = new Vector2(420f, 420f), SizeFlagsVertical = SizeFlags.ExpandFill };
        column.AddChild(_bigMap);
        return box;
    }

    private void RefreshMapTitle()
    {
        if (Main.Instance?.Stages is not { } stages) return;
        if (stages.InRestRoom) { _mapTitle.Text = Loc.T("MAP"); return; }
        int pct = FogOfWar.Instance is { Active: true } fog ? Mathf.RoundToInt(fog.Explored * 100f) : 100;
        _mapTitle.Text = Loc.F("{0}  ·  {1} explored", Loc.T(stages.Stage.DisplayName), $"{pct}%");
    }

    /// <summary>Square map as big as the sheet allows: the height left under
    /// the title (and the pause buttons), and the width left beside the stats
    /// and the build box.</summary>
    private void FitMap(float room, float statsWidth)
    {
        if (!IsInstanceValid(_bigMap)) return;
        float width = GetViewportRect().Size.X - 48f - 40f - 16f - statsWidth - BuildBoxWidth - 24f;
        float side = Mathf.Clamp(Mathf.Min(room - 30f, width), 240f, MapMax);
        _bigMap.CustomMinimumSize = new Vector2(side, side);
    }
}
