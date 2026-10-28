using Godot;
using Wavebreak.Actors;
using Wavebreak.Game;

namespace Wavebreak.Net;

public sealed partial class NetSession
{
    public void AskReefHarvest(int id)
    {
        if (_migrating || ReefHarvest.Instance is null) return;
        if (Coop.IsHost) ResolveReefHarvest(id, Coop.LocalId);
        else RpcId(1, MethodName.ReefHarvestAsk, Arena.Current.Id, id);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReefHarvestAsk(string stage, int id)
    {
        if (Coop.IsHost && stage == Arena.Current.Id) ResolveReefHarvest(id, Multiplayer.GetRemoteSenderId());
    }

    private void ResolveReefHarvest(int id, long peer)
    {
        Vector3 at;
        if (peer == Coop.LocalId && Player.Instance is { IsAlive: true } player) at = player.CenterPosition;
        else if (_remotes.TryGetValue(peer, out RemotePlayer? remote) && remote.IsAlive) at = remote.GlobalPosition + Vector3.Up;
        else return;
        if (ReefHarvest.Instance?.TryHarvest(id, at) == true)
            Rpc(MethodName.ReefHarvestSpent, Arena.Current.Id, new[] { id }, true);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReefHarvestSpent(string stage, int[] ids, bool feedback)
    {
        if (!Coop.IsClient || stage != Arena.Current.Id || ids.Length > 128) return;
        foreach (int id in ids) ReefHarvest.Instance?.ApplySpent(id, feedback);
    }

    private void SendReefHarvest(long peer)
    {
        if (ReefHarvest.Instance is { } reef)
            RpcId(peer, MethodName.ReefHarvestSpent, Arena.Current.Id, reef.SpentIds, false);
        if (ReefCheckHelpers.Find<ReefLandmarks>(GetTree().Root) is { CaveRewards: > 0 })
            RpcId(peer, MethodName.ReefCaveClaimed, Arena.Current.Id);
    }

    public void SendReefCaveClaim()
    { if (Coop.IsHost) Rpc(MethodName.ReefCaveClaimed, Arena.Current.Id); }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReefCaveClaimed(string stage)
    {
        if (Coop.IsClient && stage == Arena.Current.Id)
            ReefCheckHelpers.Find<ReefLandmarks>(GetTree().Root)?.ApplyCaveClaim();
    }
}
