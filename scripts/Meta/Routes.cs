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
/// Classic (index 0) is <see cref="Stages.Run"/> itself and the default; any
/// automated run uses it unless <c>--route=&lt;id&gt;</c> or
/// <c>--route=mapA,mapB,mapC</c> is given, so <c>--auto</c> and the smoke test
/// never depend on what was last picked in the menu. Other routes copy each
/// map with the classic numbers for its phase (<see cref="Stages.ForPhase"/>),
/// so a route changes where you fight, not how hard each phase is.
///
/// Drawn routes pick 3 of the 5 maps from the run seed: a seeded run replays
/// the same draw.
/// </summary>
public static class Routes
{
    public const int Length = 3;

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
            new("draw", "Tide Draw", "Three of the five maps, in a random order, drawn from the run's seed.",
                Array.Empty<string>()),
        };
        // "Start on X": X first, then two of the other four drawn per run.
        foreach (string id in new[] { "thornmeadow", "shallows", "rimeglass", "gravemoor", "cinder" })
            list.Add(new RouteDef("start:" + id, "Start: " + (Stages.ById(id)?.DisplayName ?? id),
                                  "This map first; the next two are drawn from the rest each run.",
                                  Array.Empty<string>(), id));
        return list;
    }

    private const string SelectedKey = "route";

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
    /// The maps for a new run, in phase order. Classic returns
    /// <see cref="Stages.Run"/> unchanged.
    /// </summary>
    public static IReadOnlyList<StageDef> ForRun(ulong seed)
    {
        Profile.EnsureLoaded();
        Launch.Parse();
        RouteDef route = Launch.SkipsMenu || Net.Coop.InRun ? All[0] : Selected;   // co-op: every machine plays Classic
        if (MapChallenges.RouteOverride is { } forcedRoute && ById(forcedRoute) is { } mr) route = mr;   // map challenge
        string[]? explicitMaps = null;
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (!arg.StartsWith("--route=")) continue;
            string v = arg["--route=".Length..];
            if (ById(v) is { } r) route = r;
            else explicitMaps = v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        IReadOnlyList<StageDef> stages;
        if (explicitMaps is { Length: > 0 })
        {
            route = new RouteDef("custom", "Custom", "", explicitMaps);
            stages = Build(route, seed);
        }
        else stages = route.Id == "classic" ? Stages.Run : Build(route, seed);

        stages = MapChallenges.Twist(stages);   // map challenge twist, on a copy of its map
        Active = route;
        GD.Print($"META route={route.Id} maps={string.Join(",", stages.Select(s => s.Id))}");
        return stages;
    }

    /// <summary>The maps of <paramref name="route"/> for a run of
    /// <paramref name="seed"/>, each scaled for its phase.</summary>
    public static IReadOnlyList<StageDef> Build(RouteDef route, ulong seed)
    {
        var maps = new List<StageDef>();
        if (!route.Drawn)
        {
            foreach (string id in route.MapIds)
                if (Stages.ById(id) is { } s) maps.Add(s);
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
            while (maps.Count < Length && pool.Count > 0)
            {
                int i = rng.RangeInt(0, pool.Count);
                maps.Add(pool[i]);
                pool.RemoveAt(i);
            }
        }
        if (maps.Count == 0) return Stages.Run;

        var scaled = new List<StageDef>(maps.Count);
        for (int i = 0; i < maps.Count; i++) scaled.Add(Stages.ForPhase(maps[i], i));
        return scaled;
    }

    /// <summary>"Thornmeadow → Rimeglass Plateau → Cinder Reach", or the draw rule.</summary>
    public static string DescribeMaps(RouteDef r)
    {
        // Arabic reads right to left, so its arrow points left.
        string arrow = Loc.IsRtl ? "  <  " : "  >  ";
        if (!r.Drawn) return string.Join(arrow, r.MapIds.Select(id => Loc.T(Stages.ById(id)?.DisplayName ?? id)));
        string first = r.StartId.Length > 0 ? Loc.T(Stages.ById(r.StartId)?.DisplayName ?? r.StartId) : "?";
        return $"{first}{arrow}?{arrow}?";
    }
}
