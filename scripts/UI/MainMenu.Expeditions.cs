using Godot;
using Wavebreak.Meta;

namespace Wavebreak.UI;

public sealed partial class MainMenu
{
    private ExpeditionFlow? _expeditionFlow;
    private Button _heroPrevious = null!, _heroNext = null!;
    private readonly System.Collections.Generic.List<(string Route, Button Button)> _mapSelectors = new();
    private readonly System.Collections.Generic.Dictionary<string, Button> _expeditionStarts = new();
    private Label _expeditionHero = null!;
    private DifficultyPicker _expeditionDifficulty = null!;
    private Button _expeditionFocus = null!;

    private Control WrapMainExpeditions(Control main)
    {
        var wrapper = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        wrapper.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        wrapper.AddChild(main);
        var invite = Panel(470f, 235f, 520f, 185f);
        wrapper.AddChild(invite);
        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 12);
        invite.AddChild(rows);
        rows.AddChild(MakeLabel("TWO SPECIAL EXPEDITIONS", 23, Gold));
        Label description = MakeLabel("Dive into Lumenreef or grapple above Bellspire. Two new heroes are already unlocked.", 18, Ink);
        description.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        rows.AddChild(description);
        rows.AddChild(MenuButton("SPECIAL MAPS: REEF & CITY", () => ShowPage("expeditions")));
        return wrapper;
    }

    private Control BuildExpeditionPage()
    {
        var page = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        page.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var backdrop = new ColorRect { Color = new Color(.025f, .045f, .075f, .96f), MouseFilter = Control.MouseFilterEnum.Ignore };
        Place(backdrop, 36f, 176f, 1020f, 548f); page.AddChild(backdrop);
        Label title = MakeLabel("SPECIAL EXPEDITIONS", 28, Ink);
        Place(title, 60f, 192f, 650f, 40f); page.AddChild(title);
        _expeditionHero = MakeLabel("Choose a destination, then choose your character.", 16, InkDim);
        Place(_expeditionHero, 60f, 238f, 650f, 30f); page.AddChild(_expeditionHero);
        _heroPrevious = MenuButton("‹", () => CycleExpeditionHero(-1));
        _heroPrevious.Name = "ExpeditionHeroPrevious";
        _heroNext = MenuButton("›", () => CycleExpeditionHero(1));
        _heroNext.Name = "ExpeditionHeroNext";
        _heroPrevious.Visible = _heroNext.Visible = false;
        page.AddChild(_heroPrevious); page.AddChild(_heroNext);
        _expeditionDifficulty = new DifficultyPicker { Compact = true };
        Place(_expeditionDifficulty, 795f, 202f, 220f, 50f); page.AddChild(_expeditionDifficulty);
        BuildExpeditionCard(page, false, 60f);
        BuildExpeditionCard(page, true, 550f);
        Button classic = MenuButton("CLASSIC PLAY & CHARACTER DETAILS", () =>
        {
            RestoreExpeditionClassic();
            Profile.SetMeta("route", 0);
            ShowPage("play");
        });
        Place(classic, 60f, 655f, 600f, 44f); page.AddChild(classic);
        Button back = MenuButton("BACK", () => ShowPage("main"));
        Place(back, 795f, 655f, 220f, 44f); page.AddChild(back);
        return page;
    }

    private void BuildExpeditionCard(Control page, bool city, float x)
    {
        var panel = Panel(x, 282f, 465f, 348f);
        var surface = new StyleBoxFlat { BgColor = new Color(.065f, .095f, .14f), BorderColor = new Color(.16f, .24f, .31f), ContentMarginLeft = 18, ContentMarginRight = 18, ContentMarginTop = 16, ContentMarginBottom = 16 };
        surface.SetCornerRadiusAll(12); surface.SetBorderWidthAll(1);
        panel.AddThemeStyleboxOverride("panel", surface);
        page.AddChild(panel);
        var rows = new VBoxContainer(); rows.AddThemeConstantOverride("separation", 10); panel.AddChild(rows);
        rows.AddChild(new ExpeditionPreview { City = city, CustomMinimumSize = new Vector2(410f, 110f), ClipContents = true, MouseFilter = Control.MouseFilterEnum.Ignore });
        string route = city ? "skyline-run" : "reef-dive";
        Button select = MenuButton(city ? "Bellspire Ward" : "Lumenreef Basin", () => SelectExpedition(route));
        select.CustomMinimumSize = new Vector2(0f, 34f); select.AddThemeFontSizeOverride("font_size", 24);
        select.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        _mapSelectors.Add((route, select)); rows.AddChild(select);
        Label description = MakeLabel(city ? "Rooftop grappling · Colossal wardens" : "Free swimming · Hidden caves", 16, InkDim);
        description.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        description.CustomMinimumSize = new Vector2(0f, 44f); rows.AddChild(description);
        var actions = new HBoxContainer(); actions.AddThemeConstantOverride("separation", 10); rows.AddChild(actions);
        Button start = MenuButton(city ? "START CITY EXPEDITION" : "START REEF EXPEDITION", () => StartExpedition(route));
        start.AddThemeFontSizeOverride("font_size", 16);
        start.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        start.Alignment = HorizontalAlignment.Center;
        Color accent = city ? new Color(.95f, .70f, .36f) : new Color(.36f, .85f, .80f);
        start.AddThemeColorOverride("font_color", new Color(.025f, .06f, .09f));
        start.AddThemeColorOverride("font_focus_color", new Color(.025f, .06f, .09f));
        start.AddThemeStyleboxOverride("normal", ButtonBox(accent, Colors.Transparent));
        _expeditionStarts[route] = start; actions.AddChild(start);
        if (!city) _expeditionFocus = start;
        Button coop = MenuButton("SELECT FOR CO-OP", () =>
        {
            SelectExpedition(route, false);
            Profile.Save();
            ShowPage("coop");
        });
        coop.AddThemeFontSizeOverride("font_size", 14);
        coop.Alignment = HorizontalAlignment.Center;
        actions.AddChild(coop);
    }

    internal void OpenExpeditions(bool focus = true)
    {
        _expeditionFlow ??= new ExpeditionFlow();
        CharacterDef hero = Characters.ById(Profile.SelectedCharacter) ?? Characters.Default;
        bool selected = _expeditionFlow.RouteId is not null;
        _heroPrevious.Disabled = _heroNext.Disabled = !selected;
        _expeditionHero.Text = Loc.T("Choose a destination, then choose your character.");
        foreach (var entry in _mapSelectors)
            entry.Button.AddThemeColorOverride("font_color", entry.Route == _expeditionFlow.RouteId ? Gold : Ink);
        _expeditionDifficulty.Refresh();
        TintHero(hero);
        if (focus)
        {
            Button target = _expeditionFlow.RouteId is { } route && _expeditionStarts.TryGetValue(route, out Button? selectedStart)
                ? selectedStart : _expeditionFocus;
            target.GrabFocus();
        }
    }

    private void SelectExpedition(string route, bool explicitSelection = true)
    {
        _expeditionFlow ??= new ExpeditionFlow();
        _expeditionFlow.Select(route, explicitSelection);
        OpenExpeditions();
    }

    private void RestoreExpeditionClassic()
    {
        _expeditionFlow?.RestoreClassic();
        Profile.Save();
        TintHero(Characters.ById(Profile.SelectedCharacter) ?? Characters.Default);
    }

    private void CycleExpeditionHero(int step)
    {
        CharacterDef hero = ExpeditionSelection.CycleHero(step);
        _shownCharacter = hero;
        _skinIndex = 0;
        OpenExpeditions(false);
    }

    internal void StartExpedition(string route)
    {
        SelectExpedition(route, false);
        CharacterDef hero = Characters.ById(Profile.SelectedCharacter) ?? Characters.Default;
        // Let StartRun preserve this hero's owned skin and hat selections.
        _shownCharacter = null;
        ShowPage("play:" + hero.Id);
    }
}
