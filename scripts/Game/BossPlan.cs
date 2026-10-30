using System.Collections.Generic;
using Wavebreak.Core;
using Wavebreak.Data;

namespace Wavebreak.Game;

/// <summary>
/// Which boss guards each map of a run, decided up front from the run's seed.
///
/// <see cref="EnemyDef.BossTier"/> keeps the ramp: the first map gets a tier 1
/// boss, the last a tier 3, the maps between tier 2, and when a long run has
/// more middle maps than tier 2 bosses the spare tier 3 goes on the last of
/// them. Within a tier the seed draws, so runs differ, and a boss is used once
/// per run while any of its tier is left.
///
/// A map's <see cref="StageDef.BossId"/> means one of two things. A boss that
/// can only stand on that map (the Reef Leviathan, the city's Archon) is an
/// authored encounter and always comes. Any other is the map's signature: three
/// times as likely as the rest of its tier when the plan reaches that map.
///
/// A pure function of the seed, so the plan needs no state: only the host ever
/// asks (<see cref="StageDirector"/> sends clients the boss it chose), and a
/// client that becomes the host gets the same answer the old host would have.
/// </summary>
public static class BossPlan
{
    private const float SignatureWeight = 3f;

    /// <summary>The boss for each entry of <paramref name="stages"/>; null where
    /// no boss in the roster can stand on that map.</summary>
    public static EnemyDef?[] For(IReadOnlyList<StageDef> stages, IReadOnlyList<EnemyDef> roster, ulong seed)
    {
        var rng = new Rng(seed ^ 0xB055_D12AUL);
        var plan = new EnemyDef?[stages.Count];
        var used = new HashSet<EnemyDef>();

        // Authored encounters are spoken for before any draw.
        for (int i = 0; i < stages.Count; i++)
        {
            if (Find(roster, stages[i].BossId) is { } own && Authored(own) && EnemyMapEligibility.Allows(own, stages[i]))
            {
                plan[i] = own;
                used.Add(own);
            }
        }

        // The finale first: it must not find its tier spent on a middle map.
        int last = stages.Count - 1;
        if (last >= 0 && plan[last] is null) plan[last] = Draw(stages[last], roster, used, 3, rng);
        for (int i = 0; i < last; i++)
            plan[i] ??= Draw(stages[i], roster, used, i == 0 ? 1 : 2, rng);
        return plan;
    }

    /// <summary>The ramp's tier and upward, then downward, then anyone already
    /// met: repeats only once every eligible boss has been used.</summary>
    private static EnemyDef? Draw(StageDef stage, IReadOnlyList<EnemyDef> roster, HashSet<EnemyDef> used, int tier, Rng rng)
    {
        var options = new List<EnemyDef>();
        var weights = new List<float>();
        for (int t = tier; t <= 3 && options.Count == 0; t++) Collect(stage, roster, used, t, options, weights);
        for (int t = tier - 1; t >= 1 && options.Count == 0; t--) Collect(stage, roster, used, t, options, weights);
        if (options.Count == 0) Collect(stage, roster, null, tier, options, weights);
        if (options.Count == 0) return null;
        EnemyDef pick = options[rng.PickWeighted(weights)];
        used.Add(pick);
        return pick;
    }

    private static void Collect(StageDef stage, IReadOnlyList<EnemyDef> roster, HashSet<EnemyDef>? used, int tier,
                                List<EnemyDef> options, List<float> weights)
    {
        foreach (EnemyDef def in roster)
        {
            if (def is null || !def.IsBoss || def.BossTier != tier || Authored(def)) continue;
            if (used?.Contains(def) == true || !EnemyMapEligibility.Allows(def, stage)) continue;
            options.Add(def);
            weights.Add(def.Id == stage.BossId ? SignatureWeight : 1f);
        }
    }

    /// <summary>Tied to one map, so never part of a draw.</summary>
    private static bool Authored(EnemyDef def)
        => def.BossTier == 0 || def.UnderwaterOnly || Visuals.CityGiantModels.IsGiant(def);

    private static EnemyDef? Find(IReadOnlyList<EnemyDef> roster, string id)
    {
        if (id.Length == 0) return null;
        foreach (EnemyDef def in roster) if (def is not null && def.IsBoss && def.Id == id) return def;
        return null;
    }
}
