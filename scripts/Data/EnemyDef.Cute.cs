using Godot;

namespace Wavebreak.Data;

/// <summary>
/// The cute wave's additions to <see cref="EnemyDef"/>: passive traits that
/// combine with any <see cref="EnemyBehavior"/>, read by
/// <c>Actors/Enemy.Cute.cs</c> and <c>Combat/CuteHazards.cs</c>. Every one is
/// zero/empty by default, so the rest of the roster is untouched.
/// </summary>
public partial class EnemyDef
{
    /// <summary>Above 0: links to one ally within this many metres with a
    /// stinging tether drawn on the floor. Walking through it hurts; jumping
    /// over it does not.</summary>
    [Export] public float TetherLength { get; set; }

    /// <summary>Tether sting as a multiple of <see cref="Damage"/>.</summary>
    [Export] public float TetherDamage { get; set; } = 0.6f;

    /// <summary>Above 0: while within this many metres of the player, the view
    /// darkens around the edges. Its own lure shines through the dark.</summary>
    [Export] public float GloomRadius { get; set; }

    /// <summary>Above 0: leaves slime puddles that take this fraction off the
    /// player's move speed while standing in one.</summary>
    [Export] public float TrailSlow { get; set; }

    /// <summary>Bubbler: seconds a bubble holds the player (a dash pops it).
    /// Pincher: seconds the pinch slows the player.</summary>
    [Export] public float HoldSeconds { get; set; } = 1f;

    /// <summary>Every <see cref="BroodEvery"/> seconds, lays
    /// <see cref="BroodCount"/> of <see cref="BroodId"/> beside itself.</summary>
    [Export] public string BroodId { get; set; } = "";
    [Export] public int BroodCount { get; set; }
    [Export] public float BroodEvery { get; set; } = 9f;

    /// <summary>True when anything in the cute layer needs per-frame work.</summary>
    public bool HasCuteTraits =>
        TetherLength > 0f || GloomRadius > 0f || TrailSlow > 0f || BroodCount > 0
        || Behavior is EnemyBehavior.Mimic or EnemyBehavior.Bubbler or EnemyBehavior.Puffer or EnemyBehavior.Pincher;
}
