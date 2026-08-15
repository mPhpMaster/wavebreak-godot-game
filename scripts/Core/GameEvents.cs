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
