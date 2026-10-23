using System.Collections.Generic;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Core;
using Wavebreak.Data;

namespace Wavebreak.Game;

// "Spawn this here" entry points for the developer panel (UI/DevPanel). They
// reuse each spawner's own creation path and bookkeeping, and skip only the
// random placement search: the panel already knows where it wants the thing.

public sealed partial class ChestSpawner
{
    /// <summary>A chest of <paramref name="tier"/> at <paramref name="at"/>
    /// (ground height is the caller's), facing the player. Null without a map.</summary>
    public Chest? SpawnAt(ChestTier tier, bool free, Vector3 at)
    {
        if (_map is null || !IsInstanceValid(_map)) return null;
        Vector3 face = Player.Instance is { } p && IsInstanceValid(p) ? p.GlobalPosition : _centre;
        int before = _chests.Count;
        Spawn(new Request(tier, free, at), at, face);
        return _chests.Count > before ? _chests[^1] : null;
    }
}

public sealed partial class ShrineSpawner
{
    /// <summary>A map shrine of <paramref name="kind"/> at <paramref name="at"/>
    /// under <paramref name="map"/>, tracked like the placed ones.</summary>
    public MapShrine SpawnAt(MapShrineKind kind, Node3D map, Vector3 at)
    {
        var shrine = new MapShrine(kind);
        map.AddChild(shrine);
        shrine.GlobalPosition = at;
        _shrines.Add(shrine);
        Placed++;
        GD.Print($"[shrine] dev placed {kind} at=({at.X:0.0},{at.Z:0.0})");
        return shrine;
    }
}

public sealed partial class Interactables
{
    /// <summary>A merchant or microwave at <paramref name="at"/>, stocked and
    /// usable like a placed one. Null without a map.</summary>
    public Station? SpawnStationAt(Station station, Vector3 at)
    {
        if (_map is null || !IsInstanceValid(_map)) { station.Free(); return null; }
        _map.AddChild(station);
        station.GlobalPosition = at;
        if (Player.Instance is { } p && IsInstanceValid(p))
        {
            Vector3 to = p.GlobalPosition - at; to.Y = 0f;
            if (to.LengthSquared() > 0.01f) station.Rotation = new Vector3(0f, Mathf.Atan2(to.X, to.Z), 0f);
        }
        _stations.Add(station);
        StationsPlaced++;
        GD.Print($"[stations] dev placed {station.GetType().Name} at=({at.X:0.0},{at.Y:0.0},{at.Z:0.0})");
        return station;
    }

    /// <summary>Breakable pots in a small ring around <paramref name="at"/>,
    /// added to this map's set (one is created if the map had none).</summary>
    public int SpawnPotsAt(Vector3 at, int count)
    {
        if (_map is null || !IsInstanceValid(_map) || count <= 0) return 0;
        var spots = new List<Vector3>(count);
        for (int i = 0; i < count; i++)
        {
            float a = Mathf.Tau * i / count;
            float r = count == 1 ? 0f : 1.3f + 0.25f * (i % 2);
            spots.Add(at + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
        }
        if (_pots is null || !IsInstanceValid(_pots) || _pots.IsQueuedForDeletion())
        {
            _pots = new Breakables();
            _map.AddChild(_pots);
            _pots.Build(spots, new Rng(Rng.Seed ^ 0xDE7UL), _phase, _radius + 8f);
        }
        else _pots.DevAppend(spots, _radius + 8f);
        PotsPlaced += count;
        return count;
    }
}

public sealed partial class Breakables
{
    /// <summary>Rebuild with every unbroken pot plus <paramref name="extra"/>.</summary>
    public void DevAppend(List<Vector3> extra, float arenaRadius)
    {
        var spots = new List<Vector3>(_alive + extra.Count);
        foreach (Pot p in _pots) if (!p.Broken) spots.Add(p.Position);
        spots.AddRange(extra);
        for (int v = 0; v < _multis.Length; v++)
        {
            if (_multis[v] is { } mmi && IsInstanceValid(mmi)) mmi.QueueFree();
            _multis[v] = null;
        }
        Build(spots, _rng, _phase, arenaRadius);
    }
}
