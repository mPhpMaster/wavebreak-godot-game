using Godot;
using Wavebreak.Actors;
using Wavebreak.Game;

namespace Wavebreak.Net;

public sealed partial class NetSession
{
    public int CityTellsSent { get; private set; }
    public int CityTellsReceived { get; private set; }

    private void SendGrapple(Player me)
        => Rpc(MethodName.GrappleState, me.LeftHookAttached, me.LeftHookAnchor, me.RightHookAttached, me.RightHookAnchor, me.HookBoosts);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable, TransferChannel = 1)]
    private void GrappleState(bool left, Vector3 leftAnchor, bool right, Vector3 rightAnchor, int boosts)
    {
        long sender = Multiplayer.GetRemoteSenderId();
        if (!_remotes.TryGetValue(sender, out RemotePlayer? remote) || !IsInstanceValid(remote)) return;
        bool active = Arena.IsCity && !remote.Down && remote.HasState;
        bool validLeft = active && left && GrappleAnchors.Valid(remote, remote.CenterPosition, leftAnchor);
        bool validRight = active && right && GrappleAnchors.Valid(remote, remote.CenterPosition, rightAnchor);
        remote.SetGrapple(validLeft, leftAnchor, validRight, rightAnchor, Mathf.Clamp(boosts, 0, 1000000));
    }

    internal void SendCityTell(Enemy giant, Vector3 target, bool aerial, float remaining)
    {
        if (!Coop.IsHost || giant.NetId <= 0) return;
        CityTellsSent++;
        Rpc(MethodName.CityTell, giant.NetId, target, aerial, remaining);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void CityTell(int enemyId, Vector3 target, bool aerial, float remaining)
    {
        if (!Arena.IsCity || !target.IsFinite() || !float.IsFinite(remaining)) return;
        if (_enemies.TryGetValue(enemyId, out Enemy? giant) && IsInstanceValid(giant) && giant.IsAlive)
        {
            CityTellsReceived++;
            giant.ReceiveCityTell(target, aerial, Mathf.Clamp(remaining, 0f, 1.7f));
        }
    }
}
