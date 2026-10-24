using System.Collections.Generic;
using Wavebreak.Meta;

namespace Wavebreak.Audio;

/// <summary>What a track is for. Stings play once; everything else loops.</summary>
public enum TrackKind { Menu, Map, Remix, Boss, Swarm, Sting }

/// <summary>One music file in <c>assets/music/</c> and what the Boombox shows for it.</summary>
public sealed class TrackDef
{
    /// <summary>File stem: <c>assets/music/&lt;Id&gt;.ogg</c>.</summary>
    public required string Id { get; init; }
    public required string Title { get; init; }
    /// <summary>Where the game plays it, one short line.</summary>
    public required string Where { get; init; }
    public required TrackKind Kind { get; init; }

    /// <summary>Map id for <see cref="TrackKind.Map"/> and <see cref="TrackKind.Remix"/> tracks.</summary>
    public string MapId { get; init; } = "";

    /// <summary>Unlock/quest id (<see cref="Unlocks.All"/>) that also opens it. Empty: none.</summary>
    public string UnlockedBy { get; init; } = "";

    /// <summary>Free from the start.</summary>
    public bool Free { get; init; }

    /// <summary>Hearing it in play (reaching the map, meeting the boss) unlocks it.</summary>
    public bool OpensWhenHeard { get; init; } = true;

    public bool Loop => Kind != TrackKind.Sting;
}

/// <summary>
/// The music catalogue: every track, which map owns which, and the Boombox's
/// unlock rules. Each map has its own run track (<see cref="ForMap"/>; a
/// StageDef.MusicId, when set, overrides it) and
/// a remix earned by clearing that map (its "qm_map_*" quest). Map, boss and
/// sting tracks unlock the first time they play in a real run, or through the
/// quest that would have played them, so profiles from before the Boombox
/// already own what they have seen.
///
/// "Heard" is a plain profile stat (<c>music.heard.&lt;id&gt;</c>), written only
/// by <see cref="MusicDirector"/>, which never exists in automated launches.
/// </summary>
public static class MusicTracks
{
    public static readonly IReadOnlyList<TrackDef> All = new List<TrackDef>
    {
        // ---- menu
        new() { Id = "title", Title = "Tide Turning", Where = "Title screen", Kind = TrackKind.Menu, Free = true },
        new() { Id = "lounge", Title = "Seashell Lounge", Where = "Menu (Boombox)", Kind = TrackKind.Menu, Free = true },

        // ---- one run track per map
        new() { Id = "meadow", Title = "Thornmeadow Morning", Where = "Thornmeadow", Kind = TrackKind.Map, MapId = "thornmeadow", Free = true },
        new() { Id = "frost", Title = "Rimeglass Hush", Where = "Rimeglass Plateau", Kind = TrackKind.Map, MapId = "rimeglass", UnlockedBy = "qm_map_rimeglass" },
        new() { Id = "ashlands", Title = "Cinder Stride", Where = "Cinder Reach", Kind = TrackKind.Map, MapId = "cinder", UnlockedBy = "qm_map_cinder" },
        new() { Id = "graveyard", Title = "Gravemoor Lullaby", Where = "Gravemoor", Kind = TrackKind.Map, MapId = "gravemoor", UnlockedBy = "qm_map_gravemoor" },
        new() { Id = "tidal", Title = "Saltglass Drift", Where = "Saltglass Shallows", Kind = TrackKind.Map, MapId = "shallows", UnlockedBy = "qm_map_shallows" },
        new() { Id = "harbour", Title = "Stormwatch Lanterns", Where = "Stormwatch Harbour", Kind = TrackKind.Map, MapId = "stormwatch", UnlockedBy = "qm_map_stormwatch" },

        // ---- remixes: earned by clearing the map
        new() { Id = "meadow_dusk", Title = "Dandelion Dusk", Where = "Thornmeadow remix", Kind = TrackKind.Remix, MapId = "thornmeadow", UnlockedBy = "qm_map_thornmeadow", OpensWhenHeard = false },
        new() { Id = "frost_aurora", Title = "Aurora Chimes", Where = "Rimeglass remix", Kind = TrackKind.Remix, MapId = "rimeglass", UnlockedBy = "qm_map_rimeglass", OpensWhenHeard = false },
        new() { Id = "ashlands_forge", Title = "Ember Bounce", Where = "Cinder Reach remix", Kind = TrackKind.Remix, MapId = "cinder", UnlockedBy = "qm_map_cinder", OpensWhenHeard = false },
        new() { Id = "graveyard_waltz", Title = "Lantern Waltz", Where = "Gravemoor remix", Kind = TrackKind.Remix, MapId = "gravemoor", UnlockedBy = "qm_map_gravemoor", OpensWhenHeard = false },
        new() { Id = "tidal_lagoon", Title = "Lagoon Breeze", Where = "Saltglass remix", Kind = TrackKind.Remix, MapId = "shallows", UnlockedBy = "qm_map_shallows", OpensWhenHeard = false },
        new() { Id = "harbour_shanty", Title = "Lighthouse Shanty", Where = "Stormwatch remix", Kind = TrackKind.Remix, MapId = "stormwatch", UnlockedBy = "qm_map_stormwatch", OpensWhenHeard = false },

        // ---- the big moments
        new() { Id = "boss", Title = "Big Wave", Where = "Phase bosses", Kind = TrackKind.Boss, UnlockedBy = "bossfall" },
        new() { Id = "boss_final", Title = "Tidebreaker", Where = "Final boss", Kind = TrackKind.Boss, UnlockedBy = "q_boss_p3" },
        new() { Id = "swarm", Title = "Final Swarm", Where = "The Final Swarm", Kind = TrackKind.Swarm, UnlockedBy = "breakwater" },

        // ---- stingers
        new() { Id = "shop", Title = "Merchant Jingle", Where = "Opening a merchant", Kind = TrackKind.Sting },
        new() { Id = "victory", Title = "Boss Down", Where = "Final boss defeated", Kind = TrackKind.Sting, UnlockedBy = "q_boss_p3" },
        new() { Id = "fanfare", Title = "Tide Turned", Where = "Winning a run", Kind = TrackKind.Sting, UnlockedBy = "breakwater" },
        new() { Id = "defeat", Title = "Washed Ashore", Where = "Run lost", Kind = TrackKind.Sting, Free = true },
    };

    private static Dictionary<string, TrackDef>? _byId;

    public static TrackDef? ById(string? id)
    {
        if (id is null) return null;
        if (_byId is null)
        {
            _byId = new Dictionary<string, TrackDef>();
            foreach (TrackDef t in All) _byId.TryAdd(t.Id, t);
        }
        return _byId.TryGetValue(id, out TrackDef? d) ? d : null;
    }

    /// <summary>The run track of a map (Kind Map), or null.</summary>
    public static TrackDef? ForMap(string mapId)
    {
        foreach (TrackDef t in All) if (t.Kind == TrackKind.Map && t.MapId == mapId) return t;
        return null;
    }

    public static string HeardKey(string id) => "music.heard." + id;

    public static bool IsUnlocked(TrackDef t)
        => t.Free
           || (t.OpensWhenHeard && Profile.Get(HeardKey(t.Id)) > 0)
           || (t.UnlockedBy.Length > 0 && Profile.IsUnlocked(t.UnlockedBy));

    public static bool IsUnlocked(string id) => ById(id) is { } t && IsUnlocked(t);

    /// <summary>How to get it, for a locked row.</summary>
    public static string HowToUnlock(TrackDef t, bool full = true)
    {
        UnlockDef? u = Unlocks.ById(t.UnlockedBy);
        // Built from translated pieces: the composite never matches one key.
        string quest = u is null ? "" : u.Secret && !Profile.IsUnlocked(u.Id) ? Loc.F("quest: {0}", u.Hint)
            : full ? Loc.F("quest \"{0}\": {1}", u.Name, u.Description) : Loc.F("quest \"{0}\"", u.Name);
        string heard = !t.OpensWhenHeard ? "" : t.Kind switch
        {
            TrackKind.Map => Loc.F("Reach {0}", t.Where),
            TrackKind.Boss => Loc.F("Meet the {0}", t.Where.ToLowerInvariant()),
            TrackKind.Swarm => Loc.T("Survive into the Final Swarm"),
            _ => Loc.F("Hear it once: {0}", t.Where.ToLowerInvariant()),
        };
        if (!full && quest.Length > 0 && u is not null && heard.Length == 0) quest = $"{quest}: {Loc.T(u.Description)}";
        if (heard.Length > 0 && quest.Length > 0) return Loc.F("{0}, or {1}", heard, quest);
        return heard.Length > 0 ? heard : quest.Length > 0 ? char.ToUpperInvariant(quest[0]) + quest[1..] : Loc.T("Locked");
    }

    /// <summary>Record that a track played in a real session. Returns true the
    /// first time (the caller saves). Never in automated launches.</summary>
    internal static bool MarkHeard(string id)
    {
        Launch.Parse();
        if (Launch.Automated || ById(id) is not { OpensWhenHeard: true } t || t.Free) return false;
        string key = HeardKey(id);
        if (Profile.Get(key) > 0) return false;
        Profile.Best(key, 1);
        return true;
    }

    /// <summary>Loop tracks the player owns: the shuffle pool.</summary>
    public static List<string> UnlockedLoops()
    {
        var list = new List<string>();
        foreach (TrackDef t in All) if (t.Loop && IsUnlocked(t)) list.Add(t.Id);
        return list;
    }
}
