using System.Collections.Generic;
using System.Text;
using Godot;
using Wavebreak.Meta;

namespace Wavebreak.Net;

/// <summary>
/// The version handshake, before any RPC. Godot numbers a node's RPCs by their
/// sorted names, so two builds with different RPC sets call each other's wrong
/// methods: a check inside an RPC cannot be trusted to arrive. These messages go
/// as raw bytes (<see cref="SceneMultiplayer.SendBytes"/>), which no build
/// difference can shift.
///
///   client -> host   hello | version | content hash
///   host -> client   ok, or refuse | version | content hash
///
/// Only after "ok" does the client say Hello, and the host ignores a Hello from
/// a peer that has not passed. A refusal carries the host's facts rather than a
/// sentence, so the client writes it in its own language
/// (<see cref="CoopBuild.MismatchText"/>). A host too old to answer leaves the
/// client to its timeout.
/// </summary>
public sealed partial class NetSession
{
    private const double HandshakeSeconds = 15.0;
    private const string HsHello = "wb-hello", HsOk = "wb-ok", HsRefuse = "wb-refuse";

    private readonly HashSet<long> _verified = new();   // host: peers past the handshake
    private double _handshakeDeadline = -1;             // client: waiting for the host's answer
    private int _helloPrevSlot = -1;

    private void SendRaw(long id, params string[] fields)
        => ((SceneMultiplayer)Multiplayer).SendBytes(Encoding.UTF8.GetBytes(string.Join('|', fields)), (int)id);

    /// <summary>Client: connected; introduce the build first.</summary>
    private void BeginHandshake(int prevSlot)
    {
        _helloPrevSlot = prevSlot;
        _handshakeDeadline = Now + HandshakeSeconds;
        SendRaw(1, HsHello, CoopBuild.Version, CoopBuild.Content);
    }

    private void OnPeerPacket(long id, byte[] packet)
    {
        string[] f = Encoding.UTF8.GetString(packet).Split('|');
        if (Coop.IsHost && f[0] == HsHello && f.Length >= 3) HostHandshake(id, f[1], f[2]);
        else if (!Coop.IsClient || id != 1 || _handshakeDeadline < 0) return;
        else if (f[0] == HsOk)
        {
            _handshakeDeadline = -1;
            SendHello();
        }
        else if (f[0] == HsRefuse && f.Length >= 3)
        {
            _handshakeDeadline = -1;
            string message = CoopBuild.MismatchText(f[1], f[2]);
            if (_migrating) Fallback(message);
            else Fail(message);
        }
    }

    private void HostHandshake(long id, string version, string content)
    {
        if (!CoopBuild.Matches(version, content))
        {
            GD.Print($"[coop] refused peer {id}: version {version} #{content}, this host {CoopBuild.Version} #{CoopBuild.Content}");
            SendRaw(id, HsRefuse, CoopBuild.Version, CoopBuild.Content);
            DropPeerSoon(id);
            TestBuildRefused();
            return;
        }
        _verified.Add(id);
        SendRaw(id, HsOk);
    }

    /// <summary>Host: a connection that never completes the handshake (an old
    /// build, a stray socket) does not keep its socket.</summary>
    private void WatchHandshake(long id)
    {
        GetTree().CreateTimer(HandshakeSeconds + 5.0, processAlways: true).Timeout += () =>
        {
            if (!IsInstanceValid(this) || !IsInsideTree() || !Coop.IsHost || _verified.Contains(id)) return;
            if (System.Array.IndexOf(Multiplayer.GetPeers(), (int)id) < 0) return;
            GD.Print($"[coop] peer {id} never completed the handshake");
            _peer?.DisconnectPeer((int)id);
        };
    }

    private void StepHandshake()
    {
        if (_handshakeDeadline < 0 || Now < _handshakeDeadline) return;
        _handshakeDeadline = -1;
        if (_migrating) RetryReconnect();
        else Fail(Loc.T("Different game version."));
    }
}
