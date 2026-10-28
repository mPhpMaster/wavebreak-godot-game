using System.Collections.Generic;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Combat;
using Wavebreak.Game;

namespace Wavebreak.Net;

public sealed partial class NetSession
{
    private readonly Dictionary<long, ulong> _coreStrikeAt = new();
    internal int AcceptedCoreStrikes { get; private set; }
    internal void QueueCoreStrike(int enemyId, float amount)
    {
        if (Coop.IsClient) RpcId(1, MethodName.CoreStrike, enemyId, amount);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void CoreStrike(int enemyId, float amount)
    {
        if (!Coop.IsHost || !Arena.IsCity || GetTree().Paused || !float.IsFinite(amount) || amount <= 0f) return;
        long sender = Multiplayer.GetRemoteSenderId();
        if (!_remotes.TryGetValue(sender, out var remote) || !IsInstanceValid(remote) || !remote.IsAlive
            || !remote.CoreStrikeAirborne || remote.CoreStrikeSpeed < 7f
            || !_enemies.TryGetValue(enemyId, out var giant) || !IsInstanceValid(giant) || !giant.IsAlive || !giant.IsCityGiant) return;
        ulong now = Time.GetTicksMsec();
        if (_coreStrikeAt.TryGetValue(sender, out ulong previous) && now - previous < 650) return;
        Vector3 core = giant.ClosestCore(remote.CenterPosition);
        if (remote.CenterPosition.DistanceSquaredTo(core) > 100f || !WorldSight.Clear(remote, remote.CenterPosition, core)) return;
        _coreStrikeAt[sender] = now;
        Enemy.NetHitter = sender;
        try
        {
            giant.TakeDamage(new DamageInfo { Amount = Mathf.Min(amount, (160f + Mathf.Min(remote.CoreStrikeSpeed, 42f) * 14f) * 10f),
                Direction = (core - remote.CenterPosition).Normalized(), CanCrit = false, ProcCoefficient = 0f, Source = "core_strike" });
            HitsApplied++;
            AcceptedCoreStrikes++;
        }
        finally { Enemy.NetHitter = 0; }
    }
}
