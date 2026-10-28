using Godot;
using Wavebreak.Game;

namespace Wavebreak.Net;

public sealed partial class NetSession
{
    public int CaveDiscoveryUpdates { get; private set; }
    public void BroadcastCaveDiscovery(int mask)
    {
        if (Coop.IsHost && Arena.Current.Underwater)
            Rpc(MethodName.CaveDiscoveryState, Arena.Current.Id, mask & 3);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void CaveDiscoveryState(string stage, int mask)
    {
        if (Coop.IsClient && Arena.Current.Underwater && stage == Arena.Current.Id)
        {
            ReefCaveDiscovery.Instance?.Merge(mask);
            CaveDiscoveryUpdates++;
        }
    }

    private void SendCaveDiscovery(long peer)
    {
        if (Arena.Current.Underwater && ReefCaveDiscovery.Instance is { } cave)
            RpcId(peer, MethodName.CaveDiscoveryState, Arena.Current.Id, cave.Mask);
    }
}
