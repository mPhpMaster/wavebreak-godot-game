using System.Collections.Generic;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Game;

namespace Wavebreak.Net;

/// <summary>
/// Co-op Tide Totems (Actors/TideTotem, Game/Interactables.Totems.cs).
/// Shared, unlike the other stations: everyone sees the same totems and a
/// spent one is spent for everyone.
///
/// - The host places them and sends <c>TotemPlaced</c> (id, spot, facing,
///   spent flag) to everyone; a late joiner or a client reconnecting after a
///   host migration gets the whole list after the world snapshot.
/// - E on a client sends <c>TotemAsk</c>; the host grants the first asker
///   (later askers hear "no"), latches every networked orb to that player
///   (the host's collector loop flies and pays them: experience shared, gold
///   to the collector) and sends <c>TotemFired</c> (id, peer) to everyone,
///   which puts the totem to sleep, plays the whoosh and lets the caller's
///   machine pull its own pot drops and show the orbs flying in.
/// </summary>
public sealed partial class NetSession
{
    /// <summary>Totem orbs the host latched to each peer (for the test).</summary>
    internal readonly List<int> TotemNetIds = new();
    internal long TotemLastCaller;
    internal int TotemsSynced, TotemFires;

    /// <summary>Host: a totem was placed (or dev-spawned).</summary>
    public void OnTotemPlaced(TideTotem t)
    {
        if (!Coop.IsHost || Multiplayer.GetPeers().Length == 0) return;
        FlushEvents();
        Rpc(MethodName.TotemPlaced, t.NetId, t.GlobalPosition, t.Rotation.Y, t.Used);
        TotemsSynced++;
    }

    /// <summary>Host: every totem of the map to one peer (late join, resync).</summary>
    private void SendTotems(long peer)
    {
        if (Interactables.Instance is not { } hub) return;
        foreach (TideTotem t in hub.Totems)
            RpcId(peer, MethodName.TotemPlaced, t.NetId, t.GlobalPosition, t.Rotation.Y, t.Used);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void TotemPlaced(int id, Vector3 at, float yaw, bool used)
    {
        if (!Coop.IsClient || Interactables.Instance is not { } hub) return;
        if (hub.AddTotem(new TideTotem(), at, yaw, id, used) is not null) TotemsSynced++;
    }

    public void AskTotem(TideTotem t)
    {
        if (_migrating) { Interactables.Instance?.TotemDenied(t); return; }
        if (Coop.IsHost) ResolveTotem(t.NetId, Coop.LocalId);
        else RpcId(1, MethodName.TotemAsk, t.NetId);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void TotemAsk(int id)
    {
        if (Coop.IsHost) ResolveTotem(id, Multiplayer.GetRemoteSenderId());
    }

    private void ResolveTotem(int id, long requester)
    {
        Interactables? hub = Interactables.Instance;
        TideTotem? t = hub?.TotemById(id);
        if (hub is null || t is null || t.Used)
        {
            if (requester == Coop.LocalId) { if (t is not null) hub?.TotemDenied(t); }
            else RpcId(requester, MethodName.TotemNo, id);
            return;
        }
        TotemLastCaller = requester;
        TotemRemoteCollectsAtFire = RemoteCollects;
        TotemNetIds.Clear();
        hub.FireTotem(t, requester, TotemNetIds);   // latches the orbs to the requester
        TotemFires++;
        FlushEvents();
        Rpc(MethodName.TotemFired, id, requester);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void TotemFired(int id, long peer)
    {
        if (Interactables.Instance is not { } hub || hub.TotemById(id) is not { } t) return;
        TotemLastCaller = peer;
        if (peer == Coop.LocalId) TotemNetIds.Clear();
        hub.FireTotem(t, peer, peer == Coop.LocalId ? TotemNetIds : null);
        TotemFires++;
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void TotemNo(int id)
    {
        if (Interactables.Instance is { } hub && hub.TotemById(id) is { } t) hub.TotemDenied(t);
    }
}
