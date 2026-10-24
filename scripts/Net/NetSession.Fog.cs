using Godot;
using Wavebreak.UI;

namespace Wavebreak.Net;

/// <summary>
/// Shared fog of war (UI/FogOfWar). Each machine already reveals round every
/// player it can see, its own and the teammates' puppets; this shares the
/// record so nothing explored is lost to a missed puppet update, a late join
/// or a host migration:
///
///   FogBits (client -> host, reliable)  this player's coarse grid, when it changed, at most every 2 s
///   FogBits (host -> all, reliable)     the team's OR of them, when it changed (every 2 s at most),
///                                       and every 10 s regardless so a newcomer catches up
///
/// One bit per 5 m cell: about 750 bytes on the largest map, so even the
/// worst case is well under 1 KB/s per player. A mask carries the map's token
/// (phase and map id); one for another map is dropped.
/// </summary>
public sealed partial class NetSession
{
    private const float FogSendEvery = 2f;
    private const float FogResendEvery = 10f;
    private float _fogSendIn = 1f;
    private float _fogResendIn = FogResendEvery;
    private bool _fogTeamChanged;

    // Counters for the automated test.
    public int FogMasksSent { get; private set; }
    public int FogMasksReceived { get; private set; }

    /// <summary>Called by FogOfWar every frame of a co-op run.</summary>
    public void FogTick(float dt)
    {
        if (FogOfWar.Instance is not { Active: true } fog || Multiplayer.MultiplayerPeer is null) return;
        StepFogTest();   // --coop-check-fog (NetSession.Test.Fog.cs)
        _fogSendIn -= dt;
        _fogResendIn -= dt;
        if (_fogSendIn > 0f) return;
        _fogSendIn = FogSendEvery;

        if (Coop.IsHost)
        {
            bool due = fog.LocalChanged || _fogTeamChanged || _fogResendIn <= 0f;
            if (!due || Multiplayer.GetPeers().Length == 0) return;
            fog.LocalChanged = false;
            _fogTeamChanged = false;
            _fogResendIn = FogResendEvery;
            Rpc(MethodName.FogBits, fog.Token, fog.Bits);
            FogMasksSent++;
        }
        else if (Coop.IsClient && fog.LocalChanged)
        {
            fog.LocalChanged = false;
            RpcId(1, MethodName.FogBits, fog.Token, fog.Bits);
            FogMasksSent++;
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void FogBits(int token, byte[] bits)
    {
        if (FogOfWar.Instance is not { Active: true } fog || token != fog.Token) return;
        FogMasksReceived++;
        int fresh = fog.Merge(bits);
        // The host passes on what was new to it; a client only merges.
        if (Coop.IsHost && fresh > 0) _fogTeamChanged = true;
    }
}
