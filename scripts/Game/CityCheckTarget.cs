using Godot;
using Wavebreak.Combat;

namespace Wavebreak.Game;

internal sealed class CityCheckTarget : IDamageable
{
    public Vector3 Position { get; set; }
    public bool IsAlive => true;
    public int Hits { get; private set; }
    public void TakeDamage(DamageInfo info) => Hits++;
}
