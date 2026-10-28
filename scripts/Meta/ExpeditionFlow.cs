using System;

namespace Wavebreak.Meta;

/// <summary>Defaults belong to an explicit map choice; refreshes never choose a hero.</summary>
public sealed class ExpeditionFlow
{
    private const string HeroKey = "expedition_classic_hero";
    private const string RouteKey = "expedition_classic_route";
    public string? RouteId { get; private set; }
    private readonly string _classicHero;
    private readonly string _classicRoute;

    public ExpeditionFlow()
    {
        bool side = Routes.Selected.Id is "reef-dive" or "skyline-run";
        int hero = (int)Profile.GetMeta(HeroKey) - 1;
        int route = (int)Profile.GetMeta(RouteKey) - 1;
        _classicHero = side && hero >= 0 && hero < Characters.All.Count ? Characters.All[hero].Id : Profile.SelectedCharacter;
        _classicRoute = side ? route >= 0 && route < Routes.All.Count ? Routes.All[route].Id : "classic" : Routes.Selected.Id;
        if (_classicRoute is "reef-dive" or "skyline-run") _classicRoute = "classic";
    }

    public CharacterDef Select(string route, bool explicitSelection = false)
    {
        if (route is not ("reef-dive" or "skyline-run")) throw new ArgumentException("Not an expedition", nameof(route));
        if (RouteId != route || explicitSelection)
        {
            for (int i = 0; i < Characters.All.Count; i++)
                if (Characters.All[i].Id == _classicHero) Profile.SetMeta(HeroKey, i + 1);
            Profile.SetMeta(RouteKey, Routes.IndexOf(Routes.ById(_classicRoute)!) + 1);
            Profile.SelectedCharacter = route == "reef-dive" ? "nori" : "vesper";
            RouteId = route;
        }
        ExpeditionSelection.SelectRoute(route);
        return Characters.ById(Profile.SelectedCharacter) ?? Characters.Default;
    }

    public void RestoreClassic()
    {
        Profile.SelectedCharacter = _classicHero;
        ExpeditionSelection.SelectRoute(_classicRoute);
        RouteId = null;
    }
}
