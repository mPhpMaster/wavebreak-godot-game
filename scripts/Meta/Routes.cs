using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Wavebreak.Core;
using Wavebreak.Data;

namespace Wavebreak.Meta;

/// <summary>A run's map sequence. <see cref="MapIds"/> is fixed; an empty list
/// means "drawn per run" (see <see cref="Routes.Build"/>), and
/// <see cref="StartId"/> fixes only the first map of a draw.</summary>
public sealed record RouteDef(string Id, string Name, string Blurb, string[] MapIds, string StartId = "")
{
    public bool Drawn => MapIds.Length == 0;
}

/// <summary>
/// Which maps a run visits, picked on the play page.
///
/// Classic (index 0) is the default, built on <see cref="Stages.Run"/>; any
/// automated run uses it unless <c>--route=&lt;id&gt;</c> or
/// <c>--route=mapA,mapB,mapC</c> is given, so <c>--auto</c> and the smoke test
/// never depend on what was last picked in the menu. Other routes copy each
/// map with the classic numbers for its phase (<see cref="Stages.ForPhase"/>),
/// so a route changes where you fight, not how hard each phase is.
///
/// Drawn routes pick their maps from the run seed: a seeded run replays the
/// same draw.
///
/// How many maps depends on the tier (<see cref="DifficultyTier.Phases"/>: 4 on
/// Normal and Hard, 5 on Nightmare and Tempest, 6 from Abyss up). A run longer
/// than a route's three uses more maps, never one twice: Classic becomes the
/// maps in rising order
/// (<see cref="Stages.LongOrder"/>), a fixed route gets the unused maps slotted
/// in before its last one, and a draw draws more. Each map is scaled for its
/// position (<see cref="Stages.ForPhase(StageDef, int, int)"/>), so phases 4
/// and 5 keep stepping up, and the final boss is always on the last map.
/// </summary>
public static class Routes
{
    /// <summary>Maps in a route as written, and in a tier that asks for no more
    /// (every tier today asks for at least four: <see cref="PhasesFor"/>).</summary>
    public const int Length = 3;

    /// <summary>Maps in a run on <paramref name="tier"/>: its phase count, no
    /// more than there are maps.</summary>
    public static int PhasesFor(DifficultyTier tier) => Math.Clamp(tier.Phases, 1, Stages.All.Count);

    public static readonly IReadOnlyList<RouteDef> All = BuildAll();

    private static List<RouteDef> BuildAll()
    {
        var list = new List<RouteDef>
        {
            new("classic", "Classic", "Thornmeadow, Rimeglass Plateau, Cinder Reach. The tide as charted.",
                new[] { "thornmeadow", "rimeglass", "cinder" }),
            new("undertow", "Undertow", "Saltglass Shallows, Rimeglass Plateau, Gravemoor. Wet, then cold, then dead.",
                new[] { "shallows", "rimeglass", "gravemoor" }),
            new("lantern", "Lantern Road", "Gravemoor, Thornmeadow, Cinder Reach. Start in the dark.",
                new[] { "gravemoor", "thornmeadow", "cinder" }),
            new("draw", "Tide Draw", "Maps drawn from the run's seed, in a random order.",
                Array.Empty<string>()),
        };
        // "Start on X": X first, then the rest drawn per run.
        foreach (string id in new[] { "thornmeadow", "shallows", "rimeglass", "gravemoor", "cinder", "stormwatch" })
            list.Add(new RouteDef("start:" + id, "Start: " + (Stages.ById(id)?.DisplayName ?? id),
                                  "This map first; the rest are drawn each run.",
                                  Array.Empty<string>(), id));
        // Appended after the start routes: the saved choice is an index.
        // --route=beacon forces Stormwatch Harbour first (tests, captures).
        list.Add(new("beacon", "Beacon Road", "Stormwatch Harbour, Gravemoor, Cinder Reach. Follow the lighthouse into the dark.",
                     new[] { "stormwatch", "gravemoor", "cinder" }));
        list.Add(new("reef-dive", "Reef Dive (side map)",
            "Lumenreef Basin, Saltglass Shallows, Stormwatch Harbour. Swim through an optional underwater expedition.",
            new[] { "lumenreef", "shallows", "stormwatch" }));
        list.Add(new("skyline-run", "Bellspire Ascent (side map)",
            "Bellspire Ward, Stormwatch Harbour, Cinder Reach. Grapple through a city under siege by stone wardens.",
            new[] { "bellspire", "stormwatch", "cinder" }));
        // Classic's maps with a seeded puzzle map before each (Game/StageDirector.Puzzle.cs).
        list.Add(new(PuzzleId, Game.Puzzle.PuzzleText.RouteName,
            "Classic's maps, each after a short puzzle map. Solve it fast for a better reward for the fight ahead.",
            new[] { "thornmeadow", "rimeglass", "cinder" }));
        return list;
    }

    private const string SelectedKey = "route";

    /// <summary>The puzzle route: Classic's maps, a puzzle map before each.</summary>
    public const string PuzzleId = "puzzle";

    /// <summary>Routes that play Classic's maps in Classic's order.</summary>
    public static bool ClassicMaps(RouteDef r) => r.Id is "classic" or PuzzleId;

    public static RouteDef? ById(string id) => All.FirstOrDefault(r => r.Id == id);

    public static int IndexOf(RouteDef r)
    {
        for (int i = 0; i < All.Count; i++) if (All[i].Id == r.Id) return i;
        return 0;
    }

    /// <summary>The player's saved choice.</summary>
    public static RouteDef Selected
    {
        get
        {
            Profile.EnsureLoaded();
            int i = (int)Math.Clamp(Profile.GetMeta(SelectedKey), 0L, All.Count - 1);
            return All[i];
        }
    }

    /// <summary>Step the saved choice by <paramref name="step"/>, wrapping. Does not save.</summary>
    public static RouteDef Cycle(int step)
    {
        int n = All.Count;
        int i = ((IndexOf(Selected) + step) % n + n) % n;
        Profile.SetMeta(SelectedKey, i);
        return All[i];
    }

    /// <summary>The route of the run in progress (for logs and the HUD).</summary>
    public static RouteDef Active { get; private set; } = null!;

    /// <summary>
    /// The maps for a new run, in phase order. A three-map Classic returns
    /// <see cref="Stages.Run"/> unchanged.
    /// </summary>
    public static IReadOnlyList<StageDef> ForRun(ulong seed)
    {
        Profile.EnsureLoaded();
        Launch.Parse();
        RouteDef route = Net.Coop.InRun ? ById(Net.Coop.RouteId) ?? All[0]
            : Launch.SkipsMenu ? All[0] : Selected;
        if (!Net.Coop.InRun && MapChallenges.RouteOverride is { } forcedRoute && ById(forcedRoute) is { } mr) route = mr;
        string[]? explicitMaps = null;
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (Net.Coop.InRun) break; // The host's route is authoritative on every peer.
            if (!arg.StartsWith("--route=")) continue;
            string v = arg["--route=".Length..];
            if (ById(v) is { } r) route = r;
            else explicitMaps = v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        int phases = PhasesFor(DifficultyTiers.Active);
        IReadOnlyList<StageDef> stages;
        if (explicitMaps is { Length: > 0 })
        {
            // An explicit list is played as given, at its own length.
            route = new RouteDef("custom", "Custom", "", explicitMaps);
            stages = Build(route, seed, explicitMaps.Length);
        }
        else stages = ClassicMaps(route) && phases == Length ? Stages.Run : Build(route, seed, phases);

        stages = MapChallenges.Twist(stages);   // map challenge twist, on a copy of its map
        EndlessMode.BindSeed(seed);
        Active = route;
        GD.Print($"META route={route.Id} maps={string.Join(",", stages.Select(s => s.Id))}");
        return stages;
    }

    /// <summary>The maps of <paramref name="route"/> for a run of
    /// <paramref name="seed"/>, each scaled for its phase.</summary>
    public static IReadOnlyList<StageDef> Build(RouteDef route, ulong seed) => Build(route, seed, Length);

    /// <summary>The maps of <paramref name="route"/> for a run of
    /// <paramref name="phases"/> maps, each scaled for its phase.</summary>
    public static IReadOnlyList<StageDef> Build(RouteDef route, ulong seed, int phases)
    {
        phases = Math.Clamp(phases, 1, Stages.All.Count);
        var maps = new List<StageDef>();
        if (!route.Drawn && ClassicMaps(route) && phases > Length)
        {
            // The classic three plus the extra maps they need, all in rising
            // order: Gravemoor before Shallows, so a 4-map run adds the harder one.
            var keep = new HashSet<string>(route.MapIds);
            foreach (string id in new[] { "gravemoor", "stormwatch", "shallows", "rimeglass", "thornmeadow", "cinder" })
            {
                if (keep.Count >= phases) break;
                keep.Add(id);
            }
            foreach (string id in Stages.LongOrder)
                if (keep.Contains(id) && Stages.ById(id) is { } s) maps.Add(s);
        }
        else if (!route.Drawn)
        {
            foreach (string id in route.MapIds)
                if (Stages.ById(id) is { } s) maps.Add(s);
            if (route.Id == "reef-dive")
            {
                foreach (string id in new[] { "cinder", "gravemoor", "rimeglass" })
                {
                    if (maps.Count >= phases) break;
                    if (Stages.ById(id) is { } extension) maps.Add(extension);
                }
            }
            // Longer than the route: the unused maps, in rising order, go in
            // before its last map (the route's finale stays the finale).
            // Shorter: drop from the middle, keeping the start and the finale.
            foreach (string id in Stages.LongOrder)
            {
                if (maps.Count >= phases || maps.Count == 0) break;
                if (maps.Exists(m => m.Id == id) || Stages.ById(id) is not { } extra) continue;
                maps.Insert(maps.Count - 1, extra);
            }
            while (maps.Count > phases && maps.Count > 1) maps.RemoveAt(maps.Count - 2);
        }
        else
        {
            // Its own stream: drawing the route never shifts the run's Rng.
            var rng = new Rng(seed ^ 0xB0A7_5EA5UL);
            var pool = Stages.All.ToList();
            if (route.StartId.Length > 0 && Stages.ById(route.StartId) is { } first)
            {
                maps.Add(first);
                pool.Remove(first);
            }
            while (maps.Count < phases && pool.Count > 0)
            {
                int i = rng.RangeInt(0, pool.Count);
                maps.Add(pool[i]);
                pool.RemoveAt(i);
            }
        }
        if (maps.Count == 0) return Stages.Run;

        var scaled = new List<StageDef>(maps.Count);
        for (int i = 0; i < maps.Count; i++) scaled.Add(Stages.ForPhase(maps[i], i, maps.Count));
        return scaled;
    }

    /// <summary>
    /// The maps of Endless loop <paramref name="loop"/>: a pure function of the
    /// first loop and the seed, so every machine holds the same list and a late
    /// joiner can rebuild it from the count alone. Each loop takes the maps the
    /// last one left out before any it played, in a fresh order that does not
    /// reopen on the map it just closed on. Side maps stay on their own routes.
    /// </summary>
    public static IReadOnlyList<StageDef> ForLoop(IReadOnlyList<StageDef> firstLoop, ulong seed, int loop)
    {
        List<string> played = firstLoop.Select(s => s.Id).ToList();
        for (int l = 1; l <= loop; l++)
        {
            var rng = new Rng(seed ^ 0xE1D1_E55UL ^ ((ulong)l * 0x9E3779B97F4A7C15UL));
            List<string> fresh = Shuffled(Stages.All.Select(s => s.Id).Where(id => !played.Contains(id)).ToList(), rng);
            List<string> again = Shuffled(Stages.All.Select(s => s.Id).Where(played.Contains).ToList(), rng);
            List<string> next = Shuffled(fresh.Concat(again).Take(firstLoop.Count).ToList(), rng);
            if (next.Count > 1 && next[0] == played[^1]) (next[0], next[^1]) = (next[^1], next[0]);
            played = next;
        }
        var maps = new List<StageDef>(played.Count);
        foreach (string id in played)
            if (Stages.ById(id) is { } map) maps.Add(Stages.ForPhase(map, maps.Count, played.Count));
        return maps;
    }

    private static List<string> Shuffled(List<string> ids, Rng rng)
    {
        for (int i = ids.Count - 1; i > 0; i--)
        {
            int j = rng.RangeInt(0, i + 1);
            (ids[i], ids[j]) = (ids[j], ids[i]);
        }
        return ids;
    }

    /// <summary>"Thornmeadow → Rimeglass Plateau → Cinder Reach", or the draw rule.</summary>
    public static string DescribeMaps(RouteDef r)
    {
        // Arabic reads right to left, so its arrow points left.
        string arrow = Loc.IsRtl ? "  <  " : "  >  ";
        if (r.Id == PuzzleId)
        {
            // Puzzle > map > puzzle > map ...
            string puzzle = Loc.T("Puzzle");
            return string.Join(arrow, r.MapIds.Select(id => puzzle + arrow + Loc.T(Stages.ById(id)?.DisplayName ?? id)));
        }
        if (!r.Drawn) return string.Join(arrow, r.MapIds.Select(id => Loc.T(Stages.ById(id)?.DisplayName ?? id)));
        string first = r.StartId.Length > 0 ? Loc.T(Stages.ById(r.StartId)?.DisplayName ?? r.StartId) : "?";
        return $"{first}{arrow}?{arrow}?";
    }
}
