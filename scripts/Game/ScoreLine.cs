namespace Wavebreak.Game;

public sealed class ScoreLine
{
    public long Id;
    public string Name = "";
    public int Slot;
    public int Kills, BossKills, Downs, Deaths, Revives, Level = 1, GoldEarned;
    public float DamageDealt, DamageTaken, HealingRestored, Dps;
}
