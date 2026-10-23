using System.Collections.Generic;
using Godot;

namespace Wavebreak.Data;

/// <summary>
/// One map, and the phase of the run that is played on it.
///
/// A stage is data for the same reason weapons and enemies are: adding a fourth
/// map, or re-tuning how long the second phase lasts, should be an edit to a
/// table and not a new class. <see cref="Game.Arena"/> reads every field here
/// and nothing else, so two stages that differ only in numbers produce two
/// genuinely different places.
///
/// The look — floor, sky, fog, sun, trees and rocks — comes from a
/// <see cref="Visuals.PropTheme"/> named by <see cref="Theme"/>; this table owns
/// what the theme does not: the shape and size of the ground, the terrain that
/// matters to movement, the landmarks, and the phase numbers.
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
    /// is a place the player can learn, not a fresh scramble every time. (Chests
    /// and shrines are seeded per run, so what is worth finding still moves.)</summary>
    [Export] public int LayoutSeed { get; set; } = 1;

    // ---------------------------------------------------------------- shape

    /// <summary>Sides of the floor polygon. 48 and up reads as a circle; 6 or 8
    /// give corners, which changes where the player can get trapped.</summary>
    [Export] public int Sides { get; set; } = 64;

    /// <summary>
    /// Circumradius in metres — the distance to a corner. The usable radius is
    /// smaller on low-sided shapes; see <see cref="InRadius"/>.
    ///
    /// Big on purpose (2–3x the original 60 m disc). Enemies spawn on a ring
    /// around the <em>player</em>, not the map, so a larger map does not thin
    /// the fight; what it adds is somewhere to go — landmarks, chests and
    /// shrines far enough apart that reaching one is a decision.
    /// </summary>
    [Export] public float Radius { get; set; } = 130f;

    /// <summary>Distance from the centre to the middle of a wall: the radius a
    /// circle drawn inside the floor can have. This, not <see cref="Radius"/>,
    /// is what clamps and steering should trust.</summary>
    public float InRadius => Radius * Mathf.Cos(Mathf.Pi / Mathf.Max(3, Sides));

    /// <summary>Kept free of anything solid. The portal opens and the player
    /// arrives here, so it must always be reachable and never inside a mound.</summary>
    [Export] public float ClearRadius { get; set; } = 12f;

    // ---------------------------------------------------------------- look

    /// <summary><see cref="Visuals.PropTheme"/> id: floor shader, sky, fog,
    /// sun, and the props scattered over the map.</summary>
    [Export] public string Theme { get; set; } = "meadow";

    /// <summary>Theme prop density. Below 1 keeps lanes open: the scatter is
    /// visual (no collision), but a field too thick to see enemies through is
    /// still a field you cannot kite in.</summary>
    [Export] public float PropDensity { get; set; } = 0.7f;

    /// <summary>Boundary wall colour. Emissive, so the edge reads through fog.</summary>
    [Export] public Color Wall { get; set; } = new(0.35f, 0.30f, 0.45f);

    /// <summary>Colour of built structures — platforms and their ramps. Kept
    /// distinct from the floor so a ramp is visible as a ramp from a
    /// third-person camera, where shading alone is too subtle.</summary>
    [Export] public Color Terrain { get; set; } = new(0.30f, 0.32f, 0.42f);

    // -------------------------------------------------------------- terrain
    // Everything below collides. These are the features the movement system
    // cares about: a slide gains speed downhill, and the jump apex is 6 m, so
    // anything under that is a reachable perch.

    /// <summary>Flat-topped hills with slopes on every side.</summary>
    [Export] public int Mounds { get; set; }
    [Export] public float MoundHeight { get; set; } = 2f;

    /// <summary>Raised platforms, each with one ramp and a pair of pillars.
    /// Enemies can only reach the top by the ramp, so a platform is a place to
    /// breathe for a few seconds, not a place to hide forever.</summary>
    [Export] public int Platforms { get; set; }
    [Export] public float PlatformHeight { get; set; } = 3f;

    /// <summary>Long two-sided ridges: a slope up, a slope down. Purely a
    /// movement feature — the far side is where a slide picks up speed.</summary>
    [Export] public int Ridges { get; set; }
    [Export] public float RidgeHeight { get; set; } = 2.4f;

    /// <summary>Large theme props (rock formations, pillars) with colliders.
    /// Few relative to the area on purpose: enemies steer straight at the player
    /// with no pathfinding, so every solid is also a place a crowd piles up — a
    /// tactical tool in small numbers and a slog in large ones.</summary>
    [Export] public int Landmarks { get; set; }

    /// <summary>World chests placed when the map is built.</summary>
    [Export] public int Chests { get; set; } = 5;

    /// <summary>Shady merchants: a stall selling three items for gold. One per
    /// map is the design; zero turns the shop off for a map.</summary>
    [Export] public int Merchants { get; set; } = 1;

    /// <summary>Microwaves: pay once to duplicate one held item (+1 stack).
    /// Each one works once.</summary>
    [Export] public int Microwaves { get; set; } = 1;

    /// <summary>Breakable pots, vases and crates scattered in small clusters.
    /// Weapons and slams break them for a little gold, sometimes xp, rarely a
    /// heal. See <see cref="Game.Breakables"/>.</summary>
    [Export] public int Breakables { get; set; } = 30;
    /// <summary>Map shrines placed when the map is built (see
    /// <see cref="Game.ShrineSpawner"/>): charge rings, timed challenges, gold
    /// statues and greed altars. The hidden secret shrines are separate.</summary>
    [Export] public int ChargeShrines { get; set; } = 3;
    [Export] public int ChallengeShrines { get; set; } = 1;
    [Export] public int StatueShrines { get; set; } = 1;
    [Export] public int GreedShrines { get; set; } = 1;

    // ---------------------------------------------------------------- phase

    /// <summary>Seconds of ordinary waves before the phase boss arrives.</summary>
    [Export] public float PhaseSeconds { get; set; } = 210f;

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
///
/// Timing: three phases of 3:30 of waves plus a boss fight of roughly two
/// minutes each lands a won run near the 15 minutes the rest of the game was
/// paced for (<see cref="Game.RunState.TargetSeconds"/>). The first verification
/// run used 4:30 phases and finished its third boss after 21 minutes.
/// </summary>
public static class Stages
{
    public static readonly IReadOnlyList<StageDef> Run = new List<StageDef>
    {
        // ---- Phase 1. Open green meadow, round, gentle: low mounds to learn
        // the slide on and a few platforms. The early game was tuned on a flat
        // disc, so nothing solid stands near the start.
        new StageDef
        {
            Id = "thornmeadow",
            DisplayName = "Thornmeadow",
            Tagline = "Low hills. Slide down them.",
            LayoutSeed = 20260815,
            Sides = 64, Radius = 130f, ClearRadius = 16f,
            Theme = "meadow", PropDensity = 0.6f,
            Wall = new Color(0.55f, 0.48f, 0.32f),
            Terrain = new Color(0.58f, 0.52f, 0.40f),
            Mounds = 16, MoundHeight = 2.0f,
            Platforms = 5, PlatformHeight = 3.0f,
            Ridges = 4, RidgeHeight = 2.2f,
            Landmarks = 10,
            Chests = 6,
            Merchants = 1, Microwaves = 1, Breakables = 28,
            ChargeShrines = 3, ChallengeShrines = 1, StatueShrines = 1, GreedShrines = 1,
            PhaseSeconds = 210f, BossGraceSeconds = 120f,
        },

        // ---- Phase 2. Frozen hexagonal plateau under a pale haze: corners to
        // get pinned in, and mesas everywhere — the map where the high ground
        // is the strategy.
        new StageDef
        {
            Id = "rimeglass",
            DisplayName = "Rimeglass Plateau",
            Tagline = "Mesas and corners. Take the high ground.",
            LayoutSeed = 7731,
            Sides = 6, Radius = 165f, ClearRadius = 13f,
            Theme = "frost", PropDensity = 0.7f,
            Wall = new Color(0.30f, 0.62f, 0.80f),
            Terrain = new Color(0.62f, 0.70f, 0.80f),
            Mounds = 8, MoundHeight = 2.4f,
            Platforms = 14, PlatformHeight = 3.4f,
            Ridges = 6, RidgeHeight = 2.4f,
            Landmarks = 14,
            Chests = 7,
            Merchants = 1, Microwaves = 1, Breakables = 36,
            ChargeShrines = 4, ChallengeShrines = 2, StatueShrines = 1, GreedShrines = 1,
            PhaseSeconds = 210f, BossGraceSeconds = 120f,
            EnemyHealthScale = 1.25f, EnemyDamageScale = 1.12f, EnemySpeedScale = 1.04f,
        },

        // ---- Phase 3. Ember-cracked basalt, octagonal, crowded with ridges:
        // the fastest map to cross if you slide the slopes and the slowest if
        // you walk them, which is the last lesson the movement system has to
        // teach before the final boss.
        new StageDef
        {
            Id = "cinder",
            DisplayName = "Cinder Reach",
            Tagline = "Ridges everywhere. Carry your speed.",
            LayoutSeed = 4409,
            Sides = 8, Radius = 150f, ClearRadius = 13f,
            Theme = "ashlands", PropDensity = 0.75f,
            Wall = new Color(1.00f, 0.38f, 0.12f),
            Terrain = new Color(0.34f, 0.22f, 0.19f),
            Mounds = 8, MoundHeight = 2.8f,
            Platforms = 7, PlatformHeight = 3.8f,
            Ridges = 18, RidgeHeight = 2.8f,
            Landmarks = 12,
            Chests = 7,
            Merchants = 1, Microwaves = 1, Breakables = 32,
            ChargeShrines = 4, ChallengeShrines = 2, StatueShrines = 2, GreedShrines = 1,
            PhaseSeconds = 210f, BossGraceSeconds = 120f,
            // The last boss meets the steepest part of the clock curve; at 0.55
            // the first full run's final boss had 82,000 hp.
            BossHealthScale = 0.45f,
            EnemyHealthScale = 1.55f, EnemyDamageScale = 1.25f, EnemySpeedScale = 1.08f,
        },
    };
}
