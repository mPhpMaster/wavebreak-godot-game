using Godot;
using Wavebreak.Meta;

namespace Wavebreak.UI;

/// <summary>
/// The CREDITS page: who built the game, who shaped the idea, and the free
/// assets it stands on (assets/CREDITS.md has the full licence list). Opened
/// from the small CREDITS button at the bottom right of the title screen.
/// </summary>
public sealed partial class MainMenu
{
    private static readonly (string Heading, string[] Names)[] CreditSections =
    {
        ("BUILT BY", new[] { "sensei (hlaCk)", "Claude AI" }),
        ("IDEA", new[] { "sensei (hlaCk)", "Megabonk", "Ahmed Hus (Random)", "adamix57/ttv", "IcyG" }),
        ("FREE ASSETS", new[] { "Kenney (CC0)", "Quaternius (CC0)", "Noto Sans Arabic UI (OFL)", "Godot Engine (MIT)" }),
    };

    private Control BuildCreditsPage()
    {
        var page = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        page.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        PanelContainer panel = Panel(60f, 196f, 520f, 446f);
        page.AddChild(panel);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        panel.AddChild(scroll);
        var box = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(box);

        box.AddChild(MakeLabel("CREDITS", 30, Gold));
        foreach ((string heading, string[] names) in CreditSections)
        {
            box.AddChild(SectionLabel(heading));
            foreach (string name in names)
            {
                Label l = MakeLabel(name, 22, Ink);
                l.AutoTranslateMode = AutoTranslateModeEnum.Disabled;   // names stay as written
                box.AddChild(l);
            }
        }
        box.AddChild(new Control { CustomMinimumSize = new Vector2(0f, 10f) });
        box.AddChild(MakeLabel("Thank you for playing.", 17, InkDim));

        Button back = MenuButton("BACK", () => ShowPage("main"));
        Place(back, 60f, 654f, 240f, 44f);
        page.AddChild(back);
        return page;
    }

    /// <summary>Small CREDITS button, bottom right above the version.</summary>
    private void AddCreditsButton()
    {
        var b = new Button { Text = Loc.T("CREDITS"), Flat = true, FocusMode = Control.FocusModeEnum.None };
        b.AddThemeFontSizeOverride("font_size", 16);
        b.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 0.75f));
        b.AddThemeColorOverride("font_hover_color", Gold);
        b.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomRight);
        b.OffsetLeft = -160f; b.OffsetTop = -62f; b.OffsetRight = -12f; b.OffsetBottom = -32f;
        b.Alignment = HorizontalAlignment.Right;
        b.Pressed += () => ShowPage("credits");
        _root.AddChild(b);
        _creditsButton = b;
    }

    private Button? _creditsButton;
}
