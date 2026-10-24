using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using Wavebreak.Data;

namespace Wavebreak.Meta;

/// <summary>What a map challenge asks for. The challenge map is always the
/// first map of the run (its route starts there), so "the map" is phase 1.</summary>
public enum MapGoal
{
    /// <summary>Survive the waves until the map's boss arrives.</summary>
    Survive,
    /// <summary>Beat the map's boss (the portal opens).</summary>
    Clear,
    /// <summary>Beat the final boss and last 60 s in the Final Swarm.</summary>
    Swarm,
}

/// <summary>A map-specific change to the ground itself. Applied to the
/// challenge map's copy only (<see cref="MapChallenges.Twist"/>), plus a light
/// and fog pass at runtime for the visual ones.</summary>
public enum MapTwist { None, Brambles, Whiteout, Frost, Night, RestlessGraves, HighTide, LavaFloor, Ashfall }

public enum CosmeticKind { None, Hat, Skin }

/// <summary>
/// One per-map challenge: a map, a goal, a few of the global run modifiers
/// (<see cref="Challenges"/>), an optional map twist and pressure tier, and a
/// reward. Everything it does is data read by <see cref="MapChallenges"/> and
/// <see cref="MapChallengeRuntime"/>.
/// </summary>
public sealed record MapChallengeDef(
    string Id, string MapId, string Name, string Blurb,
    MapGoal Goal, string[] Modifiers, MapTwist Twist, int Tier, int Pearls,
    CosmeticKind Cosmetic = CosmeticKind.None, string CosmeticId = "")
{
    public StageDef? Map => Stages.ById(MapId);

    public string GoalText => Goal switch
    {
        MapGoal.Survive => Loc.F("Survive {0} until its boss arrives", Map?.DisplayName ?? MapId),
        MapGoal.Clear => Loc.F("Beat the boss of {0}", Map?.DisplayName ?? MapId),
        _ => Loc.F("Win the run, then last {0:0}s in the Final Swarm", MapChallenges.SwarmSeconds),
    };

    public string GoalShort => Goal switch
    {
        MapGoal.Survive => Loc.T("Reach the boss"),
        MapGoal.Clear => Loc.T("Beat the boss"),
        _ => Loc.T("Final Swarm"),
    };

    public string TierName => Loc.T(MapChallenges.TierNames[Math.Clamp(Tier, 0, MapChallenges.TierNames.Length - 1)]);

    public string RewardText
    {
        get
        {
            string s = Loc.F("{0} Pearls", Pearls);
            if (Cosmetic == CosmeticKind.Hat) s += " + " + Loc.F("Hat: {0}", Hats.ById(CosmeticId)?.Name ?? CosmeticId);
            else if (Cosmetic == CosmeticKind.Skin) s += " + " + Unlocks.DescribeSkin(CosmeticId);
            return s;
        }
    }

    /// <summary>"Riptide, Night, Hard" — the rules in one line.</summary>
    public string RulesText
    {
        get
        {
            var parts = new List<string>();
            if (Twist != MapTwist.None) parts.Add(MapChallenges.TwistName(Twist));
            foreach (string m in Modifiers) parts.Add(Loc.T(Challenges.ById(m)?.Name ?? m));
            if (Tier > 0) parts.Add(TierName);
            return parts.Count == 0 ? Loc.T("No modifiers") : string.Join("  ·  ", parts);
        }
    }
}

/// <summary>
/// Per-map challenges: 11 named runs for each of the six maps (66 in all),
/// built from the twelve global modifiers plus map twists (Gravemoor at night,
/// the Shallows at high tide, a lava floor on Cinder Reach) and three pressure
/// tiers (Hard / Harder / Hardest).
///
/// One may be selected at a time, on the challenge picker. A selected map
/// challenge sets the run's route to start on its map, adds its modifiers to
/// whatever global ones are picked, twists its map and scales enemy health and
/// damage by its tier. Completing its goal pays Pearls once (and a hat or skin
/// on the hardest ones) and is recorded in the profile (<c>mchal_&lt;id&gt;</c>).
///
/// Automated runs never pick one up from the profile; <c>--map-challenge=&lt;id&gt;</c>
/// forces one, which is how the twists and goals are tested.
/// </summary>
public static class MapChallenges
{
    public const float SwarmSeconds = 60f;

    public static readonly string[] TierNames = { "", "Hard", "Harder", "Hardest" };
    private static readonly float[] TierHealth = { 1f, 1.35f, 1.75f, 2.3f };
    private static readonly float[] TierDamage = { 1f, 1.2f, 1.4f, 1.65f };

    private static readonly string[] None = Array.Empty<string>();
    private static string[] M(params string[] ids) => ids;

    private static MapChallengeDef C(string id, string map, string name, string blurb, MapGoal goal, string[] mods,
                                     MapTwist twist, int tier, int pearls,
                                     CosmeticKind cosmetic = CosmeticKind.None, string cosmeticId = "")
        => new(id, map, name, blurb, goal, mods, twist, tier, pearls, cosmetic, cosmeticId);

    public static readonly IReadOnlyList<MapChallengeDef> All = new List<MapChallengeDef>
    {
        // ------------------------------------------------------ Thornmeadow
        C("tm_bramble", "thornmeadow", "Bramble Walk", "Thorn patches have crept over the hills. They scratch and they snag.",
          MapGoal.Clear, None, MapTwist.Brambles, 0, 40),
        C("tm_glass", "thornmeadow", "Glass Petals", "Half your health, half again your damage. Pretty, and easy to break.",
          MapGoal.Survive, M("glass"), MapTwist.None, 0, 35),
        C("tm_riptide", "thornmeadow", "Meadow Riptide", "Everything runs: you, them, the clock.",
          MapGoal.Clear, M("hyper"), MapTwist.None, 0, 50),
        C("tm_shoal", "thornmeadow", "Dandelion Shoal", "Twice the crowd, drifting in like seeds on the wind.",
          MapGoal.Clear, M("swarm"), MapTwist.None, 0, 60),
        C("tm_grazers", "thornmeadow", "Big Grazers", "Huge, slow and stubborn. Bring something that hits hard.",
          MapGoal.Clear, M("chonk"), MapTwist.None, 0, 60),
        C("tm_picnic", "thornmeadow", "Picnic Clock", "Short waves, and the boss must fall in time. Pack quickly.",
          MapGoal.Clear, M("speed"), MapTwist.None, 0, 70),
        C("tm_barefoot", "thornmeadow", "Barefoot Meadow", "No items at all. Just you, your weapons and your books.",
          MapGoal.Clear, M("noitems"), MapTwist.None, 0, 90),
        C("tm_hard", "thornmeadow", "Rough Meadow", "Thornmeadow, but the tide brought its bigger cousins.",
          MapGoal.Clear, None, MapTwist.None, 1, 80),
        C("tm_harder", "thornmeadow", "Wild Meadow", "Tougher crowds wading through the brambles.",
          MapGoal.Clear, None, MapTwist.Brambles, 2, 120),
        C("tm_hardest", "thornmeadow", "Meadow Untamed", "The hardest tide, fast and thorny. A hat for the brave.",
          MapGoal.Clear, M("hyper"), MapTwist.Brambles, 3, 200, CosmeticKind.Hat, "tricorne"),
        C("tm_crown", "thornmeadow", "Meadow Crown", "Start in the brambles with wardens on your heels, win, then hold the swarm.",
          MapGoal.Swarm, M("elites"), MapTwist.Brambles, 0, 300, CosmeticKind.Skin, "drifter.driftwood"),

        // ------------------------------------------------------- Rimeglass
        C("rg_whiteout", "rimeglass", "Whiteout", "A snow squall swallows the plateau. Keep the high ground in mind.",
          MapGoal.Clear, None, MapTwist.Whiteout, 0, 45),
        C("rg_hoarfrost", "rimeglass", "Hoarfrost Pools", "Frozen pools that grab your boots and do not let go.",
          MapGoal.Clear, None, MapTwist.Frost, 0, 50),
        C("rg_hop", "rimeglass", "Snowdrift Hop", "Half gravity on the mesas. Floaty, and so are they.",
          MapGoal.Survive, M("lowgrav"), MapTwist.None, 0, 35),
        C("rg_still", "rimeglass", "Frozen Still", "Your weapons are frozen for the first 45 seconds. Run.",
          MapGoal.Clear, M("pacifist"), MapTwist.None, 0, 55),
        C("rg_thaw", "rimeglass", "Thaw Surge", "Every so often the ice cracks and the crowd rushes in.",
          MapGoal.Clear, M("surge"), MapTwist.None, 0, 60),
        C("rg_hotfeet", "rimeglass", "Hot Feet", "Standing still burns, even on ice. Keep sliding.",
          MapGoal.Clear, M("molten"), MapTwist.None, 0, 60),
        C("rg_wardens", "rimeglass", "Ice Wardens", "Elite waves every 20 seconds, colder than the rest.",
          MapGoal.Clear, M("elites"), MapTwist.None, 0, 70),
        C("rg_hard", "rimeglass", "Cold Snap", "The plateau with sharper teeth.",
          MapGoal.Clear, None, MapTwist.None, 1, 80),
        C("rg_harder", "rimeglass", "Deep Freeze", "Tougher crowds among the frost pools.",
          MapGoal.Clear, None, MapTwist.Frost, 2, 120),
        C("rg_hardest", "rimeglass", "Absolute Zero", "Whiteout, giants and the hardest tide. A warm hat awaits.",
          MapGoal.Clear, M("chonk"), MapTwist.Whiteout, 3, 200, CosmeticKind.Hat, "souwester"),
        C("rg_crown", "rimeglass", "Glacier Crown", "Begin in the whiteout, win the run and hold the swarm.",
          MapGoal.Swarm, None, MapTwist.Whiteout, 0, 300, CosmeticKind.Skin, "gale_courier.dawn"),

        // ------------------------------------------------------- Gravemoor
        C("gm_night", "gravemoor", "Gravemoor by Night", "The moon is down and the fog is up. Your lantern is all the light there is.",
          MapGoal.Clear, None, MapTwist.Night, 0, 50),
        C("gm_restless", "gravemoor", "Restless Graves", "Twice the sunken graves, and they bite twice as hard.",
          MapGoal.Clear, None, MapTwist.RestlessGraves, 0, 55),
        C("gm_fret", "gravemoor", "Graveyard Fret", "Night and sea fret together. You will hear them first.",
          MapGoal.Survive, M("fog"), MapTwist.Night, 0, 45),
        C("gm_barrow", "gravemoor", "Barrow Kings", "The old kings rise, enormous and slow.",
          MapGoal.Clear, M("chonk"), MapTwist.None, 0, 60),
        C("gm_bones", "gravemoor", "Brittle Bones", "Glass hull among the graves. Hit hard, never get hit.",
          MapGoal.Clear, M("glass"), MapTwist.None, 0, 70),
        C("gm_souls", "gravemoor", "Shoal of Souls", "Twice the crowd, all of them in the dark.",
          MapGoal.Clear, M("swarm"), MapTwist.Night, 0, 80),
        C("gm_lantern", "gravemoor", "Lantern Run", "Night, short waves and a boss on a timer.",
          MapGoal.Clear, M("speed"), MapTwist.Night, 0, 90),
        C("gm_hard", "gravemoor", "Wake", "Gravemoor stirs, and it is in a mood.",
          MapGoal.Clear, None, MapTwist.None, 1, 80),
        C("gm_harder", "gravemoor", "Haunting", "Tougher dead, walking at night.",
          MapGoal.Clear, None, MapTwist.Night, 2, 120),
        C("gm_hardest", "gravemoor", "Danse Macabre", "Night, a doubled crowd and the hardest tide. Dance well, win a bonnet.",
          MapGoal.Clear, M("swarm"), MapTwist.Night, 3, 200, CosmeticKind.Hat, "shell_bonnet"),
        C("gm_crown", "gravemoor", "Moonlit Crown", "Start in the night, win the run and hold the swarm.",
          MapGoal.Swarm, None, MapTwist.Night, 0, 300, CosmeticKind.Skin, "stormwright.calm"),

        // --------------------------------------------------- Saltglass Shallows
        C("sh_hightide", "shallows", "High Tide", "The tide is in: twice the pools, and wider. Wade carefully.",
          MapGoal.Clear, None, MapTwist.HighTide, 0, 45),
        C("sh_flotsam", "shallows", "Flotsam", "High tide and half gravity. Bob along.",
          MapGoal.Survive, M("lowgrav"), MapTwist.HighTide, 0, 40),
        C("sh_riptide", "shallows", "Riptide Shallows", "Everything faster in the shallows.",
          MapGoal.Clear, M("hyper"), MapTwist.None, 0, 50),
        C("sh_doldrums", "shallows", "Doldrums", "Becalmed at high tide: no weapons for 45 seconds, and wet feet.",
          MapGoal.Clear, M("pacifist"), MapTwist.HighTide, 0, 65),
        C("sh_reef", "shallows", "Reef Wardens", "Elites patrol the reef every 20 seconds.",
          MapGoal.Clear, M("elites"), MapTwist.None, 0, 70),
        C("sh_kingtide", "shallows", "King Tide", "The highest tide, and spring surges on top. A wreath for the patient.",
          MapGoal.Clear, M("surge"), MapTwist.HighTide, 0, 80, CosmeticKind.Hat, "kelp_wreath"),
        C("sh_castaway", "shallows", "Castaway Cove", "No items. Whatever washes up, you cannot keep it.",
          MapGoal.Clear, M("noitems"), MapTwist.None, 0, 90),
        C("sh_hard", "shallows", "Choppy Water", "The Shallows with a bit of chop.",
          MapGoal.Clear, None, MapTwist.None, 1, 80),
        C("sh_harder", "shallows", "Heavy Swell", "Tougher crowds at high tide.",
          MapGoal.Clear, None, MapTwist.HighTide, 2, 120),
        C("sh_hardest", "shallows", "Maelstrom", "High tide, surges and the hardest tide. A starfish for the survivor.",
          MapGoal.Clear, M("surge"), MapTwist.HighTide, 3, 200, CosmeticKind.Hat, "starfish"),
        C("sh_crown", "shallows", "Tidal Crown", "Start at high tide, win the run and hold the swarm.",
          MapGoal.Swarm, None, MapTwist.HighTide, 0, 300, CosmeticKind.Skin, "gilded_augur.moth"),

        // ----------------------------------------------------- Cinder Reach
        C("cr_lava", "cinder", "The Floor Is Lava", "Lava pools have opened across the ridges. Jump them, slide past them, never stand in them.",
          MapGoal.Clear, None, MapTwist.LavaFloor, 0, 60),
        C("cr_ashfall", "cinder", "Ashfall", "A smoky orange haze settles over the Reach.",
          MapGoal.Clear, None, MapTwist.Ashfall, 0, 45),
        C("cr_swarm", "cinder", "Ember Swarm", "Twice the crowd, like sparks off a fire.",
          MapGoal.Clear, M("swarm"), MapTwist.None, 0, 60),
        C("cr_glass", "cinder", "Glassmaker's Test", "Glass hull over a lava floor. Hot, fragile, fast.",
          MapGoal.Survive, M("glass"), MapTwist.LavaFloor, 0, 60),
        C("cr_fuse", "cinder", "Burning Fuse", "Short waves and a boss on a timer.",
          MapGoal.Clear, M("speed"), MapTwist.None, 0, 70),
        C("cr_titans", "cinder", "Magma Titans", "Giants walking through the ashfall.",
          MapGoal.Clear, M("chonk"), MapTwist.Ashfall, 0, 80),
        C("cr_hotfoot", "cinder", "Hotfoot Reach", "Lava pools, and standing still burns too.",
          MapGoal.Clear, M("molten"), MapTwist.LavaFloor, 0, 90),
        C("cr_hard", "cinder", "Smoulder", "Cinder Reach, a few degrees hotter.",
          MapGoal.Clear, None, MapTwist.None, 1, 80),
        C("cr_harder", "cinder", "Blaze", "Tougher crowds on the lava floor.",
          MapGoal.Clear, None, MapTwist.LavaFloor, 2, 120),
        C("cr_hardest", "cinder", "Inferno", "Lava, speed and the hardest tide. The ash mother would approve.",
          MapGoal.Clear, M("hyper"), MapTwist.LavaFloor, 3, 220, CosmeticKind.Skin, "kiln_sister.ashmother"),
        C("cr_crown", "cinder", "Cinder Crown", "Glass hull on the lava floor: win the run and hold the swarm. The pearl tiara.",
          MapGoal.Swarm, M("glass"), MapTwist.LavaFloor, 0, 450, CosmeticKind.Hat, "pearl_tiara"),

        // ------------------------------------------------- Stormwatch Harbour
        // Appended last: the saved selection is an index into this list.
        C("sw_storm", "stormwatch", "Storm Night", "The storm has put the harbour lights out. Steer by the lighthouse.",
          MapGoal.Clear, None, MapTwist.Night, 0, 50),
        C("sw_flood", "stormwatch", "Flood Tide", "The sea is in the streets: wide pools of slow water everywhere.",
          MapGoal.Clear, None, MapTwist.HighTide, 0, 50),
        C("sw_fret", "stormwatch", "Harbour Fret", "Sea fret rolls in off the water. Listen for them round the corners.",
          MapGoal.Survive, M("fog"), MapTwist.None, 0, 40),
        C("sw_gale", "stormwatch", "Gale Warning", "Everything moves faster in a gale, you included.",
          MapGoal.Clear, M("hyper"), MapTwist.None, 0, 55),
        C("sw_market", "stormwatch", "Market Day", "Twice the crowd in the old town's streets.",
          MapGoal.Clear, M("swarm"), MapTwist.None, 0, 60),
        C("sw_watch", "stormwatch", "Night Watch", "Elite waves every 20 seconds, in the dark.",
          MapGoal.Clear, M("elites"), MapTwist.Night, 0, 80),
        C("sw_ballast", "stormwatch", "No Ballast", "No items at all. Sail light.",
          MapGoal.Clear, M("noitems"), MapTwist.None, 0, 90),
        C("sw_hard", "stormwatch", "Squall Line", "Stormwatch with the wind up.",
          MapGoal.Clear, None, MapTwist.None, 1, 80),
        C("sw_harder", "stormwatch", "Storm Surge", "Tougher crowds wading through flooded streets.",
          MapGoal.Clear, None, MapTwist.HighTide, 2, 120),
        C("sw_hardest", "stormwatch", "The Perfect Storm", "Night, a gale and the hardest tide. The keeper would be proud.",
          MapGoal.Clear, M("hyper"), MapTwist.Night, 3, 220),
        C("sw_crown", "stormwatch", "Lighthouse Crown", "Start in the storm, win the run and hold the swarm.",
          MapGoal.Swarm, None, MapTwist.Night, 0, 300),
    };

    /// <summary>Map ids in picker order.</summary>
    public static readonly string[] MapOrder = { "thornmeadow", "rimeglass", "gravemoor", "shallows", "cinder", "stormwatch" };

    public static IEnumerable<MapChallengeDef> ForMap(string mapId) => All.Where(c => c.MapId == mapId);

    public static MapChallengeDef? ById(string id) => All.FirstOrDefault(c => c.Id == id);

    public static int IndexOf(MapChallengeDef c)
    {
        for (int i = 0; i < All.Count; i++) if (All[i].Id == c.Id) return i;
        return -1;
    }

    public static string TwistName(MapTwist t) => Loc.T(t switch
    {
        MapTwist.Brambles => "Brambles",
        MapTwist.Whiteout => "Whiteout",
        MapTwist.Frost => "Hoarfrost",
        MapTwist.Night => "Night",
        MapTwist.RestlessGraves => "Restless Graves",
        MapTwist.HighTide => "High Tide",
        MapTwist.LavaFloor => "Lava Floor",
        MapTwist.Ashfall => "Ashfall",
        _ => "",
    });

    // ---------------------------------------------------------- selection

    private const string SelectedKey = "mchal_selected";
    private static string DoneKey(MapChallengeDef c) => "mchal_" + c.Id;

    /// <summary>The saved choice, or null for none.</summary>
    public static MapChallengeDef? Selected
    {
        get
        {
            Profile.EnsureLoaded();
            long i = Profile.GetMeta(SelectedKey) - 1;
            return i >= 0 && i < All.Count ? All[(int)i] : null;
        }
    }

    /// <summary>Select (or with null, clear). Does not save.</summary>
    public static void Select(MapChallengeDef? c)
        => Profile.SetMeta(SelectedKey, c is null ? 0 : IndexOf(c) + 1);

    public static long Completions(MapChallengeDef c)
    {
        Profile.EnsureLoaded();
        return Profile.GetMeta(DoneKey(c));
    }

    public static bool IsDone(MapChallengeDef c) => Completions(c) > 0;

    public static int DoneCount() => All.Count(IsDone);

    public static int DoneCount(string mapId) => ForMap(mapId).Count(IsDone);

    // ---------------------------------------------------------------- run

    /// <summary>The map challenge of the run in progress, or null.</summary>
    public static MapChallengeDef? Active { get; private set; }

    public static float HealthScale => Active is null ? 1f : TierHealth[Math.Clamp(Active.Tier, 0, 3)];
    public static float DamageScale => Active is null ? 1f : TierDamage[Math.Clamp(Active.Tier, 0, 3)];

    /// <summary>
    /// Resolve the run's map challenge and fold its modifiers into the global
    /// set. Called by <see cref="Challenges.BeginRun"/>; automated runs only
    /// get one through <c>--map-challenge=&lt;id&gt;</c>.
    /// </summary>
    public static IEnumerable<ChallengeDef> Merge(IEnumerable<ChallengeDef> set)
    {
        Launch.Parse();
        Active = Launch.SkipsMenu || Net.Coop.InRun ? null : Selected;   // co-op: none
        foreach (string arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--map-challenge=")) Active = ById(arg["--map-challenge=".Length..]);
        if (Active is null) return set;
        var merged = new List<ChallengeDef>(set);
        foreach (string id in Active.Modifiers)
            if (Challenges.ById(id) is { } c && !merged.Contains(c)) merged.Add(c);
        GD.Print($"META map-challenge {Active.Id} map={Active.MapId} goal={Active.Goal} twist={Active.Twist} tier={Active.Tier} " +
                 $"mods=[{string.Join(",", Active.Modifiers)}]");
        return merged;
    }

    /// <summary>Route id for the active map challenge ("start:&lt;map&gt;"), or null.</summary>
    public static string? RouteOverride => Active is null ? null : "start:" + Active.MapId;

    /// <summary>The run's maps with the active challenge's twist applied to
    /// its map (a copy: the shared stage table is never edited).</summary>
    public static IReadOnlyList<StageDef> Twist(IReadOnlyList<StageDef> stages)
    {
        if (Active is not { Twist: not MapTwist.None } mc) return stages;
        var list = new List<StageDef>(stages);
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Id != mc.MapId) continue;
            list[i] = ApplyTwist(Copy(list[i]), mc.Twist);
            GD.Print($"META map-twist {mc.Twist} on {mc.MapId}: pools={list[i].HazardPools} r={list[i].HazardRadius:0.#} " +
                     $"slow={list[i].HazardSlow:0.##} dmg={list[i].HazardDamage:0.###}");
        }
        return list;
    }

    /// <summary>The stage changes a twist makes. Visual twists (night,
    /// whiteout, ashfall) are applied by <see cref="MapChallengeRuntime"/>.</summary>
    public static StageDef ApplyTwist(StageDef s, MapTwist twist)
    {
        switch (twist)
        {
            case MapTwist.Brambles:
                s.HazardPools = 14; s.HazardRadius = 4.5f; s.HazardSlow = 0.75f; s.HazardDamage = 0.02f;
                s.HazardColor = new Color(0.45f, 0.75f, 0.25f); s.HazardName = "bramble patch";
                break;
            case MapTwist.Frost:
                s.HazardPools = 16; s.HazardRadius = 5.5f; s.HazardSlow = 0.5f; s.HazardDamage = 0f;
                s.HazardColor = new Color(0.70f, 0.90f, 1.00f); s.HazardName = "hoarfrost";
                break;
            case MapTwist.RestlessGraves:
                s.HazardPools = Math.Max(1, s.HazardPools) * 2; s.HazardDamage = Math.Max(0.02f, s.HazardDamage) * 2f;
                if (s.HazardName.Length == 0) { s.HazardName = "restless grave"; s.HazardColor = new Color(0.55f, 0.95f, 0.45f); }
                break;
            case MapTwist.HighTide:
                s.HazardPools = Math.Max(8, s.HazardPools) * 2; s.HazardRadius = Math.Max(5f, s.HazardRadius) * 1.4f;
                s.HazardSlow = Math.Min(s.HazardSlow, 0.6f);
                if (s.HazardName.Length == 0) { s.HazardName = "tide pool"; s.HazardColor = new Color(0.25f, 0.75f, 0.85f); }
                break;
            case MapTwist.LavaFloor:
                s.HazardPools = 20; s.HazardRadius = 6f; s.HazardSlow = 0.85f; s.HazardDamage = 0.07f;
                s.HazardColor = new Color(1.00f, 0.42f, 0.10f); s.HazardName = "lava";
                break;
        }
        return s;
    }

    /// <summary>A field-for-field copy (reflection, like Stages.ForPhase, so
    /// fields others add to StageDef are carried over).</summary>
    private static StageDef Copy(StageDef map)
    {
        var copy = new StageDef();
        foreach (PropertyInfo p in typeof(StageDef).GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            if (p.CanRead && p.CanWrite) p.SetValue(copy, p.GetValue(map));
        return copy;
    }

    // ------------------------------------------------------------- reward

    /// <summary>Record the completion and pay the reward. Pearls and the
    /// cosmetic only the first time; later completions are counted.
    /// Returns the reward line, or "" when it was already done.</summary>
    public static string Complete(MapChallengeDef c)
    {
        Profile.EnsureLoaded();
        bool first = !IsDone(c);
        Profile.SetMeta(DoneKey(c), Completions(c) + 1);
        string reward = "";
        if (first)
        {
            Meta.Pearls.Earn(c.Pearls);
            GrantCosmetic(c);
            reward = c.RewardText;
        }
        Profile.Save();
        GD.Print($"META map-challenge complete {c.Id} first={first} reward=\"{reward}\" done={DoneCount()}/{All.Count}");
        return reward;
    }

    /// <summary>Hats bought with Pearls are owned through their bought flag;
    /// quest skins are owned through their quest, which this marks earned.</summary>
    public static void GrantCosmetic(MapChallengeDef c)
    {
        if (c.Cosmetic == CosmeticKind.Hat && Hats.ById(c.CosmeticId) is { } hat)
        {
            if (hat.UnlockedBy is { } q) Profile.Unlock(q);
            else Profile.SetMeta(Hats.BoughtKey(hat.Id), 1);
        }
        else if (c.Cosmetic == CosmeticKind.Skin && Unlocks.SourceOf(c.CosmeticId) is { } row)
            Profile.Unlock(row);
    }

    /// <summary>True when the cosmetic is already owned (shown as such on the card).</summary>
    public static bool CosmeticOwned(MapChallengeDef c) => c.Cosmetic switch
    {
        CosmeticKind.Hat => Hats.ById(c.CosmeticId)?.IsOwned ?? false,
        CosmeticKind.Skin => Unlocks.IsContentUnlocked(c.CosmeticId),
        _ => true,
    };
}
