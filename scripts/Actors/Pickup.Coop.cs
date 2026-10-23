using System.Collections.Generic;
using Godot;
using Wavebreak.Core;
using Wavebreak.Progression;

namespace Wavebreak.Actors;

/// <summary>
/// Co-op pickups. The host owns every orb: it rolls the drops, decides which
/// player an orb flies to (the first whose pickup range it enters, then the
/// nearest living one if that player goes down or leaves), and pays out.
/// Experience is shared: every orb anyone collects is credited to every
/// player's own bar (each still applies their own XP-gain stat), as in Bonk
/// With Friends. Gold and med-packs go to whoever picked them up.
///
/// A client sees the same orbs (<see cref="SpawnNet"/>) and animates the
/// magnet toward its own player, but never pays anything out itself; the host
/// sends the payout and removes the orb.
/// </summary>
public sealed partial class Pickup
{
    /// <summary>Session-wide id (host-assigned). 0 = not networked.</summary>
    internal int NetId;

    /// <summary>Host: peer this orb is flying to (0 = not latched yet).</summary>
    private long _owner;

    /// <summary>Client: reached the local player, waiting for the host's word.</summary>
    private bool _netPending;
    private float _pendingFor;

    /// <summary>Set around a spawn whose drop belongs to this machine's player
    /// alone (breakable pots: every player has their own). Never networked.</summary>
    internal static bool Personal;

    private void ResetNetState()
    {
        NetId = 0;
        _owner = 0;
        _netPending = false;
        _pendingFor = 0f;
    }

    /// <summary>A personal orb (<see cref="NetId"/> 0) in a co-op run: the
    /// single-player rule, for the local player only. True when it was
    /// collected and released.</summary>
    private static bool StepPersonal(Pickup p, int index, float dt)
    {
        Player? player = Player.Instance;
        bool live = player is not null && IsInstanceValid(player) && player.IsAlive;
        Vector3 target = live ? player!.GlobalPosition + Vector3.Up * 0.9f : Vector3.Zero;
        float range = live ? player!.Stats.Get(Stats.Stat.PickupRange) : 0f;
        if (!p.Step(dt, target, range * range, live)) return false;
        p.Collect(player);
        SwapRemove(index);
        _pool?.Release(p);
        return true;
    }

    private static readonly Dictionary<int, Pickup> ByNetId = new();
    private static readonly List<(long Peer, Vector3 Hip, float Range)> Collectors = new();

    /// <summary>Co-op counters for the automated test.</summary>
    internal static int NetSpawned, NetCollectedRemote;

    // ------------------------------------------------------------------ host

    private static void HostSpawned(Pickup p, Vector3 launch)
    {
        p._owner = 0;
        Net.NetSession.Instance?.OnPickupSpawned(p, launch);
        ByNetId[p.NetId] = p;
    }

    private static void UpdateAllHost(float dt)
    {
        Collectors.Clear();
        Net.NetSession.Instance?.FillCollectors(Collectors);

        for (int i = Live.Count - 1; i >= 0; i--)
        {
            Pickup p = Live[i];
            if (!IsInstanceValid(p) || p.IsPooled)
            {
                SwapRemove(i);
                continue;
            }

            if (p.NetId == 0) { StepPersonal(p, i, dt); continue; }   // a pot's drop: ours alone
            int who = p.ResolveOwner();
            bool chasing = p._attracted && who >= 0;
            Vector3 target = chasing ? Collectors[who].Hip : Vector3.Zero;

            if (p.Step(dt, target, 0f, chasing))
            {
                long peer = Collectors[who].Peer;
                Net.NetSession.Instance?.HostCollected(p, peer);
                if (peer == Net.Coop.LocalId) p.Collect(Player.Instance);
                else p.ClearPayout();
                ByNetId.Remove(p.NetId);
                p.NetId = 0;
                SwapRemove(i);
                _pool?.Release(p);
            }
        }
    }

    /// <summary>Host: index into <see cref="Collectors"/> of the player this orb
    /// chases, latching it on the first one in range. -1 = nobody.</summary>
    private int ResolveOwner()
    {
        Vector3 at = GlobalPosition;
        if (!_attracted)
        {
            for (int c = 0; c < Collectors.Count; c++)
            {
                float r = Collectors[c].Range;
                if (at.DistanceSquaredTo(Collectors[c].Hip) <= r * r)
                {
                    _attracted = true;
                    _owner = Collectors[c].Peer;
                    return c;
                }
            }
            return -1;
        }

        for (int c = 0; c < Collectors.Count; c++)
            if (Collectors[c].Peer == _owner) return c;

        // Latched by "collect everything", or the owner went down or left:
        // the nearest living player takes over.
        int best = -1;
        float bestSq = float.MaxValue;
        for (int c = 0; c < Collectors.Count; c++)
        {
            float sq = at.DistanceSquaredTo(Collectors[c].Hip);
            if (sq < bestSq) { bestSq = sq; best = c; }
        }
        _owner = best >= 0 ? Collectors[best].Peer : 0;
        return best;
    }

    /// <summary>Host: paid out elsewhere (a remote collector); just empty it.</summary>
    private void ClearPayout()
    {
        _value = 0f;
        _attracted = false;
        Visible = false;
    }

    /// <summary>Kind and value, for the host's payout message.</summary>
    internal (PickupKind Kind, float Value) NetPayload => (_kind, _value);

    // ---------------------------------------------------------------- client

    /// <summary>Client: the host dropped an orb.</summary>
    internal static void SpawnNet(int id, PickupKind kind, Vector3 position, float value, Vector3 launch)
    {
        if (value <= 0f || ByNetId.ContainsKey(id)) return;
        Pickup? p = _pool?.Get();
        if (p is null) return;
        p.Activate(kind, position, value, launch);
        p.NetId = id;
        p._netPending = false;
        p._pendingFor = 0f;
        Live.Add(p);
        ByNetId[id] = p;
        NetSpawned++;
    }

    /// <summary>Client: the host says the orb is gone (someone collected it).</summary>
    internal static void NetGone(int id)
    {
        if (!ByNetId.Remove(id, out Pickup? p) || !IsInstanceValid(p) || p.IsPooled) return;
        p.NetId = 0;
        p.ClearPayout();
        int i = Live.IndexOf(p);
        if (i >= 0) SwapRemove(i);
        _pool?.Release(p);
    }

    private static void UpdateAllClient(float dt)
    {
        Player? player = Player.Instance;
        bool live = player is not null && IsInstanceValid(player) && player.IsAlive;
        Vector3 target = live ? player!.GlobalPosition + Vector3.Up * 0.9f : Vector3.Zero;
        float range = live ? player!.Stats.Get(Stats.Stat.PickupRange) : 0f;

        for (int i = Live.Count - 1; i >= 0; i--)
        {
            Pickup p = Live[i];
            if (!IsInstanceValid(p) || p.IsPooled)
            {
                SwapRemove(i);
                continue;
            }
            if (p.NetId == 0) { StepPersonal(p, i, dt); continue; }   // a pot's drop: ours alone
            if (p._netPending)
            {
                // Touched; the host's confirmation removes it. If it never
                // comes (the host gave it to someone else and the message was
                // lost with a disconnect), let it go anyway.
                p._pendingFor += dt;
                if (p._pendingFor > 3f) NetGone(p.NetId);
                continue;
            }
            if (p.Step(dt, target, range * range, live))
            {
                p._netPending = true;
                p.Visible = false;
            }
        }
    }

    /// <summary>Back to single-player state (scene change).</summary>
    internal static void ResetNet()
    {
        ByNetId.Clear();
        Collectors.Clear();
        NetSpawned = 0;
        NetCollectedRemote = 0;
    }
}
