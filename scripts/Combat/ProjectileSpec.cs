using Godot;

namespace Wavebreak.Combat;

/// <summary>How a spawned projectile behaves once it is in the air.</summary>
public enum ProjectileMode
{
    /// <summary>Flies in a straight line, pierces, expires by range.</summary>
    Straight,

    /// <summary>Steers toward the nearest live target at a bounded turn rate.</summary>
    Homing,

    /// <summary>Detonates on first contact (or at end of life) for area damage.</summary>
    Explosive,

    /// <summary>Pure decoration: no movement, no collision, fades and dies.
    /// Melee swings damage instantly and use one of these to show the arc.</summary>
    Cosmetic,
}

/// <summary>
/// Everything a weapon has to say about one shot.
///
/// Passed as a struct rather than a dozen positional arguments because the five
/// attack shapes fill in overlapping-but-different subsets of it, and a call site
/// with nine floats in a row is where a blast radius quietly ends up in the pierce
/// count.
/// </summary>
public struct ProjectileSpec
{
    public ProjectileMode Mode;
    public Vector3 Origin;
    public Vector3 Direction;

    /// <summary>Metres per second.</summary>
    public float Speed;

    /// <summary>Metres before the shot gives up. Converted to a lifetime.</summary>
    public float Range;

    /// <summary>Radius of the shot's own hitbox.</summary>
    public float HitRadius;

    /// <summary>Area damage radius for <see cref="ProjectileMode.Explosive"/>.</summary>
    public float BlastRadius;

    /// <summary>Extra targets the shot passes through. 0 dies on first hit.</summary>
    public int Pierce;

    public Color Tint;

    /// <summary>Explicit lifetime in seconds. 0 derives it from range and speed.</summary>
    public float Lifetime;
}

