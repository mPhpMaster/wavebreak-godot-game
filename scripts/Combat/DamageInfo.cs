using Godot;

namespace Wavebreak.Combat;

public enum Element { Physical, Fire, Ice, Lightning, Poison }

/// <summary>
/// One hit, as a mutable object that travels the whole pipeline.
///
/// This is the pattern worth copying from the genre: damage is not a float
/// passed down a call chain. It is a record that every stage may inspect and
/// amend — the weapon sets the base, the attacker's stats roll the crit, the
/// victim's armour reduces it, on-hit effects add to it, and the UI reads the
/// final result including *why* it ended up that way.
///
/// Passing a float instead means each of those stages has to be hardcoded in a
/// fixed order at the call site, and adding "shots that cannot crit" or "double
/// damage below half health" turns into a new parameter on every method between
/// here and there.
/// </summary>
public sealed class DamageInfo
{
    public float Amount;
    public Element Element = Element.Physical;

    public bool Crit;
    public bool CanCrit = true;

    public float Knockback;
    public Vector3 Direction = Vector3.Zero;

    /// <summary>Scales on-hit effect chances. A fast weak weapon gets a low
    /// coefficient so it does not proc effects far more often than a slow heavy
    /// one purely by firing more.</summary>
    public float ProcCoefficient = 1f;

    /// <summary>Free-text tag for the source, used by UI and by rules like
    /// "self damage cannot kill you".</summary>
    public string Source = "";

    /// <summary>Set by the victim when the hit is cancelled — dodged, blocked,
    /// or absorbed. Nothing downstream should apply an amount when this is set.</summary>
    public bool Nullified;

    public DamageInfo Clone() => (DamageInfo)MemberwiseClone();
}

public interface IDamageable
{
    /// <summary>Apply a hit. Implementations mutate <paramref name="info"/> to
    /// record what actually happened, so the caller can react to the real result
    /// (lifesteal on damage dealt, not damage attempted).</summary>
    void TakeDamage(DamageInfo info);

    bool IsAlive { get; }
    Vector3 Position { get; }
}
