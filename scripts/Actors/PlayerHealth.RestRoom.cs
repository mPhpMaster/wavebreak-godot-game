using Godot;

namespace Wavebreak.Actors;

public sealed partial class PlayerHealth
{
    /// <summary>Set current health directly, bypassing armour, i-frames and
    /// --invulnerable. For self-checks only (--check-restroom wounds the player
    /// so the campfire has something to heal).</summary>
    public void DevSetHealth(float hp)
    {
        if (_dead) return;
        Current = Mathf.Clamp(hp, 1f, Max);
    }
}
