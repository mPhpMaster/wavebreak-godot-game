using Godot;
using Wavebreak.Actors;
using Wavebreak.Game;
using Wavebreak.Data;

namespace Wavebreak.Net;

public sealed partial class NetSession
{
    public int CityTellsSent { get; private set; }
    public int CityTellsReceived { get; private set; }

    private void SendGrapple(Player me)
        => Rpc(MethodName.GrappleState, me.LeftHookAttached || me.LeftHookFiring, me.LeftHookAnchor, me.RightHookAttached || me.RightHookFiring, me.RightHookAnchor, me.HookBoosts,
            me.HookEnemyId(HookSide.Left), me.HookLocalAnchor(HookSide.Left),
            me.HookEnemyId(HookSide.Right), me.HookLocalAnchor(HookSide.Right),
            me.LeftHookFiring, me.HookTip(HookSide.Left), me.RightHookFiring, me.HookTip(HookSide.Right));

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable, TransferChannel = 1)]
    private void GrappleState(bool left, Vector3 leftAnchor, bool right, Vector3 rightAnchor, int boosts, int leftEnemyId, Vector3 leftLocal, int rightEnemyId, Vector3 rightLocal, bool leftFiring, Vector3 leftTip, bool rightFiring, Vector3 rightTip)
    {
        long sender = Multiplayer.GetRemoteSenderId();
        if (!_remotes.TryGetValue(sender, out RemotePlayer? remote) || !IsInstanceValid(remote)) return;
        bool active = Arena.IsCity && !remote.Down && remote.HasState;
        Enemy? leftBody = HookBody(leftEnemyId, leftLocal), rightBody = HookBody(rightEnemyId, rightLocal);
        if (leftEnemyId != 0) { left &= leftBody is not null; if (leftBody is not null) leftAnchor = leftBody.ToGlobal(leftLocal); }
        if (rightEnemyId != 0) { right &= rightBody is not null; if (rightBody is not null) rightAnchor = rightBody.ToGlobal(rightLocal); }
        bool validLeft = active && left && (leftBody is not null
            ? GrappleAnchors.ValidOnBody(remote, remote.CenterPosition, leftAnchor, leftBody)
            : GrappleAnchors.Valid(remote, remote.CenterPosition, leftAnchor));
        bool validRight = active && right && (rightBody is not null
            ? GrappleAnchors.ValidOnBody(remote, remote.CenterPosition, rightAnchor, rightBody)
            : GrappleAnchors.Valid(remote, remote.CenterPosition, rightAnchor));
        remote.SetGrapple(validLeft && !leftFiring, leftAnchor, validRight && !rightFiring, rightAnchor, Mathf.Clamp(boosts, 0, 1000000), leftBody, leftLocal, rightBody, rightLocal);
        remote.SetHookFlights(validLeft && leftFiring && leftTip.IsFinite() && leftTip.DistanceTo(remote.CenterPosition) < 96,
            leftTip, validRight && rightFiring && rightTip.IsFinite() && rightTip.DistanceTo(remote.CenterPosition) < 96, rightTip);
    }

    private Enemy? HookBody(int enemyId, Vector3 local)
        => enemyId > 0 && local.IsFinite() && local.LengthSquared() < 10000f
            && _enemies.TryGetValue(enemyId, out Enemy? body) && IsInstanceValid(body)
            && body.IsAlive && body.IsCityGiant ? body : null;

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
