using System;
using Godot;
using Wavebreak.Data;
using Wavebreak.Meta;

namespace Wavebreak.UI;

/// <summary>Modal, scrollable release notes. Gameplay never runs beneath this title overlay.</summary>
public sealed partial class WhatsNewPanel : Control
{
    private readonly ReleaseNotesEntry _notes;
    private readonly Action _dismiss;
    public WhatsNewPanel(ReleaseNotesEntry notes, Action dismiss) { _notes = notes; _dismiss = dismiss; }

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        var shade = new ColorRect { Color = new Color(.015f, .025f, .045f, .88f), MouseFilter = MouseFilterEnum.Stop };
        shade.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(shade);
        var frame = new PanelContainer();
        frame.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        frame.OffsetLeft = -370; frame.OffsetRight = 370; frame.OffsetTop = -275; frame.OffsetBottom = 275;
        var surface = new StyleBoxFlat { BgColor = new Color(.055f, .08f, .12f), BorderColor = new Color(.22f, .38f, .45f), ContentMarginLeft = 30, ContentMarginRight = 30, ContentMarginTop = 25, ContentMarginBottom = 25 };
        surface.SetCornerRadiusAll(14); surface.SetBorderWidthAll(1);
        frame.AddThemeStyleboxOverride("panel", surface); AddChild(frame);
        var rows = new VBoxContainer(); rows.AddThemeConstantOverride("separation", 15); frame.AddChild(rows);
        rows.AddChild(MainMenu.MakeLabel("WHAT'S NEW", 29, new Color(.4f, .87f, .82f)));
        rows.AddChild(MainMenu.MakeLabel(Loc.F("Version {0}", _notes.Version), 17, new Color(.65f, .73f, .81f)));
        Label title = MainMenu.MakeLabel(_notes.Title, 22, Colors.White);
        title.AutowrapMode = TextServer.AutowrapMode.WordSmart; rows.AddChild(title);
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        rows.AddChild(scroll);
        var changes = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        changes.AddThemeConstantOverride("separation", 18); scroll.AddChild(changes);
        foreach (string change in _notes.Changes)
        {
            Label text = MainMenu.MakeLabel("• " + Loc.T(change), 18, new Color(.88f, .92f, .96f));
            text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            text.SizeFlagsHorizontal = SizeFlags.ExpandFill; changes.AddChild(text);
        }
        Button close = MainMenu.MenuButton("LET'S PLAY", _dismiss);
        close.Alignment = HorizontalAlignment.Center; rows.AddChild(close);
        // Keep keyboard/gamepad navigation inside the modal rather than the covered menu.
        NodePath ownPath = close.GetPath();
        close.FocusNext = ownPath; close.FocusPrevious = ownPath;
        close.FocusNeighborLeft = ownPath; close.FocusNeighborRight = ownPath;
        close.FocusNeighborTop = ownPath; close.FocusNeighborBottom = ownPath;
        close.GrabFocus();
    }

    public override void _Input(InputEvent input)
    {
        if (input.IsActionPressed("ui_cancel")) { GetViewport().SetInputAsHandled(); _dismiss(); }
    }
}
