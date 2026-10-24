using System;
using System.Collections.Generic;
using Godot;

namespace Wavebreak.Net;

/// <summary>
/// A Godot <see cref="MultiplayerPeer"/> that carries the high-level
/// multiplayer (RPCs, server relay) over Steam instead of ENet. NetSession
/// sets it as <c>Multiplayer.MultiplayerPeer</c> and nothing above it
/// changes: the same RPCs, the same host-authoritative star, peer 1 is the
/// host. Steam itself is behind <see cref="ISteamLink"/>.
///
/// Star topology, like ENet: clients talk only to the host; Godot's server
/// relay forwards client-to-client RPCs, and tells clients about each other.
///
/// Handshake (every frame starts with a kind byte, see <see cref="SteamFrame"/>):
///   client -> host  HELLO      repeated each second until answered
///   host -> client  WELCOME id the client's Godot peer id; both sides now connected
///   host -> client  REFUSE     full, or not taking players
///   either          DATA       one Godot packet: channel, mode, [sequence], payload
///   either          PING       keep-alive once a second when nothing else went
///   either          BYE        leaving on purpose
/// The host accepts a HELLO only from a member of its Steam lobby
/// (<see cref="AllowJoin"/>). A peer silent for <see cref="TimeoutSeconds"/>
/// is dropped: messages are connectionless, so silence is how a crash or a
/// pulled cable shows up.
/// </summary>
public sealed partial class SteamPeer : MultiplayerPeerExtension
{
    private sealed class Remote
    {
        public int Id;
        public ulong SteamId;
        public double LastHeard;
        public double LastSent;
        public readonly Dictionary<int, uint> SeqOut = new();
        public readonly Dictionary<int, uint> SeqIn = new();
    }

    private readonly struct Packet
    {
        public readonly int From;
        public readonly int Channel;
        public readonly TransferModeEnum Mode;
        public readonly byte[] Data;

        public Packet(int from, int channel, TransferModeEnum mode, byte[] data)
        {
            From = from;
            Channel = channel;
            Mode = mode;
            Data = data;
        }
    }

    private ISteamLink? _link;
    private readonly bool _host;
    private readonly ulong _hostSteamId;
    private readonly Dictionary<int, Remote> _byId = new();
    private readonly Dictionary<ulong, Remote> _bySteam = new();
    private readonly Queue<Packet> _incoming = new();
    private readonly byte[] _scratch = new byte[64];
    private byte[] _out = new byte[2048];

    private ConnectionStatus _status;
    private int _uniqueId;
    private int _target;
    private int _channel;
    private TransferModeEnum _mode = TransferModeEnum.Reliable;
    private bool _refusing;
    private double _connectStarted;
    private double _lastHello = -10;

    /// <summary>Host side: may this Steam user join? (lobby members only)</summary>
    public Func<ulong, bool> AllowJoin { get; set; } = _ => true;

    /// <summary>Most players in the session, host included.</summary>
    public int MaxPlayers { get; set; } = Coop.MaxPlayers;

    /// <summary>A connected peer silent this long is dropped.</summary>
    public double TimeoutSeconds { get; set; } = 12.0;

    /// <summary>A client with no WELCOME after this long gives up.</summary>
    public double ConnectTimeoutSeconds { get; set; } = 25.0;

    /// <summary>Wall clock in seconds; replaceable so tests can run fast.</summary>
    public Func<double> Clock { get; set; } = () => Time.GetTicksMsec() / 1000.0;

    /// <summary>Why the last connection ended, for the UI ("refused", "timeout"...).</summary>
    public string EndReason { get; private set; } = "";

    // Statistics for the self-check and the log.
    public int FramesSent { get; private set; }
    public int FramesReceived { get; private set; }
    public int DroppedStale { get; private set; }

    public SteamPeer() { }   // Godot needs a parameterless constructor

    private SteamPeer(ISteamLink link, bool host, ulong hostSteamId)
    {
        _link = link;
        _host = host;
        _hostSteamId = hostSteamId;
    }

    /// <summary>A host: peer id 1, waiting for HELLOs.</summary>
    public static SteamPeer CreateHost(ISteamLink link)
    {
        var p = new SteamPeer(link, true, link.LocalId) { _uniqueId = 1, _status = ConnectionStatus.Connected };
        return p;
    }

    /// <summary>A client of the host with Steam id <paramref name="hostSteamId"/>.
    /// Its peer id is 0 until the host's WELCOME names it.</summary>
    public static SteamPeer CreateClient(ISteamLink link, ulong hostSteamId)
    {
        var p = new SteamPeer(link, false, hostSteamId) { _status = ConnectionStatus.Connecting };
        p._connectStarted = p.Clock();
        return p;
    }

    /// <summary>Steam id behind a Godot peer id (host side, plus id 1 on clients).</summary>
    public ulong SteamIdOf(long peerId)
    {
        if (peerId == _uniqueId && _link is not null) return _link.LocalId;
        if (!_host && peerId == 1) return _hostSteamId;
        return _byId.TryGetValue((int)peerId, out Remote? r) ? r.SteamId : 0;
    }

    public int RemoteCount => _byId.Count;

    // ------------------------------------------------------------ MultiplayerPeer

    public override int _GetAvailablePacketCount() => _incoming.Count;

    public override int _GetMaxPacketSize() => SteamFrame.MaxPayload;

    public override byte[] _GetPacketScript()
    {
        return _incoming.Count > 0 ? _incoming.Dequeue().Data : Array.Empty<byte>();
    }

    public override int _GetPacketChannel() => _incoming.Count > 0 ? _incoming.Peek().Channel : 0;

    public override TransferModeEnum _GetPacketMode() => _incoming.Count > 0 ? _incoming.Peek().Mode : TransferModeEnum.Reliable;

    public override int _GetPacketPeer() => _incoming.Count > 0 ? _incoming.Peek().From : 0;

    public override Error _PutPacketScript(byte[] buffer)
    {
        if (_link is null || _status != ConnectionStatus.Connected) return Error.Unconfigured;
        if (buffer.Length > SteamFrame.MaxPayload) return Error.OutOfMemory;
        if (!_host)
        {
            // A client only ever talks to the host; the server relay does the rest.
            return _byId.TryGetValue(1, out Remote? h) ? SendData(h, buffer) : Error.Unavailable;
        }
        if (_target > 0)
            return _byId.TryGetValue(_target, out Remote? r) ? SendData(r, buffer) : Error.InvalidParameter;
        Error result = Error.Ok;
        foreach (Remote r in _byId.Values)
        {
            if (_target < 0 && r.Id == -_target) continue;
            Error e = SendData(r, buffer);
            if (e != Error.Ok) result = e;
        }
        return result;
    }

    public override void _SetTransferChannel(int channel) => _channel = channel;
    public override int _GetTransferChannel() => _channel;
    public override void _SetTransferMode(TransferModeEnum mode) => _mode = mode;
    public override TransferModeEnum _GetTransferMode() => _mode;
    public override void _SetTargetPeer(int peer) => _target = peer;
    public override bool _IsServer() => _host;
    public override bool _IsServerRelaySupported() => true;
    public override int _GetUniqueId() => _uniqueId;
    public override void _SetRefuseNewConnections(bool enable) => _refusing = enable;
    public override bool _IsRefusingNewConnections() => _refusing;
    public override ConnectionStatus _GetConnectionStatus() => _status;

    public override void _Close()
    {
        if (_link is null) return;
        // Like ENet's close(): say goodbye, forget everyone, emit nothing (the
        // caller is tearing the session down and swaps in another peer).
        foreach (Remote r in _byId.Values) SendControl(r.SteamId, SteamFrame.Bye);
        if (!_host && _status == ConnectionStatus.Connecting) SendControl(_hostSteamId, SteamFrame.Bye);
        _byId.Clear();
        _bySteam.Clear();
        _incoming.Clear();
        _link.Shutdown();
        _link = null;
        _status = ConnectionStatus.Disconnected;
    }

    public override void _DisconnectPeer(int peer, bool force)
    {
        if (!_byId.TryGetValue(peer, out Remote? r)) return;
        SendControl(r.SteamId, SteamFrame.Bye);
        Drop(r, "kicked");
    }

    public override void _Poll()
    {
        if (_link is null) return;
        double now = Clock();

        while (_link.TryTakeFailure(out ulong failed))
        {
            if (_bySteam.TryGetValue(failed, out Remote? r)) Drop(r, "session failed");
            else if (!_host && failed == _hostSteamId && _status == ConnectionStatus.Connecting) GiveUp("unreachable");
            if (_link is null) return;
        }

        while (_link is not null && _link.TryReceive(out ulong from, out byte[] data))
        {
            FramesReceived++;
            if (_host) HostReceive(from, data, now);
            else ClientReceive(from, data, now);
        }
        if (_link is null) return;

        if (!_host && _status == ConnectionStatus.Connecting)
        {
            if (now - _connectStarted > ConnectTimeoutSeconds)
            {
                GiveUp("timeout");
                return;
            }
            if (now - _lastHello >= 1.0)
            {
                _lastHello = now;
                SendControl(_hostSteamId, SteamFrame.Hello, SteamFrame.Magic);
            }
        }

        List<Remote>? silent = null;
        foreach (Remote r in _byId.Values)
        {
            if (now - r.LastHeard > TimeoutSeconds) (silent ??= new()).Add(r);
            else if (now - r.LastSent >= 1.0)
            {
                r.LastSent = now;
                SendControl(r.SteamId, SteamFrame.Ping);
            }
        }
        if (silent is not null)
            foreach (Remote r in silent) Drop(r, "timeout");
    }

    // ------------------------------------------------------------ receive

    private void HostReceive(ulong from, byte[] data, double now)
    {
        if (data.Length == 0) return;
        _bySteam.TryGetValue(from, out Remote? r);
        if (r is not null) r.LastHeard = now;
        switch (data[0])
        {
            case SteamFrame.Hello:
                if (data.Length < 5 || SteamFrame.ReadInt(data, 1) != SteamFrame.Magic) return;   // not our game
                if (r is not null)
                {
                    SendControl(from, SteamFrame.Welcome, r.Id);   // the first WELCOME was lost
                    return;
                }
                if (_refusing || _byId.Count + 1 >= MaxPlayers)
                {
                    SendControl(from, SteamFrame.Refuse);
                    return;
                }
                // Not (yet) in the lobby: ignore, do not refuse. Lobby membership
                // and the first HELLO race; the client asks again in a second.
                if (!AllowJoin(from)) return;
                r = new Remote { Id = NewPeerId(), SteamId = from, LastHeard = now, LastSent = now };
                _byId[r.Id] = r;
                _bySteam[from] = r;
                SendControl(from, SteamFrame.Welcome, r.Id);
                GD.Print($"[steam] {from} joined as peer {r.Id}");
                EmitSignal(MultiplayerPeer.SignalName.PeerConnected, (long)r.Id);
                break;
            case SteamFrame.Data:
                if (r is not null) Enqueue(r, data);
                break;
            case SteamFrame.Bye:
                if (r is not null) Drop(r, "left");
                break;
        }
    }

    private void ClientReceive(ulong from, byte[] data, double now)
    {
        if (data.Length == 0 || from != _hostSteamId) return;   // only the host speaks to a client
        if (_byId.TryGetValue(1, out Remote? h)) h.LastHeard = now;
        switch (data[0])
        {
            case SteamFrame.Welcome:
                if (_status != ConnectionStatus.Connecting || data.Length < 5) return;
                int id = SteamFrame.ReadInt(data, 1);
                if (id < 2) return;
                _uniqueId = id;
                h = new Remote { Id = 1, SteamId = from, LastHeard = now, LastSent = now };
                _byId[1] = h;
                _bySteam[from] = h;
                _status = ConnectionStatus.Connected;
                GD.Print($"[steam] connected to host {from} as peer {id}");
                EmitSignal(MultiplayerPeer.SignalName.PeerConnected, 1L);
                break;
            case SteamFrame.Refuse:
                if (_status == ConnectionStatus.Connecting) GiveUp("refused");
                break;
            case SteamFrame.Data:
                if (h is not null) Enqueue(h, data);
                break;
            case SteamFrame.Bye:
                if (h is not null) Drop(h, "host left");
                else if (_status == ConnectionStatus.Connecting) GiveUp("host left");
                break;
        }
    }

    private void Enqueue(Remote r, byte[] frame)
    {
        if (!SteamFrame.TryReadData(frame, out int channel, out TransferModeEnum mode, out uint seq, out byte[] payload)) return;
        if (mode == TransferModeEnum.UnreliableOrdered)
        {
            // Steam does not order unreliable messages; drop anything older
            // than the newest already delivered on this channel.
            if (r.SeqIn.TryGetValue(channel, out uint last) && !SteamFrame.IsNewer(seq, last))
            {
                DroppedStale++;
                return;
            }
            r.SeqIn[channel] = seq;
        }
        _incoming.Enqueue(new Packet(r.Id, channel, mode, payload));
    }

    // ------------------------------------------------------------ send

    private Error SendData(Remote r, byte[] payload)
    {
        uint seq = 0;
        if (_mode == TransferModeEnum.UnreliableOrdered)
        {
            r.SeqOut.TryGetValue(_channel, out seq);
            seq++;
            r.SeqOut[_channel] = seq;
        }
        int needed = SteamFrame.DataHeader + payload.Length;
        if (_out.Length < needed) _out = new byte[Math.Max(needed, _out.Length * 2)];
        int n = SteamFrame.WriteData(_out, _channel, _mode, seq, payload);
        r.LastSent = Clock();
        FramesSent++;
        return _link!.Send(r.SteamId, _out, n, _mode == TransferModeEnum.Reliable) ? Error.Ok : Error.ConnectionError;
    }

    private void SendControl(ulong to, byte kind, int? value = null)
    {
        if (_link is null) return;
        _scratch[0] = kind;
        int n = 1;
        if (value is { } v)
        {
            SteamFrame.WriteInt(_scratch, 1, v);
            n = 5;
        }
        FramesSent++;
        _link.Send(to, _scratch, n, kind != SteamFrame.Ping);
    }

    // ------------------------------------------------------------ bookkeeping

    private void Drop(Remote r, string why)
    {
        if (!_byId.Remove(r.Id)) return;
        _bySteam.Remove(r.SteamId);
        GD.Print($"[steam] peer {r.Id} ({r.SteamId}) gone: {why}");
        if (!_host)
        {
            // The host is gone: the whole connection is over.
            EndReason = why;
            _status = ConnectionStatus.Disconnected;
            _incoming.Clear();
            _link?.Shutdown();
            _link = null;
        }
        EmitSignal(MultiplayerPeer.SignalName.PeerDisconnected, (long)r.Id);
    }

    private void GiveUp(string why)
    {
        GD.Print($"[steam] could not connect: {why}");
        EndReason = why;
        _status = ConnectionStatus.Disconnected;
        _link?.Shutdown();
        _link = null;
    }

    private int NewPeerId()
    {
        // Godot peer ids are positive 32-bit ints; 1 is the host.
        while (true)
        {
            int id = (int)(GD.Randi() & 0x7FFFFFFF);
            if (id > 1 && !_byId.ContainsKey(id)) return id;
        }
    }
}

/// <summary>
/// Wire format of <see cref="SteamPeer"/> frames. Little-endian.
///   HELLO    [1][magic:4]
///   WELCOME  [2][peer id:4]
///   REFUSE   [3]
///   DATA     [4][channel:1][mode:1][sequence:4][payload...]
///   PING     [5]
///   BYE      [6]
/// The sequence is only meaningful for unreliable-ordered packets (zero
/// otherwise); it is always present so the header has one size.
/// </summary>
public static class SteamFrame
{
    public const byte Hello = 1, Welcome = 2, Refuse = 3, Data = 4, Ping = 5, Bye = 6;

    /// <summary>"WBK1": a HELLO from anything else is ignored.</summary>
    public const int Magic = 0x314B4257;

    public const int DataHeader = 7;

    /// <summary>Steam's per-message limit (512 KB) less the header.</summary>
    public const int MaxPayload = 512 * 1024 - DataHeader;

    public static int WriteData(byte[] dst, int channel, MultiplayerPeer.TransferModeEnum mode, uint seq, byte[] payload)
    {
        dst[0] = Data;
        dst[1] = (byte)Math.Clamp(channel, 0, 255);
        dst[2] = (byte)mode;
        WriteInt(dst, 3, unchecked((int)seq));
        Array.Copy(payload, 0, dst, DataHeader, payload.Length);
        return DataHeader + payload.Length;
    }

    public static bool TryReadData(byte[] frame, out int channel, out MultiplayerPeer.TransferModeEnum mode, out uint seq, out byte[] payload)
    {
        channel = 0;
        mode = MultiplayerPeer.TransferModeEnum.Reliable;
        seq = 0;
        payload = Array.Empty<byte>();
        if (frame.Length < DataHeader || frame[0] != Data || frame[2] > 2) return false;
        channel = frame[1];
        mode = (MultiplayerPeer.TransferModeEnum)frame[2];
        seq = unchecked((uint)ReadInt(frame, 3));
        payload = new byte[frame.Length - DataHeader];
        Array.Copy(frame, DataHeader, payload, 0, payload.Length);
        return true;
    }

    /// <summary>True when <paramref name="seq"/> comes after <paramref name="last"/>,
    /// allowing for the counter wrapping around.</summary>
    public static bool IsNewer(uint seq, uint last) => unchecked((int)(seq - last)) > 0;

    public static void WriteInt(byte[] dst, int at, int v)
    {
        dst[at] = (byte)v;
        dst[at + 1] = (byte)(v >> 8);
        dst[at + 2] = (byte)(v >> 16);
        dst[at + 3] = (byte)(v >> 24);
    }

    public static int ReadInt(byte[] src, int at) => src[at] | src[at + 1] << 8 | src[at + 2] << 16 | src[at + 3] << 24;
}
