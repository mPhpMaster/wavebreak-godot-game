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
        Label title = MakeLabel("SPECIAL EXPEDITIONS", 27, Gold);
        Place(title, 60f, 183f, 900f, 40f); page.AddChild(title);
        _expeditionHero = MakeLabel("", 18, Ink);
        Place(_expeditionHero, 115f, 226f, 650f, 56f); page.AddChild(_expeditionHero);
        Button previous = _heroPrevious = MenuButton("‹", () => CycleExpeditionHero(-1));
        previous.Name = "ExpeditionHeroPrevious";
        Place(previous, 60f, 230f, 45f, 36f); page.AddChild(previous);
        Button next = _heroNext = MenuButton("›", () => CycleExpeditionHero(1));
        next.Name = "ExpeditionHeroNext";
        Place(next, 735f, 230f, 45f, 36f); page.AddChild(next);
        _expeditionDifficulty = new DifficultyPicker { Compact = true };
        Place(_expeditionDifficulty, 795f, 226f, 220f, 55f); page.AddChild(_expeditionDifficulty);
        BuildExpeditionCard(page, false, 60f);
        BuildExpeditionCard(page, true, 550f);
        Button classic = MenuButton("CLASSIC PLAY & CHARACTER DETAILS", () =>
        {
            RestoreExpeditionClassic();
            ShowPage("play");
        });
        Place(classic, 60f, 655f, 600f, 44f); page.AddChild(classic);
        Button back = MenuButton("BACK", () => ShowPage("main"));
        Place(back, 795f, 655f, 220f, 44f); page.AddChild(back);
        return page;
    }

    private void BuildExpeditionCard(Control page, bool city, float x)
    {
        var panel = Panel(x, 296f, 465f, 342f);
        page.AddChild(panel);
        var rows = new VBoxContainer(); rows.AddThemeConstantOverride("separation", 5); panel.AddChild(rows);
        rows.AddChild(new ExpeditionPreview { City = city, CustomMinimumSize = new Vector2(410f, 42f) });
        string route = city ? "skyline-run" : "reef-dive";
        Button select = MenuButton(city ? "Bellspire Ward" : "Lumenreef Basin", () => SelectExpedition(route));
        select.CustomMinimumSize = new Vector2(0f, 34f); select.AddThemeFontSizeOverride("font_size", 23);
        _mapSelectors.Add((route, select)); rows.AddChild(select);
        Label description = MakeLabel(city
            ? "Swing between tall rooftops. Defeat colossal stone wardens by striking their glowing cores. Recommended: Vesper."
            : "Explore deep reefs, currents and a hidden cave. Swim freely in three dimensions. Recommended: Nori.", 16, Ink);
        description.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        description.CustomMinimumSize = new Vector2(0f, 66f); rows.AddChild(description);
        Label controls = MakeLabel(city
            ? Loc.F("{0} / {1}: independent hooks  |  {2}: attached boost",
                Keybinds.Describe(Keybinds.Get("city_hook_left", 0)), Keybinds.Describe(Keybinds.Get("city_hook_right", 0)), Keybinds.Describe(Keybinds.Get("jump", 0)))
            : Loc.F("Hold {0}: rise  |  Hold {1}: dive  |  Look + forward: swim",
                Keybinds.Describe(Keybinds.Get("jump", 0)), Keybinds.Describe(Keybinds.Get("slide", 0))), 14, Accent);
        controls.AutowrapMode = TextServer.AutowrapMode.WordSmart; rows.AddChild(controls);
        Button start = MenuButton(city ? "START CITY EXPEDITION" : "START REEF EXPEDITION", () => StartExpedition(route));
        _expeditionStarts[route] = start;
        rows.AddChild(start);
        if (!city) _expeditionFocus = start;
        rows.AddChild(MenuButton("SELECT FOR CO-OP", () =>
        {
            SelectExpedition(route, false);
            Profile.Save();
            ShowPage("coop");
        }));
    }

    internal void OpenExpeditions(bool focus = true)
    {
        _expeditionFlow ??= new ExpeditionFlow();
        CharacterDef hero = Characters.ById(Profile.SelectedCharacter) ?? Characters.Default;
        bool selected = _expeditionFlow.RouteId is not null;
        _heroPrevious.Disabled = _heroNext.Disabled = !selected;
        _expeditionHero.Text = selected ? Loc.F("Character: {0}  ·  {1}", Loc.T(hero.DisplayName), Characters.Describe(hero))
            : Loc.T("Select a map to preview its hero; you can choose another afterward");
        _expeditionHero.TooltipText = Loc.T(hero.PassiveText);
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
        StartRun(hero);
    }
}
