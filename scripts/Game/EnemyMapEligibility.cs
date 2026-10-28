using Wavebreak.Data;
using Wavebreak.Visuals;

namespace Wavebreak.Game;

internal static class EnemyMapEligibility
{
    internal static bool Allows(EnemyDef definition, StageDef stage)
        => CityGiantModels.IsGiant(definition) == (stage.Layout == "city")
            && (!definition.UnderwaterOnly || stage.Underwater);
}
