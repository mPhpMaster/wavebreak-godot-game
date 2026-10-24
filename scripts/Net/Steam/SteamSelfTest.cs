using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace Wavebreak.Net;

/// <summary>
/// <c>--steam-selftest</c>: checks the Steam transport without Steam.
///
/// Part one tests the frame format on its own: round trips, malformed frames,
/// sequence numbers wrapping around.
///
/// Part two runs real Godot multiplayer over <see cref="SteamPeer"/> on a
/// <see cref="FakeSteamHub"/>: a host and clients, each with its own
/// SceneMultiplayer rooted at its own node, in one process. Joining and ids,
/// the server relay (clients see each other, client-to-client RPCs), a large
/// reliable message, a non-member ignored, a full lobby refused, ordering on
/// unreliable-ordered packets, a client leaving, a client going silent
/// (timeout), and the host leaving.
///
/// Prints <c>STEAM SELFTEST PASS checks=N</c> or <c>STEAM SELFTEST FAIL ...</c>
/// and quits. Needs no Steam client, account or network.
/// </summary>
public sealed partial class SteamSelfTest : Node
{
    public static bool Requested
    {
        get
        {
            foreach (string a in OS.GetCmdlineUserArgs()) if (a == "--steam-selftest") return true;
            return false;
        }
    }

    private sealed class Side
    {
        public string Name = "";
        public SceneMultiplayer Mp = null!;
        public SteamPeer Peer = null!;
        public SteamSelfTestProbe Probe = null!;
        public readonly List<long> Joined = new();
        public readonly List<long> Left = new();
        public bool Connected, Failed, ServerGone;
        public long Id => Mp.GetUniqueId();
    }

    private readonly List<string> _failed = new();
    private int _checks;

    private const ulong HostId = 76561190000000001, A = 76561190000000002, B = 76561190000000003,
                        C = 76561190000000004, D = 76561190000000005, Stranger = 76561190000000666;

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        GD.Print("[steam-selftest] start");
        try
        {
            FrameChecks();
            RawPeerChecks();
            await SessionChecks();
        }
        catch (Exception e)
        {
            Check(false, $"exception: {e.GetType().Name}: {e.Message}");
            GD.Print(e.ToString());
        }
        GD.Print(_failed.Count == 0
            ? $"STEAM SELFTEST PASS checks={_checks}"
            : $"STEAM SELFTEST FAIL checks={_checks} failed={_failed.Count} first=\"{_failed[0]}\"");
        GetTree().Quit(_failed.Count == 0 ? 0 : 1);
    }

    private void Check(bool ok, string what)
    {
        _checks++;
        GD.Print($"[steam-selftest] {(ok ? "ok  " : "FAIL")} {what}");
        if (!ok) _failed.Add(what);
    }

    // ------------------------------------------------------------ frames

    private void FrameChecks()
    {
        var payload = new byte[] { 9, 8, 7, 6, 5 };
        var buf = new byte[64];
        int n = SteamFrame.WriteData(buf, 3, MultiplayerPeer.TransferModeEnum.UnreliableOrdered, 0xFFFFFFFE, payload);
        var frame = new byte[n];
        Array.Copy(buf, frame, n);
        bool ok = SteamFrame.TryReadData(frame, out int ch, out var mode, out uint seq, out byte[] back);
        Check(ok && ch == 3 && mode == MultiplayerPeer.TransferModeEnum.UnreliableOrdered && seq == 0xFFFFFFFE
              && back.AsSpan().SequenceEqual(payload), "frame: data round trip keeps channel, mode, sequence, payload");
        Check(n == SteamFrame.DataHeader + payload.Length, "frame: header is 7 bytes");

        Check(SteamFrame.TryReadData(new byte[] { SteamFrame.Data, 0, 2, 0, 0, 0, 0 }, out _, out _, out _, out byte[] empty) && empty.Length == 0,
              "frame: empty payload is valid");
        Check(!SteamFrame.TryReadData(new byte[] { SteamFrame.Data, 0, 2 }, out _, out _, out _, out _), "frame: short frame rejected");
        Check(!SteamFrame.TryReadData(new byte[] { SteamFrame.Data, 0, 9, 0, 0, 0, 0 }, out _, out _, out _, out _), "frame: unknown mode rejected");
        Check(!SteamFrame.TryReadData(new byte[] { SteamFrame.Ping, 0, 2, 0, 0, 0, 0 }, out _, out _, out _, out _), "frame: non-data kind rejected");

        Check(SteamFrame.IsNewer(2, 1) && !SteamFrame.IsNewer(1, 2) && !SteamFrame.IsNewer(5, 5), "sequence: newer / older / same");
        Check(SteamFrame.IsNewer(0, uint.MaxValue) && SteamFrame.IsNewer(3, 0xFFFFFFF0) && !SteamFrame.IsNewer(0xFFFFFFF0, 3),
              "sequence: wraps around");

        var i = new byte[8];
        SteamFrame.WriteInt(i, 2, -123456789);
        Check(SteamFrame.ReadInt(i, 2) == -123456789, "frame: int round trip");
    }

    // ------------------------------------------------------------ raw peers

    /// <summary>Two peers polled by hand, no SceneMultiplayer: the handshake,
    /// and what reaches the reader under reordering.</summary>
    private void RawPeerChecks()
    {
        var hub = new FakeSteamHub();
        SteamPeer host = SteamPeer.CreateHost(hub.Link(1));
        SteamPeer client = SteamPeer.CreateClient(hub.Link(2), 1);
        long joined = 0, clientSaw = 0;
        host.PeerConnected += id => joined = id;
        client.PeerConnected += id => clientSaw = id;

        Check(client.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Connecting && client.GetUniqueId() == 0,
              "raw: client starts connecting with no id");
        client.Poll();   // HELLO
        host.Poll();     // WELCOME
        client.Poll();
        Check(client.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Connected, "raw: client connected after HELLO/WELCOME");
        Check(joined > 1 && joined == client.GetUniqueId(), "raw: host announced the id the client was given");
        Check(clientSaw == 1, "raw: client announced the host as peer 1");
        Check(host.SteamIdOf(joined) == 2 && client.SteamIdOf(1) == 1, "raw: peer id to Steam id both ways");

        // Unreliable-ordered: three packets arrive newest first; only the newest is kept.
        hub.HoldUnreliable = true;
        host.TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered;
        host.TransferChannel = 3;
        host.SetTargetPeer(0);
        foreach (byte b in new byte[] { 1, 2, 3 }) host.PutPacket(new[] { b });
        hub.ReleaseHeld(reversed: true);
        client.Poll();
        bool one = client.GetAvailablePacketCount() == 1;
        int peer = client.GetPacketPeer(), channel = client.GetPacketChannel();
        var m = client.GetPacketMode();
        byte[] got = client.GetPacket();
        Check(one && got.Length == 1 && got[0] == 3, "ordered: stale packets dropped, newest delivered");
        Check(peer == 1 && channel == 3 && m == MultiplayerPeer.TransferModeEnum.UnreliableOrdered, "ordered: packet peer, channel and mode reported");
        Check(client.DroppedStale == 2, "ordered: two stale packets counted");

        // Plain unreliable: no ordering promised, nothing dropped.
        host.TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable;
        foreach (byte b in new byte[] { 4, 5 }) host.PutPacket(new[] { b });
        hub.ReleaseHeld(reversed: true);
        client.Poll();
        Check(client.GetAvailablePacketCount() == 2 && client.GetPacket()[0] == 5 && client.GetPacket()[0] == 4,
              "unreliable: both delivered as they came");
        hub.HoldUnreliable = false;

        // Broadcast excluding a peer, and a target that does not exist.
        host.TransferMode = MultiplayerPeer.TransferModeEnum.Reliable;
        host.SetTargetPeer(-(int)joined);
        host.PutPacket(new byte[] { 7 });
        client.Poll();
        Check(client.GetAvailablePacketCount() == 0, "target: -id skips that peer");
        host.SetTargetPeer(424242);
        Check(host.PutPacket(new byte[] { 8 }) != Error.Ok, "target: unknown peer is an error");

        // A HELLO from another game (wrong magic) is ignored.
        FakeSteamLink other = hub.Link(3);
        var bad = new byte[5];
        bad[0] = SteamFrame.Hello;
        SteamFrame.WriteInt(bad, 1, 0x12345678);
        other.Send(1, bad, 5, true);
        host.Poll();
        Check(host.RemoteCount == 1 && !other.TryReceive(out _, out _), "hello: wrong magic ignored, no answer");

        // The client leaves: the host hears BYE and drops it.
        long left = 0;
        host.PeerDisconnected += id => left = id;
        client.Close();
        host.Poll();
        Check(left == joined && host.RemoteCount == 0, "bye: host drops a client that leaves");
        Check(client.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Disconnected, "bye: closed client is disconnected");
        host.Close();
    }

    // ------------------------------------------------------------ sessions

    private Side MakeSide(string name, SteamPeer peer)
    {
        var root = new Node { Name = name };
        AddChild(root);
        var probe = new SteamSelfTestProbe { Name = "Probe" };
        root.AddChild(probe);
        var mp = new SceneMultiplayer();
        GetTree().SetMultiplayer(mp, root.GetPath());
        var s = new Side { Name = name, Mp = mp, Peer = peer, Probe = probe };
        mp.PeerConnected += id => s.Joined.Add(id);
        mp.PeerDisconnected += id => s.Left.Add(id);
        mp.ConnectedToServer += () => s.Connected = true;
        mp.ConnectionFailed += () => s.Failed = true;
        mp.ServerDisconnected += () => s.ServerGone = true;
        mp.MultiplayerPeer = peer;
        return s;
    }

    private async Task<bool> Until(Func<bool> done, double seconds)
    {
        double end = Time.GetTicksMsec() / 1000.0 + seconds;
        while (!done())
        {
            if (Time.GetTicksMsec() / 1000.0 > end) return false;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        return true;
    }

    private Task Wait(double seconds) => Until(() => false, seconds);

    private async Task SessionChecks()
    {
        var hub = new FakeSteamHub();
        var members = new HashSet<ulong> { A, B, C, D };
        SteamPeer hp = SteamPeer.CreateHost(hub.Link(HostId));
        hp.AllowJoin = members.Contains;
        hp.TimeoutSeconds = 2.5;
        Side host = MakeSide("Host", hp);
        Side a = MakeSide("A", SteamPeer.CreateClient(hub.Link(A), HostId));
        Side b = MakeSide("B", SteamPeer.CreateClient(hub.Link(B), HostId));

        bool up = await Until(() => a.Connected && b.Connected && host.Joined.Count == 2
                                    && a.Joined.Contains(b.Id) && b.Joined.Contains(a.Id), 5);
        Check(up, "session: two clients join and see each other (server relay)");
        Check(a.Id > 1 && b.Id > 1 && a.Id != b.Id, $"session: distinct client ids ({a.Id}, {b.Id})");
        Check(host.Joined.Contains(a.Id) && host.Joined.Contains(b.Id), "session: host saw both peer ids");
        Check(a.Mp.GetPeers().AsSpan().Contains(1), "session: client lists the host");
        Check(hp.SteamIdOf(a.Id) == A && hp.SteamIdOf(b.Id) == B && a.Peer.SteamIdOf(1) == HostId, "session: Steam ids per peer");

        host.Probe.Rpc(SteamSelfTestProbe.MethodName.Ping, "to all");
        a.Probe.RpcId(b.Id, SteamSelfTestProbe.MethodName.Ping, "a to b");
        b.Probe.RpcId(1, SteamSelfTestProbe.MethodName.Ping, "b to host");
        string big = new('w', 200_000);
        host.Probe.RpcId(a.Id, SteamSelfTestProbe.MethodName.Ping, big);
        bool rpcs = await Until(() => a.Probe.Has(1, "to all") && b.Probe.Has(1, "to all") && b.Probe.Has(a.Id, "a to b")
                                      && host.Probe.Has(b.Id, "b to host") && a.Probe.Has(1, big), 3);
        Check(a.Probe.Has(1, "to all") && b.Probe.Has(1, "to all"), "rpc: host broadcast reached both clients");
        Check(b.Probe.Has(a.Id, "a to b"), "rpc: client to client through the host relay, sender kept");
        Check(host.Probe.Has(b.Id, "b to host"), "rpc: client to host");
        Check(a.Probe.Has(1, big), "rpc: 200 KB reliable message intact");
        Check(rpcs, "rpc: all arrived in time");

        for (int k = 1; k <= 5; k++) host.Probe.RpcId(b.Id, SteamSelfTestProbe.MethodName.Stream, k);
        await Until(() => b.Probe.Has(1, "stream 5"), 2);
        Check(b.Probe.Has(1, "stream 5"), "rpc: unreliable-ordered RPCs on channel 2 arrive");

        // Someone outside the lobby: never let in (ignored, not refused).
        SteamPeer sp = SteamPeer.CreateClient(hub.Link(Stranger), HostId);
        sp.ConnectTimeoutSeconds = 2.0;
        Side stranger = MakeSide("Stranger", sp);
        await Until(() => stranger.Failed, 4);
        Check(!stranger.Connected && stranger.Failed && sp.EndReason == "timeout", "lobby: non-member never connects, times out");
        Check(hp.RemoteCount == 2, "lobby: host still has two players");

        // Four is the most: C makes four, D is refused.
        Side c = MakeSide("C", SteamPeer.CreateClient(hub.Link(C), HostId));
        await Until(() => c.Connected, 3);
        Side d = MakeSide("D", SteamPeer.CreateClient(hub.Link(D), HostId));
        await Until(() => d.Failed, 3);
        Check(c.Connected && hp.RemoteCount == 3, "full: the fourth player joins");
        Check(d.Failed && !d.Connected && d.Peer.EndReason == "refused", "full: a fifth is refused");

        // A leaves on purpose: host and B both hear it.
        long aId = a.Id;
        a.Peer.Close();
        await Until(() => host.Left.Contains(aId) && b.Left.Contains(aId), 3);
        Check(host.Left.Contains(aId), "leave: host saw the client go");
        Check(b.Left.Contains(aId), "leave: the other client was told (relay)");

        // C goes silent (crash, cable): dropped after the timeout.
        long cId = c.Id;
        hub.Cut(C);
        bool timedOut = await Until(() => host.Left.Contains(cId), 6);
        Check(timedOut, "timeout: a silent client is dropped");
        Check(!host.Left.Contains(b.Id), "timeout: the live client stays");

        // The host leaves: B is told the server is gone.
        hp.Close();
        await Until(() => b.ServerGone, 3);
        Check(b.ServerGone, "host leaves: client gets server_disconnected");
    }
}
