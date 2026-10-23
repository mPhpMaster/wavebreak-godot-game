using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Meta;

namespace Wavebreak.UI;

/// <summary>
/// The "route & challenges" page, opened from the play page: one button that
/// cycles the route (Meta/Routes), and a grid of challenge toggles
/// (Meta/Challenges) with the combined Pearls multiplier. Every change is
/// saved to the profile at once; the run reads it when it starts.
///
/// All controls are ordinary focusable buttons, so keyboard and gamepad
/// navigation comes from the built-in ui_* actions like the rest of the menu.
/// </summary>
public sealed partial class ChallengePicker : Control
{
    private static readonly Color Ink = new(0.93f, 0.95f, 0.99f);
    private static readonly Color InkDim = new(0.62f, 0.68f, 0.78f);
    private static readonly Color On = new(0.45f, 0.92f, 0.58f);
    private static readonly Color Gold = new(1.00f, 0.84f, 0.38f);

    private readonly Action _back;
    private Button _route = null!;
    private Label _routeNote = null!;
    private Label _total = null!;
    private Label _detail = null!;
    private readonly List<(ChallengeDef def, Button button)> _toggles = new();

    public ChallengePicker(Action back)
    {
        _back = back;
        Name = "ChallengePicker";
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Build();
    }

    /// <summary>One line for the play page's button.</summary>
    public static string Summary()
    {
        IReadOnlyList<ChallengeDef> set = Challenges.Selected;
        string chal = set.Count == 0 ? "no challenges" : $"{set.Count} challenge{(set.Count == 1 ? "" : "s")} x{Challenges.RewardFor(set):0.##}";
        return $"ROUTE: {Routes.Selected.Name.ToUpperInvariant()}  ·  {chal}";
    }

    private void Build()
    {
        Label heading = MainMenu.MakeLabel("ROUTE & CHALLENGES", 20, InkDim);
        MainMenu.Place(heading, 60f, 196f, 600f, 28f);
        AddChild(heading);

        var col = new VBoxContainer();
        MainMenu.Place(col, 60f, 232f, 1140f, 470f);
        col.AddThemeConstantOverride("separation", 8);
        AddChild(col);

        _route = MainMenu.MenuButton("", () => { Routes.Cycle(+1); Profile.Save(); Refresh(); });
        _route.MouseEntered += () => _route.GrabFocus();
        _route.CustomMinimumSize = new Vector2(520f, 48f);
        _route.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        col.AddChild(_route);
        _routeNote = MainMenu.MakeLabel("", 14, InkDim);
        _routeNote.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _routeNote.CustomMinimumSize = new Vector2(0f, 40f);
        col.AddChild(_routeNote);

        _total = MainMenu.MakeLabel("", 17, Gold);
        col.AddChild(_total);

        var grid = new GridContainer { Columns = 3 };
        grid.AddThemeConstantOverride("h_separation", 8);
        grid.AddThemeConstantOverride("v_separation", 6);
        col.AddChild(grid);
        foreach (ChallengeDef def in Challenges.All)
        {
            ChallengeDef c = def;
            Button b = MainMenu.MenuButton("", () => { Challenges.Toggle(c); Profile.Save(); Refresh(); });
            b.CustomMinimumSize = new Vector2(370f, 42f);
            b.AddThemeFontSizeOverride("font_size", 18);
            b.FocusEntered += () => ShowDetail(c);
            b.MouseEntered += () => b.GrabFocus();
            grid.AddChild(b);
            _toggles.Add((c, b));
        }

        _detail = MainMenu.MakeLabel("", 15, Ink);
        _detail.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _detail.CustomMinimumSize = new Vector2(0f, 44f);
        col.AddChild(_detail);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        Button clear = MainMenu.MenuButton("CLEAR ALL", () => { Challenges.ClearSelection(); Profile.Save(); Refresh(); });
        clear.CustomMinimumSize = new Vector2(200f, 44f);
        Button back = MainMenu.MenuButton("BACK", _back);
        back.CustomMinimumSize = new Vector2(200f, 44f);
        row.AddChild(clear);
        row.AddChild(back);
        col.AddChild(row);
    }

    public void FocusFirst() => _route.GrabFocus();

    public void Refresh()
    {
        RouteDef r = Routes.Selected;
        _route.Text = $"ROUTE  ·  {r.Name.ToUpperInvariant()}   ({Routes.IndexOf(r) + 1}/{Routes.All.Count})";
        _routeNote.Text = $"{Routes.DescribeMaps(r)}\n{r.Blurb}  Press to change.";

        foreach ((ChallengeDef c, Button b) in _toggles)
        {
            bool on = Challenges.IsSelected(c);
            b.Text = $"{(on ? "[x]" : "[  ]")} {c.Name}   +{c.Reward * 100f:0}%";
            b.AddThemeColorOverride("font_color", on ? On : Ink);
        }
        IReadOnlyList<ChallengeDef> set = Challenges.Selected;
        _total.Text = set.Count == 0
            ? $"No challenges  ·  {Pearls.Name} x1"
            : $"{Challenges.Describe(set)}  ·  {Pearls.Name} x{Challenges.RewardFor(set):0.##}";
    }

    private void ShowDetail(ChallengeDef c)
    {
        List<string> maps = Challenges.ClearedMaps(c);
        long wins = Challenges.Wins(c);
        string record = maps.Count == 0 ? "not cleared yet" : $"cleared on {string.Join(", ", maps)}";
        if (wins > 0) record += $"; won {wins}x";
        _detail.Text = $"{c.Name}: {c.Blurb}  ({c.RewardText}; {record})";
    }
}
