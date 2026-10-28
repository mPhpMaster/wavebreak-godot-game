using System.Collections.Generic;

namespace Wavebreak.Game;

public static class ScoreboardOrdering
{
    public static float Value(ScoreLine line, ScoreMetric metric) => metric switch
    {
        ScoreMetric.Dps => line.Dps, ScoreMetric.Kills => line.Kills,
        ScoreMetric.Deaths => line.Deaths, ScoreMetric.Healing => line.HealingRestored,
        _ => line.DamageDealt,
    };
    public static void Fill(IReadOnlyList<ScoreLine> source, List<ScoreLine> destination, ScoreMetric metric)
    {
        destination.Clear(); for (int i = 0; i < source.Count; i++) destination.Add(source[i]);
        destination.Sort((a, b) =>
        {
            int order = Value(b, metric).CompareTo(Value(a, metric));
            return order != 0 ? order : a.Slot.CompareTo(b.Slot);
        });
    }
    public static bool Leads(ScoreLine line, ScoreMetric metric, IReadOnlyList<ScoreLine> all)
    {
        float value = Value(line, metric); if (value <= 0) return false;
        for (int i = 0; i < all.Count; i++) if (Value(all[i], metric) > value) return false;
        return true;
    }
}
