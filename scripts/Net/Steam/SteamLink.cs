using System;
using System.Collections.Generic;

namespace Wavebreak.Net;

/// <summary>
/// The thin slice of Steam networking that <see cref="SteamPeer"/> needs:
/// send a message to a Steam user, drain what arrived, hear about sessions
/// that broke. Two implementations:
///   <see cref="SteamMessagesLink"/>  the real one, ISteamNetworkingMessages over
///                                    Valve's relay (no NAT or port setup)
///   <see cref="FakeSteamHub"/>       in memory, for --steam-selftest
/// Keeping Steam behind this seam is what lets the transport's framing and
/// peer bookkeeping be checked on a machine where Steam is not running.
/// </summary>
public interface ISteamLink
{
    /// <summary>This machine's Steam id (64-bit).</summary>
    ulong LocalId { get; }

    /// <summary>Queue one message for <paramref name="to"/>. Reliable messages
    /// arrive once and in order; unreliable ones may be lost or reordered.</summary>
    bool Send(ulong to, byte[] data, int length, bool reliable);

    /// <summary>Next message that arrived, if any.</summary>
    bool TryReceive(out ulong from, out byte[] data);

    /// <summary>Next user whose session failed (gone, unreachable), if any.</summary>
    bool TryTakeFailure(out ulong user);

    /// <summary>Stop listening. Pending outgoing reliable messages may still go.</summary>
    void Shutdown();
}

/// <summary>
/// An in-memory "Steam" for tests: every link registered on the hub can send
/// to every other one. Unreliable messages can be held back and released in
/// reverse to test ordering, and a user can be cut off to test timeouts.
/// </summary>
public sealed class FakeSteamHub
{
    private readonly Dictionary<ulong, FakeSteamLink> _links = new();
    private readonly HashSet<ulong> _cut = new();
    private readonly List<(ulong To, ulong From, byte[] Data)> _held = new();

    /// <summary>When set, unreliable messages wait in <see cref="ReleaseHeld"/>.</summary>
    public bool HoldUnreliable { get; set; }

    public int Sent { get; private set; }

    public FakeSteamLink Link(ulong steamId)
    {
        var l = new FakeSteamLink(this, steamId);
        _links[steamId] = l;
        return l;
    }

    /// <summary>Silently drop everything to and from <paramref name="user"/>
    /// (a pulled cable: no goodbye, no failure callback).</summary>
    public void Cut(ulong user) => _cut.Add(user);

    /// <summary>Deliver the held unreliable messages, newest first.</summary>
    public void ReleaseHeld(bool reversed)
    {
        var batch = new List<(ulong To, ulong From, byte[] Data)>(_held);
        _held.Clear();
        if (reversed) batch.Reverse();
        foreach (var (to, from, data) in batch) Deliver(to, from, data);
    }

    internal bool Send(ulong from, ulong to, byte[] data, int length, bool reliable)
    {
        if (!_links.ContainsKey(to)) return false;
        Sent++;
        byte[] copy = new byte[length];
        Array.Copy(data, copy, length);
        if (!reliable && HoldUnreliable) { _held.Add((to, from, copy)); return true; }
        Deliver(to, from, copy);
        return true;
    }

    private void Deliver(ulong to, ulong from, byte[] data)
    {
        if (_cut.Contains(to) || _cut.Contains(from)) return;
        if (_links.TryGetValue(to, out FakeSteamLink? l) && !l.Closed) l.Inbox.Enqueue((from, data));
    }
}

public sealed class FakeSteamLink : ISteamLink
{
    private readonly FakeSteamHub _hub;
    internal readonly Queue<(ulong From, byte[] Data)> Inbox = new();
    internal bool Closed;

    internal FakeSteamLink(FakeSteamHub hub, ulong id)
    {
        _hub = hub;
        LocalId = id;
    }

    public ulong LocalId { get; }

    public bool Send(ulong to, byte[] data, int length, bool reliable) => !Closed && _hub.Send(LocalId, to, data, length, reliable);

    public bool TryReceive(out ulong from, out byte[] data)
    {
        if (Inbox.Count > 0)
        {
            (from, data) = Inbox.Dequeue();
            return true;
        }
        from = 0;
        data = Array.Empty<byte>();
        return false;
    }

    public bool TryTakeFailure(out ulong user)
    {
        user = 0;
        return false;
    }

    public void Shutdown()
    {
        Closed = true;
        Inbox.Clear();
    }
}
