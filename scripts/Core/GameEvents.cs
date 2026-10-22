using Godot;
using Wavebreak.Combat;

namespace Wavebreak.Core;

// Every cross-system event in the game. Structs so publishing does not allocate
// in the frame loop, and all in one file so the surface is easy to read whole.

public readonly struct EnemyKilled
{
    public readonly Vector3 Position;
    public readonly int XpValue;
    public readonly int GoldValue;
    public readonly bool WasElite;

    public EnemyKilled(Vector3 position, int xpValue, int goldValue, bool wasElite)
    {
        Position = position; XpValue = xpValue; GoldValue = goldValue; WasElite = wasElite;
    }
}

public readonly struct EnemyDamaged
{
    public readonly IDamageable Target;
    public readonly DamageInfo Info;
    public EnemyDamaged(IDamageable target, DamageInfo info) { Target = target; Info = info; }
}

public readonly struct PlayerDamaged
{
    public readonly DamageInfo Info;
    public readonly float RemainingHealth;
    public PlayerDamaged(DamageInfo info, float remaining) { Info = info; RemainingHealth = remaining; }
}

public readonly struct PlayerHealed
{
    public readonly float Amount;
    public PlayerHealed(float amount) { Amount = amount; }
}

public readonly struct LevelGained
{
    public readonly int Level;
    public LevelGained(int level) { Level = level; }
}

public readonly struct XpGained
{
    public readonly int Amount;
    public readonly int Total;
    public XpGained(int amount, int total) { Amount = amount; Total = total; }
}

public readonly struct GoldGained
{
    public readonly int Amount;
    public readonly int Total;
    public GoldGained(int amount, int total) { Amount = amount; Total = total; }
}

public readonly struct ItemAcquired
{
    public readonly string Id;
    public readonly int NewStackCount;
    public ItemAcquired(string id, int newStackCount) { Id = id; NewStackCount = newStackCount; }
}

public readonly struct WeaponAcquired
{
    public readonly string Id;
    public readonly int NewLevel;
    public WeaponAcquired(string id, int newLevel) { Id = id; NewLevel = newLevel; }
}

public readonly struct RunEnded
{
    public readonly bool Survived;
    public readonly float Seconds;
    public readonly int Level;
    public readonly int Kills;

    public RunEnded(bool survived, float seconds, int level, int kills)
    {
        Survived = survived; Seconds = seconds; Level = level; Kills = kills;
    }
}

/// <summary>Raised when a number should appear over something. Kept as an event
/// so combat never holds a reference to the UI.</summary>
public readonly struct PopupRequested
{
    public readonly Vector3 Position;
    public readonly string Text;
    public readonly Color Color;
    public readonly float Scale;

    public PopupRequested(Vector3 position, string text, Color color, float scale = 1f)
    {
        Position = position; Text = text; Color = color; Scale = scale;
    }
}

/// <summary>A weapon went off. Published once per volley, not per projectile, so
/// a twelve-shot fan is one event; listeners that care about rate (audio) still
/// need their own throttling, because several weapons fire every second.</summary>
public readonly struct WeaponFired
{
    public readonly Vector3 Position;
    public readonly Data.AttackShape Shape;
    public WeaponFired(Vector3 position, Data.AttackShape shape) { Position = position; Shape = shape; }
}

/// <summary>Which movement verb a <see cref="PlayerMoved"/> reports.</summary>
public enum MoveVerb { Jump, AirJump, Dash, Slide, SlamStart, SlamImpact, Land }

/// <summary>
/// The player used a movement verb. One struct for all of them rather than one
/// per verb: every listener so far wants "what happened, how hard, where", and
/// seven near-identical structs would be seven subscriptions to keep in sync.
///
/// <see cref="Strength"/> is 0..1 and verb-specific: impact for
/// <see cref="MoveVerb.Land"/>, crowd size for <see cref="MoveVerb.SlamImpact"/>,
/// 1 otherwise.
/// </summary>
public readonly struct PlayerMoved
{
    public readonly MoveVerb Verb;
    public readonly Vector3 Position;
    public readonly float Strength;
    public PlayerMoved(MoveVerb verb, Vector3 position, float strength = 1f)
    {
        Verb = verb; Position = position; Strength = strength;
    }
}

// ---------------------------------------------------------------- meta
// Appended for the profile / unlocks / secrets layer (scripts/Meta). Nothing in
// the run depends on these; they exist so the meta layer can listen instead of
// being called, and so other systems can feed it without referencing it.

/// <summary>A chest was opened. Published by the chest system; the unlock
/// tracker counts it. If the chest system ships its own event of the same
/// meaning, keep one and point the other side at it.</summary>
public readonly struct ChestOpened
{
    public readonly Vector3 Position;
    public ChestOpened(Vector3 position) { Position = position; }
}

/// <summary>A hidden in-run secret was found. <see cref="Kind"/> is the secret
/// definition's id, so "found every kind" can be counted without a list of the
/// placed instances.</summary>
public readonly struct SecretDiscovered
{
    public readonly string Kind;
    public readonly Vector3 Position;
    public SecretDiscovered(string kind, Vector3 position) { Kind = kind; Position = position; }
}

/// <summary>A persistent unlock was earned this run. Published once, at the
/// moment its condition was met; the toast listens.</summary>
public readonly struct UnlockEarned
{
    public readonly string Id;
    public UnlockEarned(string id) { Id = id; }
}
