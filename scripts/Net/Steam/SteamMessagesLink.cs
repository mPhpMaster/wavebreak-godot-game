using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Godot;
using Steamworks;

namespace Wavebreak.Net;

/// <summary>
/// <see cref="ISteamLink"/> over ISteamNetworkingMessages: connectionless,
/// addressed by Steam id, routed through Valve's relay network, so nobody
/// forwards ports or types an address. Sessions open on the first message;
/// this side accepts every session request and leaves "who may play" to
/// <see cref="SteamPeer"/> (lobby members only).
///
/// Everything goes on Steam channel 0. Godot's own channels ride inside the
/// frame (see <see cref="SteamPeer"/>), so reliable traffic stays in one
/// ordered stream, as it does over ENet.
///
/// Not ISteamNetworkingSockets: its P2P calls take a
/// SteamNetworkingConfigValue_t array, and that struct does not load under
/// .NET (CoreCLR rejects its overlapped union), so every call that mentions
/// it throws. The messages interface never touches it.
/// </summary>
public sealed class SteamMessagesLink : ISteamLink
{
    private const int Channel = 0;
    private const int MaxBatch = 64;

    private readonly IntPtr[] _batch = new IntPtr[MaxBatch];
    private readonly Queue<(ulong From, byte[] Data)> _inbox = new();
    private readonly Queue<ulong> _failures = new();
    private IntPtr _sendBuffer = IntPtr.Zero;
    private int _sendCapacity;
    private bool _closed;

    // Held so the garbage collector keeps the registrations alive.
    private Callback<SteamNetworkingMessagesSessionRequest_t>? _onRequest;
    private Callback<SteamNetworkingMessagesSessionFailed_t>? _onFailed;

    public SteamMessagesLink()
    {
        LocalId = SteamUser.GetSteamID().m_SteamID;
        _onRequest = Callback<SteamNetworkingMessagesSessionRequest_t>.Create(OnSessionRequest);
        _onFailed = Callback<SteamNetworkingMessagesSessionFailed_t>.Create(OnSessionFailed);
    }

    public ulong LocalId { get; }

    private void OnSessionRequest(SteamNetworkingMessagesSessionRequest_t e)
    {
        if (_closed) return;
        SteamNetworkingIdentity who = e.m_identityRemote;
        SteamNetworkingMessages.AcceptSessionWithUser(ref who);
    }

    private void OnSessionFailed(SteamNetworkingMessagesSessionFailed_t e)
    {
        if (_closed) return;
        ulong user = e.m_info.m_identityRemote.GetSteamID64();
        GD.Print($"[steam] session with {user} failed (reason {e.m_info.m_eEndReason})");
        _failures.Enqueue(user);
    }

    public bool Send(ulong to, byte[] data, int length, bool reliable)
    {
        if (_closed || length <= 0) return false;
        if (length > _sendCapacity)
        {
            if (_sendBuffer != IntPtr.Zero) Marshal.FreeHGlobal(_sendBuffer);
            _sendCapacity = Math.Max(length, 2048);
            _sendBuffer = Marshal.AllocHGlobal(_sendCapacity);
        }
        Marshal.Copy(data, 0, _sendBuffer, length);
        var id = new SteamNetworkingIdentity();
        id.SetSteamID64(to);
        int flags = (reliable ? Constants.k_nSteamNetworkingSend_Reliable : Constants.k_nSteamNetworkingSend_UnreliableNoNagle)
                    | Constants.k_nSteamNetworkingSend_AutoRestartBrokenSession;
        EResult r = SteamNetworkingMessages.SendMessageToUser(ref id, _sendBuffer, (uint)length, flags, Channel);
        return r == EResult.k_EResultOK;
    }

    public bool TryReceive(out ulong from, out byte[] data)
    {
        if (_inbox.Count == 0 && !_closed) Fill();
        if (_inbox.Count > 0)
        {
            (from, data) = _inbox.Dequeue();
            return true;
        }
        from = 0;
        data = Array.Empty<byte>();
        return false;
    }

    private void Fill()
    {
        int n = SteamNetworkingMessages.ReceiveMessagesOnChannel(Channel, _batch, MaxBatch);
        for (int i = 0; i < n; i++)
        {
            IntPtr p = _batch[i];
            try
            {
                SteamNetworkingMessage_t m = SteamNetworkingMessage_t.FromIntPtr(p);
                var bytes = new byte[m.m_cbSize];
                if (m.m_cbSize > 0) Marshal.Copy(m.m_pData, bytes, 0, m.m_cbSize);
                _inbox.Enqueue((m.m_identityPeer.GetSteamID64(), bytes));
            }
            finally
            {
                SteamNetworkingMessage_t.Release(p);
            }
        }
    }

    public bool TryTakeFailure(out ulong user) => _failures.TryDequeue(out user);

    public void Shutdown()
    {
        // Sessions are not closed here: closing drops whatever is still queued,
        // including the goodbye just sent. Idle sessions expire on their own.
        _closed = true;
        _onRequest?.Dispose();
        _onFailed?.Dispose();
        _onRequest = null;
        _onFailed = null;
        _inbox.Clear();
        if (_sendBuffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_sendBuffer);
            _sendBuffer = IntPtr.Zero;
            _sendCapacity = 0;
        }
    }
}
