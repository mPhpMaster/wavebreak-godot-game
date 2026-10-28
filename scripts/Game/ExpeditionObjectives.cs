using Wavebreak.Meta;

namespace Wavebreak.Game;

public static class ExpeditionObjectives
{
    public static string Describe(StageDirector stages)
    {
        if (stages.InRestRoom) return "";
        if (stages.State == StageDirector.PhaseState.PortalOpen) return Loc.T("Objective: enter the portal together.");
        if (stages.State == StageDirector.PhaseState.Boss) return Loc.T("Objective: defeat the phase boss.");
        if (Arena.IsCity) return Loc.T("Objective: strike glowing cores and defeat the warden.");
        if (Arena.Current.Underwater)
        {
            int discovered = ReefCaveDiscovery.Instance?.Mask ?? 0;
            int count = (discovered & 1) + ((discovered >> 1) & 1);
            int claimed = ReefHarvest.Instance?.SpentIds.Length ?? 0;
            return Loc.F("Explore: cave entrances {0}/2 · resources gathered {1}", count, claimed);
        }
        return Loc.T("Objective: survive the wave, defeat the boss, reach the portal.");
    }
}
