using System;

namespace Wavebreak.Meta;

public static class ExpeditionSelection
{
    public static void SelectRoute(string id)
    {
        RouteDef route = Routes.ById(id) ?? throw new ArgumentException("Unknown expedition route", nameof(id));
        MapChallenges.Select(null);
        EndlessMode.Selected = false;
        Profile.SetMeta("route", Routes.IndexOf(route));
    }

    public static CharacterDef CycleHero(int step)
    {
        int index = 0;
        for (int i = 0; i < Characters.All.Count; i++)
            if (Characters.All[i].Id == Profile.SelectedCharacter) index = i;
        for (int n = 0; n < Characters.All.Count; n++)
        {
            index = (index + step + Characters.All.Count) % Characters.All.Count;
            CharacterDef hero = Characters.All[index];
            if (!hero.IsAvailable) continue;
            Profile.SelectedCharacter = hero.Id;
            return hero;
        }
        return Characters.Default;
    }
}
