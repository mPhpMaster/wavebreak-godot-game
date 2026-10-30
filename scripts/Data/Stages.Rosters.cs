using System;
using System.Collections.Generic;
using System.Linq;

namespace Wavebreak.Data;

/// <summary>
/// Map crowds from <see cref="EnemyDef.Themes"/> tags.
///
/// A list per map would have to be re-read every time an enemy is added; tags
/// mean a new enemy with a "storm" tag turns up over Rimeglass on its own. The
/// director's clock still decides when: <see cref="EnemyDef.UnlockAtSeconds"/>
/// and the threat curve work on whatever list a map has, so a roster only says
/// who may come here, never how soon. Any map can be played at any clock minute
/// (first on a drawn route, last on a long one), so every roster has to work at
/// all of them; <c>--validate-content</c> walks each map through the first hour.
/// </summary>
public static partial class Stages
{
    /// <summary>In every crowd: no themed enemy unlocks before 125 s, so these
    /// are what a map opens with whichever one it is, and the warden is the
    /// only elite the first elite wave (70 s) can field.</summary>
    public static readonly string[] RosterCore = { "mote", "husk", "skitterling", "gravel_brute", "ash_warden" };

    private static readonly Dictionary<string, string[]> Rosters = new();

    /// <summary>The ids a map's <see cref="StageDef.RosterThemes"/> select, or
    /// nothing for a map without any. Built once per map id.</summary>
    public static string[] RosterFromThemes(StageDef map)
    {
        if (map.RosterThemes.Length == 0) return Array.Empty<string>();
        if (Rosters.TryGetValue(map.Id, out string[]? known)) return known;

        var ids = new List<string>(RosterCore);
        foreach (EnemyDef e in Catalog.Enemies)
        {
            if (e.IsBoss || !e.DirectorSpawns || e.UnderwaterOnly || e.HasTheme("city") || ids.Contains(e.Id)) continue;
            if (map.RosterThemes.Any(e.HasTheme) && !map.RosterAvoid.Any(e.HasTheme)) ids.Add(e.Id);
        }
        foreach (string id in map.RosterExtra)
            if (!ids.Contains(id)) ids.Add(id);
        string[] roster = ids.ToArray();
        Rosters[map.Id] = roster;
        return roster;
    }
}
