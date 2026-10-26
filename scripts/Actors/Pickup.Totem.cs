using System;
using System.Collections.Generic;
using Godot;

namespace Wavebreak.Actors;

/// <summary>
/// The Tide Totem's pull (Actors/TideTotem, Game/Interactables.Totems.cs).
/// Like <see cref="AttractAll"/>, but only experience and gold (med-packs stay
/// where they are, so the totem never replaces finding a heal), aimed at one
/// player, and fast: a tide orb ramps to <see cref="TideMaxSpeed"/>, so an orb
/// on the far side of Stormwatch (about 300 m) arrives in under five seconds.
///
/// Co-op: the host latches every networked orb to the peer who woke the totem
/// (the host's own collector loop then flies it there and pays it out as
/// usual: experience shared, gold to the collector). Every machine latches its
/// personal orbs (pot drops) only when it woke the totem itself. A client also
/// latches its copies of the networked orbs toward itself when it is the one,
/// purely to show them flying; the host's payout is what counts.
/// </summary>
public sealed partial class Pickup
{
    private const float TideMaxSpeed = 85f;
    private const float TideAcceleration = 150f;

    /// <summary>Pulled by a totem: the faster chase in <see cref="Step"/>.</summary>
    private bool _tide;

    /// <summary>What a totem pulls: experience and gold, never med-packs.</summary>
    public static bool TidePulls(PickupKind kind) => kind is PickupKind.Xp or PickupKind.Gold;

    /// <summary>
    /// Latch every experience orb and coin this machine is in charge of toward
    /// <paramref name="peer"/> (the local player in single-player). Returns how
    /// many were latched; <paramref name="netIds"/>, when given, collects the
    /// networked ones (for the co-op test).
    /// </summary>
    public static int CallTide(long peer, List<int>? netIds = null)
    {
        bool coop = Net.Coop.InRun;
        bool host = coop && Net.Coop.IsHost;
        bool mine = !coop || peer == Net.Coop.LocalId;
        int n = 0;
        for (int i = 0; i < Live.Count; i++)
        {
            Pickup p = Live[i];
            if (!IsInstanceValid(p) || p.IsPooled || p._value <= 0f || !TidePulls(p._kind)) continue;
            if (p.NetId == 0 || !coop)
            {
                if (!mine) continue;                 // someone else's pot drop
            }
            else if (host)
            {
                p._owner = peer;                     // Pickup.Coop.cs: flies to that player
                netIds?.Add(p.NetId);
            }
            else
            {
                if (!mine || p._netPending) continue; // a client only shows its own pull
                netIds?.Add(p.NetId);
            }
            p._attracted = true;
            p._tide = true;
            p._speed = MathF.Max(p._speed, AttractStartSpeed * 2f);
            n++;
        }
        return n;
    }

    /// <summary>Experience orbs and coins lying on the floor, not yet chasing
    /// anyone, at least <paramref name="minDistance"/> from
    /// <paramref name="from"/>: what a totem would gain the autopilot.</summary>
    public static int LooseOrbs(Vector3 from, float minDistance)
    {
        float minSq = minDistance * minDistance;
        int n = 0;
        for (int i = 0; i < Live.Count; i++)
        {
            Pickup p = Live[i];
            if (!IsInstanceValid(p) || p.IsPooled || p._attracted || !TidePulls(p._kind)) continue;
            if (p.GlobalPosition.DistanceSquaredTo(from) >= minSq) n++;
        }
        return n;
    }

    /// <summary>Experience orbs and coins on the floor (any state). For the
    /// totem self-check.</summary>
    public static int TideOrbsLeft()
    {
        int n = 0;
        for (int i = 0; i < Live.Count; i++)
            if (IsInstanceValid(Live[i]) && !Live[i].IsPooled && Live[i]._value > 0f && TidePulls(Live[i]._kind)) n++;
        return n;
    }

    /// <summary>Co-op test: is the networked orb <paramref name="id"/> still on
    /// this machine's floor?</summary>
    internal static bool NetOrbAlive(int id) => ByNetId.TryGetValue(id, out Pickup? p) && IsInstanceValid(p) && !p.IsPooled;
}
