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

/// <summary>A chest's tier. Standard chests stand in the world for gold; gilded
/// ones cost more and roll higher. Whether a chest is free is a separate flag,
/// because a free chest is still one tier or the other underneath.</summary>
public enum ChestTier { Standard, Gilded }

/// <summary>A chest appeared: placed when a map is populated, topped up during
/// the run, or dropped by an elite or boss (<see cref="Free"/>).</summary>
public readonly struct ChestSpawned
{
    public readonly Vector3 Position;
    public readonly ChestTier Tier;
    public readonly bool Free;
    public ChestSpawned(Vector3 position, ChestTier tier, bool free) { Position = position; Tier = tier; Free = free; }
}

/// <summary>
/// A chest was opened and paid out. <see cref="Cost"/> is the gold actually
/// spent (0 for a free chest); <see cref="Reward"/> is the display name of what
/// it gave, in <see cref="RewardRarity"/> — carried here so a listener can
/// announce it without knowing how rewards are rolled.
/// </summary>
public readonly struct ChestOpened
{
    public readonly Vector3 Position;
    public readonly ChestTier Tier;
    public readonly bool Free;
    public readonly int Cost;
    public readonly string Reward;
    public readonly Data.Rarity RewardRarity;

    public ChestOpened(Vector3 position, ChestTier tier, bool free, int cost, string reward, Data.Rarity rewardRarity)
    {
        Position = position; Tier = tier; Free = free; Cost = cost; Reward = reward; RewardRarity = rewardRarity;
    }
}

/// <summary>The player tried to open a chest they cannot afford. Its own event
/// rather than a flag on <see cref="ChestOpened"/>: nothing happened to the
/// chest, and a listener that counts openings must not have to filter.</summary>
public readonly struct ChestDenied
{
    public readonly Vector3 Position;
    public readonly int Cost;
    public readonly int Gold;
    public ChestDenied(Vector3 position, int cost, int gold) { Position = position; Cost = cost; Gold = gold; }
}
