using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Wavebreak.Data;

/// <summary>
/// Map crowds (Data/Stages.Rosters.cs). The director picks from a map's list by
/// the clock, and any map can be played at any minute, so each list is walked
/// through the first hour of a run: ids valid, never an empty pool, enough
/// variety, something near the threat the director wants, and an elite for
/// every elite wave. Then each pair of maps must be told apart by its crowd.
/// </summary>
public static partial class ContentAudit
{
    private const int RosterMinutes = 60;

    private static void AuditRosters()
    {
        var crowds = new Dictionary<string, HashSet<string>>();
        foreach (StageDef map in Stages.All)
        {
            string[] ids = map.EnemyIds;
            Check(ids.Length > 0, $"roster {map.Id}: no list, so it plays the whole catalogue");
            List<EnemyDef> defs = ids.Select(Catalog.EnemyById).OfType<EnemyDef>().ToList();
            Check(defs.Count == ids.Length && ids.Distinct().Count() == ids.Length, $"roster {map.Id}: unknown or repeated id");
            foreach (EnemyDef def in defs)
                Check(!def.IsBoss && def.DirectorSpawns, $"roster {map.Id}: {def.Id} is not a wave enemy");
            crowds[map.Id] = new HashSet<string>(ids);

            int leanest = int.MaxValue, lowestPeak = int.MaxValue;
            for (int minute = 0; minute <= RosterMinutes; minute++)
            {
                float seconds = minute * 60f;
                List<EnemyDef> open = defs.Where(d => d.UnlockAtSeconds <= seconds).ToList();
                List<EnemyDef> crowd = open.Where(d => !d.IsElite).ToList();
                // Two kinds at the bell, and the crowd must have widened by the
                // time the first boss is near.
                int want = minute < 2 ? 2 : minute < 4 ? 4 : 8;
                Check(crowd.Count >= want, $"roster {map.Id}: {crowd.Count} enemies at minute {minute}, want {want}");
                // PickAffordable centres on 1 + 0.9 x minutes and picks by the
                // log of the ratio; past the dearest enemy it simply takes the
                // top of the list, so that top has to keep climbing for a while.
                float target = 1f + minute * 0.9f;
                int peak = crowd.Count == 0 ? 0 : crowd.Max(d => d.Cost);
                Check(peak >= Math.Min(target, 9f) * 0.75f, $"roster {map.Id}: dearest enemy costs {peak} at minute {minute}, target {target:0.0}");
                if (minute >= 2) Check(open.Any(d => d.IsElite), $"roster {map.Id}: no elite at minute {minute}");
                leanest = Math.Min(leanest, crowd.Count);
                if (minute >= 10) lowestPeak = Math.Min(lowestPeak, peak);
            }
            int elites = defs.Count(d => d.IsElite);
            Line($"roster {map.Id,-12} {ids.Length} enemies, {elites} elites, leanest minute {leanest}, late top cost {lowestPeak}: " +
                 string.Join(" ", ids.Except(Stages.RosterCore)));
        }

        // Share of two crowds that is the same enemies (core left out).
        string[] maps = crowds.Keys.ToArray();
        float worst = 0f;
        string worstPair = "";
        for (int a = 0; a < maps.Length; a++)
            for (int b = a + 1; b < maps.Length; b++)
            {
                var x = crowds[maps[a]].Except(Stages.RosterCore).ToHashSet();
                var y = crowds[maps[b]].Except(Stages.RosterCore).ToHashSet();
                float shared = x.Intersect(y).Count() / (float)Math.Max(1, x.Union(y).Count());
                Check(shared < 0.6f, $"rosters {maps[a]} and {maps[b]} share {shared:P0}");
                if (shared > worst) { worst = shared; worstPair = $"{maps[a]}/{maps[b]}"; }
            }
        Line($"rosters: most alike {worstPair} at {worst:P0}");
    }
}
