using System.Collections.Generic;
using Godot;

namespace Wavebreak.Data;

/// <summary>What the arena's loose decoration is made of. A look, not a
/// mechanic: scatter has no collision, and exists so the floor has something to
/// judge speed against. <see cref="StageDef.SolidCount"/> is the part that
/// blocks.</summary>
public enum ObstacleStyle
{
    /// <summary>Squat boxes at random yaw.</summary>
    Crates,
    /// <summary>Tall thin columns — reads as a forest of posts.</summary>
    Pillars,
    /// <summary>Tilted wedges jutting out of the floor.</summary>
    Shards,
    /// <summary>Flattened boulders.</summary>
    Stones,
}

/// <summary>
/// One map, and the phase of the run that is played on it.
///
/// A stage is data for the same reason weapons and enemies are: adding a fourth
/// map, or re-tuning how long the second phase lasts, should be an edit to a
/// table and not a new class. <see cref="Game.Arena"/> reads every field here
/// and nothing else, so two stages that differ only in numbers produce two
/// genuinely different places.
///
/// The phase numbers live on the stage rather than on a separate phase table
/// because in this game they are the same thing — a phase *is* a map — and two
/// tables indexed in parallel are two tables that can drift out of step.
/// </summary>
[GlobalClass]
public partial class StageDef : Resource
{
    [Export] public string Id { get; set; } = "";
    [Export] public string DisplayName { get; set; } = "";

    /// <summary>One line under the title card. Tells the player what is new
    /// about this ground before they have to find out.</summary>
    [Export] public string Tagline { get; set; } = "";

    /// <summary>Seeds the layout. Fixed per stage rather than per run so a map
    /// is a place the player can learn, not a fresh scramble every time.</summary>
    [Export] public int LayoutSeed { get; set; } = 1;

    // ---------------------------------------------------------------- shape

    /// <summary>Sides of the floor polygon. 48 and up reads as a circle; 6 or 8
    /// give corners, which changes where the player can get trapped.</summary>
    [Export] public int Sides { get; set; } = 64;

    /// <summary>Circumradius in metres — the distance to a corner. The usable
    /// radius is smaller on low-sided shapes; see <see cref="InRadius"/>.</summary>
    [Export] public float Radius { get; set; } = 60f;

    /// <summary>Distance from the centre to the middle of a wall: the radius a
    /// circle drawn inside the floor can have. This, not <see cref="Radius"/>,
    /// is what clamps and steering should trust.</summary>
    public float InRadius => Radius * Mathf.Cos(Mathf.Pi / Mathf.Max(3, Sides));

    /// <summary>Kept free of anything solid. The portal opens and the player
    /// arrives here, so it must always be reachable and never inside a mound.</summary>
    [Export] public float ClearRadius { get; set; } = 10f;

    // -------------------------------------------------------------- palette

    [Export] public Color Ground { get; set; } = new(0.16f, 0.18f, 0.22f);
    [Export] public Color Grid { get; set; } = new(0.24f, 0.27f, 0.33f);
    [Export] public Color Wall { get; set; } = new(0.35f, 0.30f, 0.45f);

    /// <summary>Colour of everything you can stand on that is not the floor.
    /// Kept distinct from the floor so a slope is visible as a slope from a
    /// third-person camera, where shading alone is too subtle.</summary>
    [Export] public Color Terrain { get; set; } = new(0.30f, 0.32f, 0.42f);

    [Export] public Color ObstacleA { get; set; } = new(0.24f, 0.27f, 0.33f);
    [Export] public Color ObstacleB { get; set; } = new(0.35f, 0.30f, 0.45f);

    // ------------------------------------------------------------ lighting

    [Export] public Color SkyTop { get; set; } = new(0.10f, 0.11f, 0.18f);
    [Export] public Color SkyHorizon { get; set; } = new(0.28f, 0.24f, 0.36f);
    [Export] public Color GroundHorizon { get; set; } = new(0.12f, 0.12f, 0.16f);
    [Export] public Color GroundBottom { get; set; } = new(0.06f, 0.06f, 0.09f);

    [Export] public Color SunColor { get; set; } = new(1f, 1f, 1f);
    [Export] public float SunEnergy { get; set; } = 1.1f;
    [Export] public float SunPitchDegrees { get; set; } = -55f;
    [Export] public float SunYawDegrees { get; set; } = 35f;
    [Export] public float AmbientEnergy { get; set; } = 0.6f;

    /// <summary>Exponential fog density. Zero disables it. Above ~0.012 the far
    /// side of a 60 m arena disappears, which hides incoming threats — fog here
    /// is mood, and must never cost the player information.</summary>
    [Export] public float FogDensity { get; set; }
    [Export] public Color FogColor { get; set; } = new(0.5f, 0.5f, 0.6f);

    // ------------------------------------------------------------ obstacles

    [Export] public ObstacleStyle Style { get; set; } = ObstacleStyle.Crates;

    /// <summary>Decoration without collision.</summary>
    [Export] public int ScatterCount { get; set; } = 90;

    /// <summary>Large obstacles that block bodies. Few on purpose: enemies
    /// steer straight at the player with no pathfinding, so every solid is also
    /// a place a crowd piles up — a tactical tool in small numbers and a
    /// slog in large ones.</summary>
    [Export] public int SolidCount { get; set; }

    // -------------------------------------------------------------- terrain
    // Everything below collides and is walkable. These are the features the
    // movement system cares about: a slide gains speed downhill, and the jump
    // apex is 6 m, so anything under that is a reachable perch.

    /// <summary>Flat-topped hills with slopes on every side.</summary>
    [Export] public int Mounds { get; set; }
    [Export] public float MoundHeight { get; set; } = 2f;

    /// <summary>Raised platforms, each with one ramp. Enemies can only reach the
    /// top by the ramp, so a platform is a place to breathe for a few seconds,
    /// not a place to hide forever.</summary>
    [Export] public int Platforms { get; set; }
    [Export] public float PlatformHeight { get; set; } = 3f;

    /// <summary>Long two-sided ridges: a slope up, a slope down. Purely a
    /// movement feature — the far side is where a slide picks up speed.</summary>
    [Export] public int Ridges { get; set; }
    [Export] public float RidgeHeight { get; set; } = 2.4f;

    // ---------------------------------------------------------------- phase

    /// <summary>Seconds of ordinary waves before the phase boss arrives.</summary>
    [Export] public float PhaseSeconds { get; set; } = 270f;

    /// <summary>How long the boss may live before the director starts raising
    /// the pressure. Not a fail state: a slow kill is punished, never blocked.</summary>
    [Export] public float BossGraceSeconds { get; set; } = 120f;

    /// <summary>Optional boss id. Empty means "let the director choose", which
    /// is the normal case: it takes the strongest boss unlocked by then that the
    /// run has not fought yet, so bosses added to the catalog slot in without
    /// anyone editing this table.</summary>
    [Export] public string BossId { get; set; } = "";

    /// <summary>
    /// Health multiplier on the phase boss, on top of the time curve.
    ///
    /// Below 1 because a phase boss is a gate, not a wanderer. The old timed
    /// boss could be ignored, so its health could be whatever the clock made it;
    /// this one must die before the run can go on, and every weapon aims at the
    /// nearest enemy, so the boss soaks only a share of the player's damage while
    /// a crowd stands closer. At full curve health the first verification run
    /// took the 4:30 boss (6,300 hp) from 100% to 80% in two and a half minutes.
    /// </summary>
    [Export] public float BossHealthScale { get; set; } = 0.55f;

    /// <summary>Multipliers on top of the time-based difficulty curve. Phase one
    /// is exactly 1 so the opening minutes play the way they were tuned; later
    /// phases add a step because arriving somewhere new should feel like a
    /// promotion, while the clock-driven curve handles the slope in between.</summary>
    [Export] public float EnemyHealthScale { get; set; } = 1f;
    [Export] public float EnemyDamageScale { get; set; } = 1f;
    [Export] public float EnemySpeedScale { get; set; } = 1f;
}

/// <summary>
/// The run's sequence of stages. Order is phase order.
/// </summary>
public static class Stages
{
    public static readonly IReadOnlyList<StageDef> Run = new List<StageDef>
    {
        // ---- Phase 1. The original arena, kept recognisable: a circle, dusk
        // violet, crates. The early game was tuned on a flat disc, so the only
        // additions are low mounds near the edge — something to discover, never
        // something in the way during the first minute.
        new StageDef
        {
            Id = "duskfield",
            DisplayName = "Duskfield",
            Tagline = "Low hills. Slide down them.",
            LayoutSeed = 20260815,
            Sides = 64, Radius = 60f, ClearRadius = 14f,
            Ground = new Color(0.16f, 0.18f, 0.22f),
            Grid = new Color(0.24f, 0.27f, 0.33f),
            Wall = new Color(0.35f, 0.30f, 0.45f),
            Terrain = new Color(0.27f, 0.29f, 0.38f),
            ObstacleA = new Color(0.24f, 0.27f, 0.33f),
            ObstacleB = new Color(0.35f, 0.30f, 0.45f),
            SkyTop = new Color(0.10f, 0.11f, 0.18f),
            SkyHorizon = new Color(0.28f, 0.24f, 0.36f),
            GroundHorizon = new Color(0.12f, 0.12f, 0.16f),
            GroundBottom = new Color(0.06f, 0.06f, 0.09f),
            SunColor = new Color(1f, 0.96f, 0.92f), SunEnergy = 1.1f,
            SunPitchDegrees = -55f, SunYawDegrees = 35f, AmbientEnergy = 0.6f,
            FogDensity = 0f,
            Style = ObstacleStyle.Crates, ScatterCount = 80, SolidCount = 0,
            Mounds = 5, MoundHeight = 1.8f,
            Platforms = 0,
            Ridges = 0,
            PhaseSeconds = 270f, BossGraceSeconds = 120f,
        },

        // ---- Phase 2. Bright salt pan under a pale haze: everything the first
        // map was not. Hexagonal, so it has corners to get pinned in, and the
        // first map with platforms — mesas you can jump onto and that a crowd
        // can only climb by the ramp.
        new StageDef
        {
            Id = "saltglass",
            DisplayName = "Saltglass Flats",
            Tagline = "Mesas and corners. Take the high ground.",
            LayoutSeed = 7731,
            Sides = 6, Radius = 68f, ClearRadius = 11f,
            Ground = new Color(0.62f, 0.60f, 0.55f),
            Grid = new Color(0.50f, 0.47f, 0.42f),
            Wall = new Color(0.30f, 0.62f, 0.70f),
            Terrain = new Color(0.78f, 0.70f, 0.58f),
            ObstacleA = new Color(0.86f, 0.86f, 0.84f),
            ObstacleB = new Color(0.66f, 0.74f, 0.76f),
            SkyTop = new Color(0.36f, 0.56f, 0.78f),
            SkyHorizon = new Color(0.86f, 0.84f, 0.78f),
            GroundHorizon = new Color(0.70f, 0.66f, 0.60f),
            GroundBottom = new Color(0.40f, 0.38f, 0.34f),
            SunColor = new Color(1f, 0.95f, 0.84f), SunEnergy = 1.25f,
            SunPitchDegrees = -62f, SunYawDegrees = -40f, AmbientEnergy = 0.75f,
            FogDensity = 0.006f, FogColor = new Color(0.86f, 0.84f, 0.78f),
            Style = ObstacleStyle.Pillars, ScatterCount = 60, SolidCount = 7,
            Mounds = 2, MoundHeight = 2.2f,
            Platforms = 5, PlatformHeight = 3.2f,
            Ridges = 2, RidgeHeight = 2.2f,
            PhaseSeconds = 270f, BossGraceSeconds = 120f,
            EnemyHealthScale = 1.25f, EnemyDamageScale = 1.12f, EnemySpeedScale = 1.04f,
        },

        // ---- Phase 3. Dark basalt and ember light. Octagonal, crowded with
        // ridges: the fastest map to cross if you slide the slopes and the
        // slowest if you walk them, which is the last lesson the movement system
        // has to teach before the final boss.
        new StageDef
        {
            Id = "cinder",
            DisplayName = "Cinder Reach",
            Tagline = "Ridges everywhere. Carry your speed.",
            LayoutSeed = 4409,
            Sides = 8, Radius = 64f, ClearRadius = 11f,
            Ground = new Color(0.13f, 0.09f, 0.08f),
            Grid = new Color(0.55f, 0.20f, 0.08f),
            Wall = new Color(1.00f, 0.38f, 0.12f),
            Terrain = new Color(0.26f, 0.17f, 0.15f),
            ObstacleA = new Color(0.20f, 0.14f, 0.13f),
            ObstacleB = new Color(0.62f, 0.22f, 0.10f),
            SkyTop = new Color(0.06f, 0.03f, 0.04f),
            SkyHorizon = new Color(0.45f, 0.14f, 0.07f),
            GroundHorizon = new Color(0.16f, 0.06f, 0.04f),
            GroundBottom = new Color(0.05f, 0.02f, 0.02f),
            SunColor = new Color(1f, 0.62f, 0.42f), SunEnergy = 1.0f,
            SunPitchDegrees = -38f, SunYawDegrees = 120f, AmbientEnergy = 0.55f,
            FogDensity = 0.008f, FogColor = new Color(0.35f, 0.10f, 0.05f),
            Style = ObstacleStyle.Shards, ScatterCount = 70, SolidCount = 5,
            Mounds = 3, MoundHeight = 2.6f,
            Platforms = 3, PlatformHeight = 3.6f,
            Ridges = 6, RidgeHeight = 2.8f,
            PhaseSeconds = 270f, BossGraceSeconds = 120f,
            EnemyHealthScale = 1.55f, EnemyDamageScale = 1.25f, EnemySpeedScale = 1.08f,
        },
    };
}
