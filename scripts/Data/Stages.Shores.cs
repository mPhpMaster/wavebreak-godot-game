using System.Collections.Generic;
using System.Reflection;
using Godot;

namespace Wavebreak.Data;

/// <summary>
/// The maps outside the classic route, and the lookups a route needs.
///
/// <see cref="Run"/> stays the default route (Thornmeadow, Rimeglass, Cinder)
/// and is untouched: automated runs and the smoke test play it exactly as
/// before. <see cref="All"/> is every map, for route building, the boundary
/// check and the codex.
///
/// A map's enemy multipliers belong to the phase it is played in, not to the
/// map: Gravemoor played first must open as gently as Thornmeadow does. So
/// <see cref="ForPhase"/> copies a map and gives the copy the classic route's
/// numbers for that position; the two extra maps' own numbers below are only
/// what they would carry in phase two.
/// </summary>
public static partial class Stages
{
    private static List<StageDef>? _extra;
    private static List<StageDef>? _all;

    /// <summary>Maps that are not on the classic route.</summary>
    public static IReadOnlyList<StageDef> Extra => _extra ??= BuildExtra();

    /// <summary>Every map, classic route first, then the extras.</summary>
    public static IReadOnlyList<StageDef> All
    {
        get
        {
            if (_all is not null) return _all;
            _all = new List<StageDef>(Run);
            _all.AddRange(Extra);
            return _all;
        }
    }

    public static StageDef? ById(string id)
    {
        foreach (StageDef s in All) if (s.Id == id) return s;
        return null;
    }

    /// <summary>
    /// <paramref name="map"/> as played in phase <paramref name="index"/>
    /// (0-based) of a 3-map route: a copy carrying the classic route's enemy,
    /// boss and timing numbers for that position. Returns the map itself when
    /// it already is the classic map for that position, so the default route
    /// is the very same objects.
    /// </summary>
    public static StageDef ForPhase(StageDef map, int index)
    {
        StageDef template = Run[Mathf.Clamp(index, 0, Run.Count - 1)];
        if (ReferenceEquals(map, template)) return map;

        var copy = new StageDef();
        // Reflection rather than a hand list: fields other people add to
        // StageDef (music, enemy lists) are carried over without anyone
        // remembering to edit this copy.
        foreach (PropertyInfo p in typeof(StageDef).GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            if (p.CanRead && p.CanWrite) p.SetValue(copy, p.GetValue(map));

        copy.EnemyHealthScale = template.EnemyHealthScale;
        copy.EnemyDamageScale = template.EnemyDamageScale;
        copy.EnemySpeedScale = template.EnemySpeedScale;
        copy.BossHealthScale = template.BossHealthScale;
        copy.PhaseSeconds = template.PhaseSeconds;
        copy.BossGraceSeconds = template.BossGraceSeconds;
        return copy;
    }

    private static List<StageDef> BuildExtra() => new()
    {
        // ---- Gravemoor. A pentagon of barrows and crypts under a violet dusk.
        // Five sides means five deep corners, the worst places on any map to be
        // cornered; low grave mounds everywhere and many solid crypts make it
        // the map where the crowd piles up behind things. The sunken graves
        // slow and gnaw: crossing one is fine, standing in one is not.
        new StageDef
        {
            Id = "gravemoor",
            DisplayName = "Gravemoor",
            Tagline = "Barrows and crypts. Keep out of the open graves.",
            LayoutSeed = 6613,
            Sides = 5, Radius = 150f, ClearRadius = 14f,
            Theme = "graveyard", PropDensity = 0.8f,
            Wall = new Color(0.55f, 0.42f, 0.85f),
            Terrain = new Color(0.36f, 0.34f, 0.42f),
            Mounds = 22, MoundHeight = 1.6f,
            Platforms = 4, PlatformHeight = 3.2f,
            Ridges = 2, RidgeHeight = 2.0f,
            Landmarks = 18,
            Chests = 7,
            Merchants = 1, Microwaves = 1, Breakables = 34,
            ChargeShrines = 3, ChallengeShrines = 2, StatueShrines = 2, GreedShrines = 1,
            PhaseSeconds = 210f, BossGraceSeconds = 120f,
            EnemyHealthScale = 1.25f, EnemyDamageScale = 1.12f, EnemySpeedScale = 1.04f,
            HazardPools = 12, HazardRadius = 4.2f, HazardSlow = 0.7f, HazardDamage = 0.02f,
            HazardColor = new Color(0.55f, 0.95f, 0.45f), HazardName = "sunken grave",
        },

        // ---- Saltglass Shallows. A wide twelve-sided beach at low tide: long
        // dune ridges to slide, a few piers (platforms) to stand on, and almost
        // nothing solid. The open sand is fast; the tide pools between the
        // dunes are not, and the crowd does not care about them.
        new StageDef
        {
            Id = "shallows",
            DisplayName = "Saltglass Shallows",
            Tagline = "Open sand, slow water. Read the tide pools.",
            LayoutSeed = 9127,
            Sides = 12, Radius = 160f, ClearRadius = 15f,
            Theme = "tidal", PropDensity = 0.55f,
            Wall = new Color(0.25f, 0.80f, 0.85f),
            Terrain = new Color(0.78f, 0.70f, 0.52f),
            Mounds = 6, MoundHeight = 1.8f,
            Platforms = 6, PlatformHeight = 2.8f,
            Ridges = 14, RidgeHeight = 2.0f,
            Landmarks = 6,
            Chests = 7,
            Merchants = 1, Microwaves = 1, Breakables = 30,
            ChargeShrines = 4, ChallengeShrines = 1, StatueShrines = 1, GreedShrines = 1,
            PhaseSeconds = 210f, BossGraceSeconds = 120f,
            EnemyHealthScale = 1.25f, EnemyDamageScale = 1.12f, EnemySpeedScale = 1.04f,
            HazardPools = 16, HazardRadius = 6.5f, HazardSlow = 0.6f, HazardDamage = 0f,
            HazardColor = new Color(0.25f, 0.75f, 0.85f), HazardName = "tide pool",
        },
    };
}
