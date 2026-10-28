using System.Collections.Generic;
using Godot;
using Wavebreak.Core;
using Wavebreak.Net;

namespace Wavebreak.Game;

public static partial class RunScoreboard
{
    private static readonly Dictionary<long, DamageWindow> DamageMeters = new();
    private static float _localHealing;
    private static double MeterTime => Time.GetTicksMsec() / 1000.0;
    private static void BeginMeters()
    {
        DamageMeters.Clear(); _localHealing = 0;
        EventBus.Unsubscribe<PlayerHealed>(OnHealed); EventBus.Subscribe<PlayerHealed>(OnHealed);
        EventBus.Unsubscribe<RunEnded>(OnRunEnded); EventBus.Subscribe<RunEnded>(OnRunEnded);
    }
    private static void OnHealed(PlayerHealed heal)
    {
        if (float.IsFinite(heal.Amount) && heal.Amount > 0) _localHealing += heal.Amount;
    }
    private static void OnRunEnded(RunEnded ended)
    {
        if (!Authority || ended.Survived) return;
        foreach (ScoreLine line in Lines.Values)
        {
            bool dead = line.Id == Coop.LocalId ? Actors.Player.Instance is { IsAlive: false }
                : NetSession.Instance?.Remotes.TryGetValue(line.Id, out RemotePlayer? remote) == true && remote.Down;
            if (dead) line.Deaths++;
        }
        Version++;
    }
    private static void MeterHit(long id, float amount)
    {
        if (!DamageMeters.TryGetValue(id, out DamageWindow? meter)) DamageMeters[id] = meter = new DamageWindow(MeterTime);
        meter.Add(MeterTime, amount);
    }
    public static void RefreshMeters()
    {
        if (!Authority) return;
        foreach (ScoreLine line in Lines.Values)
            line.Dps = DamageMeters.TryGetValue(line.Id, out DamageWindow? meter) ? meter.Rate(MeterTime) : 0;
    }
    public static ScoreLine? Find(long id) => Lines.TryGetValue(id, out ScoreLine? line) ? line : null;
}
