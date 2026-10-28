using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Core;
using Wavebreak.Data;
using Wavebreak.Progression;
using Wavebreak.UI;

namespace Wavebreak.Game;

public sealed partial class ChestSpawner
{
    // ------------------------------------------------------------ placement

    private ChestTier RollTier() => Rng.Chance(GildedChance) ? ChestTier.Gilded : ChestTier.Standard;

    private void TopUp()
    {
        if (_clock < _nextTopUp) return;
        _nextTopUp = _clock + TopUpInterval;
        if (_map is null || Net.Coop.IsClient) return;

        int paid = 0;
        foreach (Chest c in _chests) if (Usable(c) && !c.IsFree) paid++;
        if (paid + _pending.Count < MaxWorldChests)
            _pending.Enqueue(new Request(RollTier(), free: false));
    }

    private void OnEnemyKilled(EnemyKilled e)
    {
        if (!e.WasElite || _map is null || Net.Coop.IsClient) return;

        bool boss = e.GoldValue >= BossGoldThreshold;
        if (!boss)
        {
            if (!Rng.Chance(EliteDropChance + _pity)) { _pity += ElitePityStep; return; }
        }

        int free = 0;
        foreach (Chest c in _chests) if (Usable(c) && c.IsFree) free++;
        if (free >= MaxFreeChests) return;

        _pity = 0f;
        Dropped++;
        _pending.Enqueue(new Request(boss ? ChestTier.Gilded : ChestTier.Standard, free: true, at: e.Position));
    }

    private void Place(PhysicsDirectSpaceState3D space, Request r)
    {
        Vector3 playerPos = Player.Instance is { } p && IsInstanceValid(p) ? p.GlobalPosition : _centre;

        if (r.At is { } at)
        {
            // A drop lands where the enemy died; only the ground height and the
            // map edge are checked. Moving it somewhere "valid" would break the
            // link between the kill and the reward, which is the whole point.
            Vector3 flat = ClampToArea(at);
            Vector3 ground = Ground(space, flat, out _) ?? new Vector3(flat.X, _centre.Y, flat.Z);
            Spawn(r, ground, playerPos);
            return;
        }

        for (int attempt = 0; attempt < 48; attempt++)
        {
            Vector3 candidate;
            if (r.NearStart)
            {
                // The first chest is a tutorial: close enough to be seen at the
                // start, far enough that reaching it is a choice.
                Vector3 dir = Rng.OnUnitCircleXZ();
                candidate = playerPos + dir * Rng.Range(7f, 11f);
            }
            else
            {
                float d = MathF.Sqrt(Rng.Float()) * (_radius - 5f);
                candidate = _centre + Rng.OnUnitCircleXZ() * d;
            }

            // Relaxed after many failures so a crowded map still gets its chests.
            float spacing = attempt < 32 ? MinSpacing : MinSpacing * 0.5f;
            if (TryValidate(space, candidate, playerPos, spacing, out Vector3 spot))
            {
                Spawn(r, spot, playerPos);
                return;
            }
        }

        GD.Print("[chest] no valid spot found; request dropped");
    }

    private bool TryValidate(PhysicsDirectSpaceState3D space, Vector3 candidate, Vector3 playerPos,
                             float spacing, out Vector3 spot)
    {
        spot = default;
        Vector3 flat = new(candidate.X, _centre.Y, candidate.Z);
        if (new Vector2(flat.X - _centre.X, flat.Z - _centre.Z).Length() > _radius - 3f) return false;

        Vector3? hit = Ground(space, flat, out Vector3 normal);
        if (hit is not { } g) return false;
        if (normal.Y < 0.8f) return false;                          // slope or wall face
        if (Arena.Current.Underwater)
        {
            if (g.Y > UnderwaterTerrain.WaterCeiling - 2f || g.Y < -30f) return false;
        }
        else
        {
            if (g.Y - _centre.Y > MaxPerchHeight) return false;
            if (g.Y - _centre.Y < -2f) return false;
        }

        // Headroom: something solid just above means it is under an overhang or
        // wedged in geometry.
        if (Ray(space, g + Vector3.Up * 0.1f, g + Vector3.Up * 1.6f, out _, out _)) return false;

        Vector3 dp = g - playerPos; dp.Y = 0f;
        if (dp.Length() < 5f) return false;

        foreach (Chest c in _chests)
        {
            if (!Usable(c)) continue;
            Vector3 d = c.GlobalPosition - g; d.Y = 0f;
            if (d.Length() < spacing) return false;
        }

        if (OverlapsObstacle(g) || MapBlockers.Inside(g with { Y = -1f }, 1.5f)) return false;
        spot = g;
        return true;
    }

    private void Spawn(Request r, Vector3 at, Vector3 facePoint)
    {
        if (_map is null) return;
        var chest = new Chest(r.Tier, r.Free) { SpawnedAt = _clock };
        _map.AddChild(chest);
        chest.GlobalPosition = at;

        // Face the lock toward the player when there is one to face, so the
        // first look at a chest is at its front.
        Vector3 to = facePoint - at; to.Y = 0f;
        chest.Rotation = new Vector3(0f, to.LengthSquared() > 0.01f ? MathF.Atan2(to.X, to.Z) : Rng.Range(0f, Mathf.Tau), 0f);
        if (Arena.Current.Underwater) { Visuals.ReefActorSupport.Ground(chest, Chest.HalfExtents); at = chest.GlobalPosition; }

        _chests.Add(chest);
        Spawned++;
        if (Net.Coop.IsHost) Net.NetSession.Instance?.OnChestSpawned(chest);
        EventBus.Publish(new ChestSpawned(at, r.Tier, r.Free));
        if (r.Free)
            EventBus.Publish(new PopupRequested(at + Vector3.Up * 2.4f, "FREE CHEST", new Color(0.55f, 1f, 0.65f), 1.4f));
        GD.Print($"[chest] spawned tier={r.Tier} free={r.Free} at=({at.X:0.0},{at.Y:0.0},{at.Z:0.0}) t={_clock:0}s");
    }

    private Vector3 ClampToArea(Vector3 p)
    {
        var flat = new Vector2(p.X - _centre.X, p.Z - _centre.Z);
        float max = _radius - 3f;
        if (flat.Length() > max) flat = flat.Normalized() * max;
        return new Vector3(_centre.X + flat.X, p.Y, _centre.Z + flat.Y);
    }

    /// <summary>Top of whatever solid world geometry is under (x, z).</summary>
    private Vector3? Ground(PhysicsDirectSpaceState3D space, Vector3 at, out Vector3 normal)
    {
        Vector3 from = new(at.X, _centre.Y + MaxPerchHeight + 25f, at.Z);
        Vector3 to = new(at.X, _centre.Y - (Arena.Current.Underwater ? 40f : 10f), at.Z);
        return Ray(space, from, to, out Vector3 point, out normal) ? point : null;
    }

    private bool Ray(PhysicsDirectSpaceState3D space, Vector3 from, Vector3 to, out Vector3 point, out Vector3 normal)
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

    /// <summary>
    /// Records the boxes of the map's visible props. Some scenery is mesh only,
    /// with no collider for a ray to find, and a chest half inside a pillar
    /// looks broken whether or not anything can collide with it. Very large
    /// meshes (the floor, the boundary) and flat ones (floor markings) are
    /// skipped — they are the ground, not obstacles on it.
    /// </summary>
    private void CollectObstacles(Node root)
    {
        _obstacles.Clear();
        var stack = new Stack<Node>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            Node n = stack.Pop();
            if (n is Chest) continue;
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

    private bool OverlapsObstacle(Vector3 g)
    {
        Vector3 half = Chest.HalfExtents + new Vector3(0.5f, 0f, 0.5f);
        var footprint = new Aabb(g - new Vector3(half.X, 0f, half.Z) + Vector3.Up * 0.05f,
                                 new Vector3(half.X * 2f, 1.2f, half.Z * 2f));
        foreach (Aabb box in _obstacles)
        {
            // Standing on top of a prop (a crate with a collider) is a perch,
            // not an overlap.
            if (g.Y >= box.End.Y - 0.05f) continue;
            if (footprint.Intersects(box)) return true;
        }
        return false;
    }

    // ------------------------------------------------------------- helpers

    private static bool Usable(Chest c) => IsInstanceValid(c) && !c.IsQueuedForDeletion() && !c.IsOpen;

    private Chest? Focus(Vector3 at)
    {
        Chest? best = null;
        float bestSq = float.MaxValue;
        foreach (Chest c in _chests)
        {
            if (!Usable(c) || !InReach(c, at)) continue;
            float sq = (c.GlobalPosition - at).LengthSquared();
            if (sq < bestSq) { best = c; bestSq = sq; }
        }
        return best;
    }

    /// <summary>The bound key's name, so the prompt follows a rebind.</summary>
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
