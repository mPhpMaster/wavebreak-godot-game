using System.Collections.Generic;
using Godot;

namespace Wavebreak.Net;

/// <summary>RPC target for <see cref="SteamSelfTest"/>: one per simulated
/// player, at the same path under each player's multiplayer root.</summary>
public sealed partial class SteamSelfTestProbe : Node
{
    public readonly List<(long From, string Text)> Got = new();

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void Ping(string text) => Got.Add((Multiplayer.GetRemoteSenderId(), text));

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = 2)]
    public void Stream(int n) => Got.Add((Multiplayer.GetRemoteSenderId(), $"stream {n}"));

    public bool Has(long from, string text) => Got.Contains((from, text));
}
