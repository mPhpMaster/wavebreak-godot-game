using System.Collections.Generic;
using Godot;

namespace Wavebreak.Actors;

/// <summary>
/// Host migration and late join for orbs (Net/NetSession.Migration.cs,
/// NetSession.Snapshot.cs). A client already holds every orb the host dropped
/// (with the host's ids); the new host simply starts deciding who collects
/// them. A joining or reconnecting client lines its orbs up with the host's
/// list in the world snapshot.
/// </summary>
public sealed partial class Pickup
{
    /// <summary>New host: orbs that were waiting for the old host's word
    /// come back into play (the nearest player collects them). Returns the
    /// highest orb id held, so new ids continue past it.</summary>
    internal static int PromoteToHost()
    {
        int max = 0;
        for (int i = Live.Count - 1; i >= 0; i--)
        {
            Pickup p = Live[i];
            if (!IsInstanceValid(p) || p.IsPooled || p.NetId == 0) continue;
            if (p.NetId > max) max = p.NetId;
            if (p._netPending)
            {
                p._netPending = false;
                p._pendingFor = 0f;
                p.Visible = true;
                p._attracted = true;   // it had reached this player already
            }
            p._owner = 0;
        }
        return max;
    }

    /// <summary>Host: every networked orb on the floor, for the world snapshot
    /// (id, kind, x, y, z, value).</summary>
    internal static void WriteSnapshot(List<float> into)
    {
        foreach (Pickup p in Live)
        {
            if (!IsInstanceValid(p) || p.IsPooled || p.NetId == 0 || p._value <= 0f) continue;
            Vector3 at = p.GlobalPosition;
            into.Add(p.NetId); into.Add((int)p._kind);
            into.Add(at.X); into.Add(at.Y); into.Add(at.Z); into.Add(p._value);
        }
    }

    /// <summary>Client: drop every networked orb the host does not list.</summary>
    internal static void KeepOnly(HashSet<int> ids)
    {
        var gone = new List<int>();
        foreach (KeyValuePair<int, Pickup> kv in ByNetId)
            if (!ids.Contains(kv.Key)) gone.Add(kv.Key);
        foreach (int id in gone) NetGone(id);
    }

    /// <summary>Client: an orb in the snapshot this machine does not have yet.</summary>
    internal static void SnapshotSpawn(int id, PickupKind kind, Vector3 at, float value)
    {
        if (ByNetId.ContainsKey(id)) return;
        SpawnNet(id, kind, at, value, Vector3.Zero);
    }
}
