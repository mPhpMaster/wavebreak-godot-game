using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Core;
using Wavebreak.Data;
using Wavebreak.Progression;
using Wavebreak.UI;

namespace Wavebreak.Game;

public sealed partial class Interactables
{
    // ------------------------------------------------------------ placement

    public override void _PhysicsProcess(double delta)
    {
        using var __prof = Wavebreak.Game.Prof.Scope("Interactables.phys");
        if (_placeIn < 0) return;
        if (_placeIn-- > 0) return;
        _placeIn = -1;

        if (_map is null || !IsInstanceValid(_map) || !_map.IsInsideTree()) return;
        PhysicsDirectSpaceState3D? space = _map.GetWorld3D()?.DirectSpaceState;
        if (space is null) return;
        if (!PerformanceScenario.Reference && (Arena.Current.Underwater || Arena.IsCity))
        { StepExpeditionPlacements(space); return; }

        using (Prof.Scope("Place.obstacles")) CollectObstacles(_map);
        Vector3 start = Player.Instance is { } p && IsInstanceValid(p) ? p.GlobalPosition : _centre;

        using (Prof.Scope("Place.stations"))
        {
            for (int i = 0; i < _wantMerchants; i++) PlaceStation(space, new Merchant(), start, 0.2f, 0.7f);
            for (int i = 0; i < _wantMicrowaves; i++) PlaceStation(space, new Microwave(), start, 0.15f, 0.85f);
        }
        using (Prof.Scope("Place.pots")) PlacePots(space, start);
        using (Prof.Scope("Place.totems")) PlaceTotems(space, start);   // Interactables.Totems.cs (own rng: the rest is unchanged)
    }

    private void PlaceStation(PhysicsDirectSpaceState3D space, Station station, Vector3 start, float minFrac, float maxFrac)
    {
        for (int attempt = 0; attempt < 64; attempt++)
        {
            float lo = _radius * minFrac, hi = _radius * maxFrac;
            float d = MathF.Sqrt(Rng.Range(lo * lo, hi * hi));
            Vector3 candidate = _centre + Rng.OnUnitCircleXZ() * d;
            float spacing = attempt < 40 ? StationSpacing : StationSpacing * 0.5f;

            if (!Validate(space, candidate, start, station.FootprintRadius, 2.8f, 0.9f, 2.5f, out Vector3 spot)) continue;
            if (Flat(spot).DistanceTo(Flat(start)) < 12f) continue;
            if (NearStation(spot, spacing)) continue;
            if (ChestSpawner.Instance?.Nearest(spot, StationChestClearance) is not null) continue;
            if (!Level(space, spot, station.FootprintRadius)) continue;

            _map!.AddChild(station);
            station.GlobalPosition = spot;
            Vector3 to = start - spot; to.Y = 0f;
            station.Rotation = new Vector3(0f, to.LengthSquared() > 0.01f ? MathF.Atan2(to.X, to.Z) : 0f, 0f);
            if (Arena.Current.Underwater)
                Visuals.ReefActorSupport.Ground(station, new Vector3(station.FootprintRadius, 1f, station.FootprintRadius));
            _stations.Add(station);
            StationsPlaced++;
            GD.Print($"[stations] placed {station.GetType().Name} at=({spot.X:0.0},{spot.Y:0.0},{spot.Z:0.0}) phase={_phase}");
            return;
        }
        station.Free();
        GD.Print("[stations] no valid spot found; station dropped");
    }

    private void PlacePots(PhysicsDirectSpaceState3D space, Vector3 start)
    {
        if (_wantPots <= 0 || _map is null) return;
        var spots = new List<Vector3>(_wantPots);

        for (int cluster = 0; cluster < _wantPots * 2 && spots.Count < _wantPots; cluster++)
        {
            float d = MathF.Sqrt(Rng.Float()) * (_radius - 4f);
            Vector3 hub = _centre + Rng.OnUnitCircleXZ() * d;
            int size = Rng.RangeInt(ClusterMin, ClusterMax + 1);
            for (int k = 0; k < size && spots.Count < _wantPots; k++)
            {
                for (int attempt = 0; attempt < 6; attempt++)
                {
                    Vector3 candidate = hub + Rng.OnUnitCircleXZ() * Rng.Range(0f, 2.6f);
                    if (!Validate(space, candidate, start, 0.45f, 1.2f, 0.8f, MaxPerchHeight, out Vector3 spot)) continue;
                    if (Flat(spot).DistanceTo(Flat(start)) < 6f) continue;
                    if (NearStation(spot, 2.8f)) continue;
                    if (ChestSpawner.Instance?.Nearest(spot, 2.2f) is not null) continue;
                    bool crowded = false;
                    foreach (Vector3 other in spots)
                        if (Flat(other).DistanceSquaredTo(Flat(spot)) < PotSpacing * PotSpacing) { crowded = true; break; }
                    if (crowded) continue;
                    spots.Add(spot);
                    break;
                }
            }
        }

        _pots = new Breakables();
        _map.AddChild(_pots);
        _pots.Build(spots, new Rng(Rng.Seed ^ 0xB0BB1E5UL ^ (ulong)_phase), _phase, _radius + 8f);
        PotsPlaced += spots.Count;
        GD.Print($"[stations] placed {spots.Count}/{_wantPots} breakables phase={_phase}");
    }

    private bool Validate(PhysicsDirectSpaceState3D space, Vector3 candidate, Vector3 start, float footprint,
                          float headroom, float minNormalY, float maxHeight, out Vector3 spot)
    {
        spot = default;
        var flat = new Vector2(candidate.X - _centre.X, candidate.Z - _centre.Z);
        if (flat.Length() > _radius - 3f) return false;

        if (Ground(space, candidate, out Vector3 normal) is not { } g) return false;
        if (normal.Y < minNormalY) return false;
        if (Arena.Current.Underwater)
        {
            if (g.Y > UnderwaterTerrain.WaterCeiling - 2f || g.Y < -30f) return false;
        }
        else if (g.Y - _centre.Y > maxHeight || g.Y - _centre.Y < -2f) return false;
        if (Ray(space, g + Vector3.Up * 0.1f, g + Vector3.Up * headroom, out _, out _)) return false;
        if (OverlapsObstacle(g, footprint) || MapBlockers.Inside(g with { Y = -1f }, footprint + 1f)) return false;
        spot = g;
        return true;
    }

    /// <summary>The footprint corners are within 0.3 m of the centre's height:
    /// a stall half on a slope looks broken even when the centre is flat.</summary>
    private bool Level(PhysicsDirectSpaceState3D space, Vector3 g, float r)
    {
        for (int i = 0; i < 4; i++)
        {
            float a = Mathf.Tau * i / 4f + 0.4f;
            Vector3 at = g + new Vector3(MathF.Cos(a), 0f, MathF.Sin(a)) * r;
            if (Ground(space, at, out _) is not { } h || MathF.Abs(h.Y - g.Y) > 0.3f) return false;
        }
        return true;
    }

    private bool NearStation(Vector3 p, float spacing)
    {
        foreach (Station s in _stations)
            if (IsInstanceValid(s) && Flat(s.GlobalPosition).DistanceSquaredTo(Flat(p)) < spacing * spacing) return true;
        return false;
    }

    private static Vector3 Flat(Vector3 v) => new(v.X, 0f, v.Z);

    private Vector3? Ground(PhysicsDirectSpaceState3D space, Vector3 at, out Vector3 normal)
    {
        Vector3 from = new(at.X, _centre.Y + MaxPerchHeight + 25f, at.Z);
        Vector3 to = new(at.X, _centre.Y - (Arena.Current.Underwater ? 40f : 10f), at.Z);
        return Ray(space, from, to, out Vector3 point, out normal) ? point : null;
    }

    private static bool Ray(PhysicsDirectSpaceState3D space, Vector3 from, Vector3 to, out Vector3 point, out Vector3 normal)
    {
        var q = PhysicsRayQueryParameters3D.Create(from, to, collisionMask: 1u);
        if (Player.Instance is { } p && IsInstanceValid(p))
            q.Exclude = new Godot.Collections.Array<Rid> { p.GetRid() };
        var res = space.IntersectRay(q);
        if (res.Count == 0) { point = default; normal = Vector3.Up; return false; }
        point = (Vector3)res["position"];
        normal = (Vector3)res["normal"];
        return true;
    }

    /// <summary>Visible prop boxes, as the chest spawner collects them: mesh-only
    /// scenery has no collider for a ray to find.</summary>
    private void CollectObstacles(Node root)
    {
        _obstacles.Clear();
        var stack = new Stack<Node>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            Node n = stack.Pop();
            if (n is Chest or Station or Breakables) continue;
            if (n is MeshInstance3D mi && mi.Mesh is not null)
            {
                Aabb box = mi.GlobalTransform * mi.GetAabb();
                bool huge = box.Size.X > 14f || box.Size.Z > 14f;
                bool flat = box.Size.Y < 0.15f;
                if (!huge && !flat) _obstacles.Add(box);
            }
            foreach (Node child in n.GetChildren()) stack.Push(child);
        }
    }

    private bool OverlapsObstacle(Vector3 g, float r)
    {
        var footprint = new Aabb(g - new Vector3(r + 0.3f, -0.05f, r + 0.3f), new Vector3((r + 0.3f) * 2f, 1.4f, (r + 0.3f) * 2f));
        foreach (Aabb box in _obstacles)
        {
            if (g.Y >= box.End.Y - 0.05f) continue;
            if (footprint.Intersects(box)) return true;
        }
        return false;
    }

    private static string KeyName()
    {
        if (!InputMap.HasAction(InteractAction)) return "E";
        foreach (InputEvent ev in InputMap.ActionGetEvents(InteractAction))
        {
            if (ev is not InputEventKey k) continue;
            Key code = k.PhysicalKeycode != Key.None ? k.PhysicalKeycode : k.Keycode;
            if (code != Key.None) return OS.GetKeycodeString(code);
        }
        return "E";
    }

}
