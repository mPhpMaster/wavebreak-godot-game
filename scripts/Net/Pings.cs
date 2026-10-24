using System.Collections.Generic;
using Godot;
using Wavebreak.Meta;

namespace Wavebreak.Net;

public enum PingKind { GoHere, Enemy, Chest, Shrine, Help }

/// <summary>One live ping: who placed it, what it marks, where, and when.</summary>
public sealed class Ping
{
    public long Peer;
    public int Slot;
    public string Name = "";
    public PingKind Kind;
    public Vector3 Position;
    public double Born;
    public double Age => Time.GetTicksMsec() / 1000.0 - Born;
}

/// <summary>
/// The live pings of a run and the rate limit. A ping lasts
/// <see cref="Lifetime"/> seconds; a player may place at most
/// <see cref="Burst"/> in any <see cref="Window"/> seconds, checked on the
/// sender (so the key simply does nothing when spent) and again on the host
/// (so a client cannot flood the others).
///
/// Shown by <c>UI/PingMarkers</c> (world marker, edge arrow, sound) and
/// <c>UI/Minimap</c> (a blip). Synced through the host
/// (<c>NetSession.Pings</c>); in single-player a ping is a local marker.
/// </summary>
public static class Pings
{
    public const float Lifetime = 6f;
    public const int Burst = 3;
    public const float Window = 5f;

    private static readonly List<Ping> Live = new();
    private static readonly Dictionary<long, Queue<double>> Sent = new();

    /// <summary>Pings shown on this machine this run, and those from others (the test reads them).</summary>
    public static int Shown { get; private set; }
    public static int ShownFromOthers { get; private set; }

    /// <summary>Raised for every new ping shown here (the sound).</summary>
    public static event System.Action<Ping>? Added;

    public static IReadOnlyList<Ping> Active
    {
        get
        {
            for (int i = Live.Count - 1; i >= 0; i--)
                if (Live[i].Age > Lifetime) Live.RemoveAt(i);
            return Live;
        }
    }

    public static void Reset()
    {
        Live.Clear();
        Sent.Clear();
        Shown = 0;
        ShownFromOthers = 0;
    }

    /// <summary>Within the rate limit? Records the ping if so.</summary>
    public static bool Allow(long peer)
    {
        double now = Time.GetTicksMsec() / 1000.0;
        if (!Sent.TryGetValue(peer, out Queue<double>? q)) Sent[peer] = q = new Queue<double>();
        while (q.Count > 0 && now - q.Peek() > Window) q.Dequeue();
        if (q.Count >= Burst) return false;
        q.Enqueue(now);
        return true;
    }

    /// <summary>Show a ping on this machine.</summary>
    public static void Show(long peer, PingKind kind, Vector3 at)
    {
        CoopPeer? p = Coop.Peer(peer);
        var ping = new Ping
        {
            Peer = peer,
            Slot = p?.Slot ?? 0,
            Name = p?.Name ?? NetSession.PlayerName,
            Kind = kind,
            Position = at,
            Born = Time.GetTicksMsec() / 1000.0,
        };
        // One live ping per player: a new one replaces the old.
        Live.RemoveAll(x => x.Peer == peer);
        Live.Add(ping);
        Shown++;
        if (peer != Coop.LocalId) ShownFromOthers++;
        Added?.Invoke(ping);
    }

    /// <summary>"Enemy here", "Chest", ... (translated).</summary>
    public static string Label(PingKind kind) => Loc.T(kind switch
    {
        PingKind.Enemy => "Enemy here",
        PingKind.Chest => "Chest",
        PingKind.Shrine => "Shrine",
        PingKind.Help => "Help!",
        _ => "Go here",
    });
}
