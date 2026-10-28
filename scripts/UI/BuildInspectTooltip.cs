using Godot;
using Wavebreak.Meta;

namespace Wavebreak.UI;

public sealed partial class BuildInspectTooltip : PanelContainer
{
    public string Content { get; }
    public BuildInspectTooltip(string content, Vector2 viewport)
    {
        Content = content;
        Name = "BuildInspectTooltip";
        ProcessMode = ProcessModeEnum.Always;
        MouseFilter = MouseFilterEnum.Ignore;
        LayoutDirection = Loc.IsRtl ? LayoutDirectionEnum.Rtl : LayoutDirectionEnum.Ltr;
        var style = new StyleBoxFlat { BgColor = new Color(.045f, .06f, .10f, .99f),
            BorderColor = new Color(.45f, .86f, .92f), ContentMarginLeft = 14, ContentMarginRight = 14,
            ContentMarginTop = 12, ContentMarginBottom = 12 };
        style.SetBorderWidthAll(1); style.SetCornerRadiusAll(8);
        AddThemeStyleboxOverride("panel", style);
        float width = Mathf.Clamp(viewport.X - 60, 180, 380);
        string[] parts = content.Split('\n', 2);
        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 8); AddChild(box);
        var title = Label(parts[0], 18, new Color(.65f, .98f, 1), width);
        box.AddChild(title);
        if (parts.Length < 2) return;
        var body = Label(parts[1], 15, new Color(.94f, .96f, 1), width);
        float height = body.GetThemeFont("font").GetMultilineStringSize(parts[1], HorizontalAlignment.Left, width, 15).Y + 12;
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto, MouseFilter = MouseFilterEnum.Pass,
            CustomMinimumSize = new Vector2(width, Mathf.Clamp(height, 35, Mathf.Max(80, Mathf.Min(400, viewport.Y - 140)))) };
        box.AddChild(scroll); scroll.AddChild(body);
    }

    private static Label Label(string text, int size, Color color, float width)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(width, 0), MouseFilter = MouseFilterEnum.Ignore,
            AutoTranslateMode = AutoTranslateModeEnum.Disabled, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }
}
