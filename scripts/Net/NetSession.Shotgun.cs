using Godot;

namespace Wavebreak.Net;

public sealed partial class NetSession
{
    internal void SendShotgunPose(Vector3 heading)
    {
        if (_go && !Coop.LocalHold && heading.IsFinite()) Rpc(MethodName.ShotgunPose, heading);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    private void ShotgunPose(Vector3 heading)
    {
        if (!_go || !heading.IsFinite() || heading.LengthSquared() < .25f || heading.LengthSquared() > 2) return;
        if (_remotes.TryGetValue(Multiplayer.GetRemoteSenderId(), out RemotePlayer? remote) && !remote.Down)
            remote.ApplyShotgunPose(heading.Normalized());
    }
}
