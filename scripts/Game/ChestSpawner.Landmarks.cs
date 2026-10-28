using Godot;
using Wavebreak.Actors;
using Wavebreak.Core;

namespace Wavebreak.Game;

public sealed partial class ChestSpawner
{
    public bool PlaceLandmarkChest(Vector3 at, ChestTier tier)
    {
        if (_map is null || Net.Coop.IsClient || !at.IsFinite() || Arena.EdgeExcess(at) > -3f) return false;
        Spawn(new Request(tier, free: true), at, Arena.Centre);
        return true;
    }
}
