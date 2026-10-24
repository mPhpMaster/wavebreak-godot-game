namespace Wavebreak.Game;

/// <summary>
/// Host migration (Net/NetSession.Migration.cs): a client never places chests,
/// so it never learned the map's obstacles. The new host learns them now, so
/// the chests it tops up and drops from elites keep out of shrines and props
/// like the old host's did.
/// </summary>
public sealed partial class ChestSpawner
{
    internal void PromoteToHost()
    {
        if (_map is not null && IsInstanceValid(_map)) CollectObstacles(_map);
    }
}
