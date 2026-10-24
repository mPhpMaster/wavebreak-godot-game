using Godot;

namespace Wavebreak.Net;

/// <summary>
/// Pings through the host: a client asks (<c>PingAsk</c>), the host checks
/// that client's rate limit and sends the ping to everyone, itself included
/// (<c>PingShow</c>). The host's own pings take the same check and the same
/// broadcast. Single-player never gets here (<see cref="Pings.Show"/> directly).
/// </summary>
public sealed partial class NetSession
{
    public int PingsFromClients { get; private set; }

    /// <summary>This player placed a ping (already within their own limit).</summary>
    public void SendPing(PingKind kind, Vector3 at)
    {
        if (Coop.IsHost) HostPing(Coop.LocalId, kind, at);
        else RpcId(1, MethodName.PingAsk, (int)kind, at);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void PingAsk(int kind, Vector3 at)
    {
        if (!Coop.IsHost) return;
        HostPing(Multiplayer.GetRemoteSenderId(), (PingKind)Mathf.Clamp(kind, 0, (int)PingKind.Help), at);
    }

    private void HostPing(long peer, PingKind kind, Vector3 at)
    {
        // The host's word on a client's rate limit (its own was checked when pressed).
        if (peer != Coop.LocalId && !Pings.Allow(peer)) return;
        if (peer != Coop.LocalId) PingsFromClients++;
        FlushEvents();
        Rpc(MethodName.PingShow, peer, (int)kind, at);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void PingShow(long peer, int kind, Vector3 at)
    {
        Pings.Show(peer, (PingKind)kind, at);
    }
}
