using Godot;
using Wavebreak.Actors;
using Wavebreak.Game;

namespace Wavebreak.Net;

/// <summary>
/// Co-op side of the rest room (Game/StageDirector.RestRoom.cs). The host
/// decides: it sends RestEnter when the team takes the boss portal, counts who
/// stands in the CONTINUE portal from the remote avatars it already has, and
/// streams that count to clients for the HUD. Leaving is the ordinary Phase
/// message, so a client that misses the status packets still follows.
/// Each machine shops, heals and revives its own player.
/// </summary>
public sealed partial class NetSession
{
    /// <summary>Client: rest rooms the host took this machine into.</summary>
    public int RestRoomsSeen { get; private set; }

    /// <summary>Client: revives that happened on entering a rest room.</summary>
    public int RestRevives { get; private set; }

    private bool _testDownBeforeRest;   // --coop-down-before-rest: arrive downed (tests the revive)
    private bool _testDownBeforeRestAsked;
    private bool _testExpectRest;       // --coop-expect-restroom
    private string? _testRestShot;      // --coop-rest-shot: a frame inside the rest room
    private float _testRestSeen;

    /// <summary>Test: photograph the rest room once both players have had a
    /// few seconds in it (the run clock is held there, so this counts frames).</summary>
    private void TestRestRoom(float dt)
    {
        if (_testRestShot is null || _main?.Stages is not { InRestRoom: true }) return;
        _testRestSeen += dt;
        if (_testRestSeen < 1.5f) return;
        string path = _testRestShot;
        _testRestShot = null;
        _ = Screenshot.Capture(GetTree(), this, path);
    }

    /// <summary>Host: the team goes into the rest room after phase
    /// <paramref name="clearedIndex"/> + 1.</summary>
    public void SendRestRoom(int clearedIndex)
    {
        if (!_go) return;
        FlushEvents();
        _chests.Clear();
        _claimedChests.Clear();
        Rpc(MethodName.RestEnter, clearedIndex);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RestEnter(int clearedIndex)
    {
        if (!Coop.IsClient || _main is null || !IsInstanceValid(_main)) return;
        _chests.Clear();
        RestRoomsSeen++;
        Player? me = Player.Instance;
        if (_testDownBeforeRest && me is not null && IsInstanceValid(me) && me.IsAlive)
        {
            _testDownBeforeRest = false;
            GD.Print("[coop-test] going down just before the rest room");
            me.Health.Kill();
        }
        bool wasDown = me is not null && IsInstanceValid(me) && !me.IsAlive;
        _main.Stages.NetRestRoom(clearedIndex);
        if (wasDown && me!.IsAlive) RestRevives++;
    }

    /// <summary>Host: how many players are in the CONTINUE portal, for the HUD.</summary>
    public void SendRestStatus(int inside, int total, float countdown)
    {
        if (!_go) return;
        Rpc(MethodName.RestStatus, inside, total, countdown);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable, TransferChannel = 1)]
    private void RestStatus(int inside, int total, float countdown)
    {
        if (Coop.IsClient && _main?.Stages is { InRestRoom: true }) RestRoom.SetStatus(inside, total, countdown);
    }

    /// <summary>Host: seconds until the boss portal pulls everyone through.</summary>
    public void SendPortalTimer(float secondsLeft)
    {
        if (!_go) return;
        Rpc(MethodName.PortalTimer, secondsLeft);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable, TransferChannel = 1)]
    private void PortalTimer(float secondsLeft)
    {
        if (Coop.IsClient) _main?.Stages.NetPortalTimer(secondsLeft);
    }

    /// <summary>Host: remote players standing in <paramref name="portal"/>, and
    /// how many remote players there are. A remote that has not streamed a
    /// position yet still counts towards the total (the timeout covers it).</summary>
    public (int Inside, int Total) CountRemotesInside(Portal portal)
    {
        int inside = 0, total = 0;
        foreach (RemotePlayer r in _remotes.Values)
        {
            if (!IsInstanceValid(r)) continue;
            total++;
            if (r.HasState && !r.Down && portal.Contains(r.GlobalPosition)) inside++;
        }
        return (inside, total);
    }

    /// <summary>Put every remote avatar on its arrival spot around <paramref name="at"/>.</summary>
    public void PlaceRemotes(Vector3 at)
    {
        foreach (RemotePlayer r in _remotes.Values)
            if (IsInstanceValid(r)) r.Place(at + Coop.SlotOffset(r.Slot));
    }
}
