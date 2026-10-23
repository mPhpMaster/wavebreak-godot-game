using System;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Core;
using Wavebreak.Progression;

namespace Wavebreak.Game;

/// <summary>
/// Co-op chests. The host places every chest (clients never roll a placement)
/// and settles who opens each one: pressing E sends a request, the host grants
/// it to the first asker and tells everyone else the chest is taken. The
/// winner then pays and rolls the reward from their own gold, price ladder and
/// level-up pool, exactly as in single-player — chests are shared, loot is not.
/// </summary>
public sealed partial class ChestSpawner
{
    private bool _netApproved;

    /// <summary>Gold and affordability are checked here first, so a player
    /// who cannot pay hears "no" at once instead of after a round trip.</summary>
    private bool AskHost(Chest chest)
    {
        if (chest.NetPending) return false;
        if (!CanAfford(chest))
        {
            Denied++;
            chest.Deny();
            _prompt.Deny();
            int gold = Inventory.Current?.Gold ?? 0;
            EventBus.Publish(new ChestDenied(chest.GlobalPosition, PriceFor(chest), gold));
            return false;
        }
        chest.NetPending = true;
        Net.NetSession.Instance?.AskChest(chest);
        return false;
    }

    /// <summary>The host gave this chest to us: open it for real.</summary>
    internal void NetGranted(Chest chest)
    {
        chest.NetPending = false;
        _netApproved = true;
        try { TryOpen(chest); }
        finally { _netApproved = false; }
    }

    /// <summary>Someone else got there first.</summary>
    internal void NetTakenBy(Chest chest, string who, Color color)
    {
        chest.NetPending = false;
        if (!IsInstanceValid(chest) || chest.IsOpen) return;
        chest.Open(who, color);
    }

    internal void NetDenied(Chest chest) => chest.NetPending = false;

    /// <summary>Client: the host placed a chest.</summary>
    internal Chest? NetSpawn(int id, ChestTier tier, bool free, Vector3 at, float yaw)
    {
        if (_map is null || !IsInstanceValid(_map)) return null;
        var chest = new Chest(tier, free) { SpawnedAt = _clock, NetId = id };
        _map.AddChild(chest);
        chest.GlobalPosition = at;
        chest.Rotation = new Vector3(0f, yaw, 0f);
        _chests.Add(chest);
        Spawned++;
        EventBus.Publish(new ChestSpawned(at, tier, free));
        if (free)
            EventBus.Publish(new PopupRequested(at + Vector3.Up * 2.4f, "FREE CHEST", new Color(0.55f, 1f, 0.65f), 1.4f));
        return chest;
    }
}
