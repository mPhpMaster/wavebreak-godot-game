using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Combat;
using Wavebreak.Core;
using Wavebreak.Data;

namespace Wavebreak.Actors;

public sealed partial class EnemyManager
{
    // ---------------------------------------------------------- placement

    /// <summary>
    /// Notice whether the level has a navigation mesh worth querying.
    ///
    /// Re-checked periodically rather than once, because a level may build or
    /// bake its navigation after the manager is configured. An empty map is the
    /// normal case — a flat arena needs no navmesh — and querying one that has no
    /// regions returns the origin for every point, which would pile every spawn
    /// on the middle of the world. Hence the region count gate.
    /// </summary>
    private void RefreshNavigation(float runSeconds)
    {
        if (runSeconds < _navCheckAt) return;
        _navCheckAt = runSeconds + 2f;

        _navUsable = false;
        if (!IsInsideTree() || GetWorld3D() is not { } world) return;

        _navMap = world.NavigationMap;
        if (!_navMap.IsValid) return;

        _navUsable = NavigationServer3D.MapGetRegions(_navMap).Count > 0;
    }

    /// <summary>
    /// Find somewhere on the ring around the player that an enemy can stand.
    ///
    /// Candidates are rejected when nothing walkable is under them, so the level
    /// geometry decides the arena bounds rather than a constant that has to be
    /// kept in sync with it. When every ring angle fails — which is what standing
    /// in a corner looks like — the last resort aims inward, back toward the
    /// middle of the world, because that is the one direction guaranteed to have
    /// floor on it.
    /// </summary>
    private bool TryFindSpawnPoint(Vector3 around, out Vector3 point)
    {
        for (int attempt = 0; attempt < 8; attempt++)
        {
            Vector3 direction = _rng.OnUnitCircleXZ();
            float radius = _rng.Range(SpawnRingMin, SpawnRingMax);
            Vector3 candidate = around + direction * radius;

            if (OutsideArena(candidate))
            {
                // Near an edge, the opposite side of the ring is the obvious
                // second guess and keeps spawns flowing instead of stalling.
                candidate = around - direction * radius;
                if (OutsideArena(candidate)) continue;
            }

            if (!SnapToGround(ref candidate)) continue;

            point = candidate;
            return true;
        }

        // Inward fallback. Straight at the world origin from wherever the player
        // is; if the player is standing on the origin, any direction will do.
        Vector3 inward = new(-around.X, 0f, -around.Z);
        inward = inward.LengthSquared() > 0.01f ? inward.Normalized() : _rng.OnUnitCircleXZ();

        Vector3 last = around + inward * SpawnRingMin;
        if (SnapToGround(ref last))
        {
            point = last;
            return true;
        }

        last.Y = _groundKnown ? _lastGroundY : around.Y;
        point = last;
        return _groundKnown && !Wavebreak.Game.MapBlockers.Inside(last with { Y = -1f }, 1.5f);
    }

    private bool OutsideArena(Vector3 p)
        => ArenaRadius > 0f && p.X * p.X + p.Z * p.Z > ArenaRadius * ArenaRadius;

    /// <summary>
    /// Put a spawn candidate on ground the enemy can actually stand on, or
    /// reject it.
    ///
    /// A navigation map is the real answer to "is this walkable", so it wins when
    /// the level supplies one. Otherwise a downward ray against world collision
    /// decides: a miss means empty space, which is a rejection rather than
    /// something to paper over — an enemy dropped into the void either falls
    /// forever or gets caught by its safety floor and hovers, and both look far
    /// worse than the spawn simply happening a few metres round the ring.
    /// </summary>
    private bool SnapToGround(ref Vector3 candidate)
    {
        // Never inside a cliff or a building, nor on its roof (Game/MapBlockers).
        if (Wavebreak.Game.MapBlockers.Inside(candidate with { Y = -1f }, 1.5f)) return false;
        if (_navUsable)
        {
            Vector3 onMesh = NavigationServer3D.MapGetClosestPoint(_navMap, candidate);
            Vector3 delta = onMesh - candidate;
            delta.Y = 0f;
            // A distant snap means the candidate was off the mesh entirely and
            // the returned point is some far edge; reject rather than teleport.
            if (delta.LengthSquared() > 36f) return false;
            candidate = onMesh;
            _lastGroundY = candidate.Y;
            _groundKnown = true;
            return true;
        }

        if (!IsInsideTree() || GetWorld3D() is not { } world) return false;

        _groundQuery ??= new PhysicsRayQueryParameters3D
        {
            CollisionMask = Enemy.WorldLayer,
            CollideWithAreas = false,
            CollideWithBodies = true,
        };
        _groundQuery.From = candidate + new Vector3(0f, 30f, 0f);
        _groundQuery.To = candidate + new Vector3(0f, -40f, 0f);

        Godot.Collections.Dictionary hit = world.DirectSpaceState.IntersectRay(_groundQuery);
        if (hit.Count == 0 || !hit.TryGetValue("position", out Variant where)) return false;

        float y = where.AsVector3().Y;

        // Reject anything perched well above the floor the last spawn found. That
        // is scenery — a wall cap or a crate — and an enemy standing on it would
        // walk off the edge and be held in mid-air by its own safety floor.
        if (!Wavebreak.Game.Arena.Current.Underwater && _groundKnown && y > _lastGroundY + 2f) return false;
        if (Wavebreak.Game.Arena.Current.Underwater && y > Wavebreak.Game.UnderwaterTerrain.WaterCeiling - 2f) return false;

        candidate.Y = y;
        _lastGroundY = y;
        _groundKnown = true;
        return true;
    }

    private void CullStrays(Vector3 playerPosition)
    {
        if (_alive.Count == 0) return;

        float maxSq = DespawnRadius * DespawnRadius;
        int scanned = 0;
        int batch = Math.Min(CullBatch, _alive.Count);

        while (scanned < batch)
        {
            scanned++;
            if (_alive.Count == 0) return;
            _cullCursor = (_cullCursor + 1) % _alive.Count;

            Enemy e = _alive[_cullCursor];
            if (!e.IsAlive || e.IsBoss) continue;      // bosses are never recycled

            Vector3 delta = e.GlobalPosition - playerPosition;
            delta.Y = 0f;
            if (delta.LengthSquared() > maxSq) e.Despawn();
        }
    }

}
