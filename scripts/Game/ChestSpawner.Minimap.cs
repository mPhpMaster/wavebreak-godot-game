using System.Collections.Generic;
using Wavebreak.Actors;

namespace Wavebreak.Game;

/// <summary>Read access for the minimap (UI/Minimap): the chests on this map.</summary>
public sealed partial class ChestSpawner
{
    /// <summary>Every chest on the current map, opened or not; check
    /// <c>IsOpen</c> and validity before use.</summary>
    public IReadOnlyList<Chest> Chests => _chests;
}
