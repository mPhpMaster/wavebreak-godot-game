using Godot;
using Wavebreak.Combat;

namespace Wavebreak.Game;

internal sealed class ExpeditionWeaponTarget : IDamageable
{
    public Vector3 Position { get; set; }
    public bool IsAlive => true;
    public int Hits;
    public float Damage;
    public void TakeDamage(DamageInfo hit) { Hits++; Damage += hit.Amount; }
}
