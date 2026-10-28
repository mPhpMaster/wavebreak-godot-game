using System.Collections.Generic;
using Godot;
using Wavebreak.Combat;

namespace Wavebreak.Actors;

/// <summary>
/// Co-op halves of the enemy manager.
///
/// Host: the director and the AI run exactly as in single-player, except that
/// each enemy chases the nearest living player (local or remote) instead of
/// the one local player, spawns ring around a living player in turn, and
/// strays are culled by distance to the nearest player.
///
/// Client: no director and no AI. Bodies are puppets the host spawns, moves
/// and kills (<see cref="SpawnPuppet"/>, <see cref="Enemy.StepPuppet"/>). Enemy
/// shots and blasts the host mirrors still fly here and are tested against
/// the local player, who owns their own health.
/// </summary>
public sealed partial class EnemyManager
{
    private readonly List<(Node3D Node, IDamageable Hit)> _targets = new();
    private int _anchorTurn;

    /// <summary>Client: put the host's enemy <paramref name="netId"/> on the field.</summary>
    internal Enemy? SpawnPuppet(int defIndex, int netId, Vector3 at, float yaw, float maxHealth, float health)
    {
        if (defIndex < 0 || defIndex >= _pools.Count) return null;
        Enemy? enemy = _pools[defIndex].Get();
        if (enemy is null) return null;
        enemy.PoolIndex = defIndex;
        enemy.Despawned ??= _despawnHandler;
        enemy.Random = _rng;
        enemy.ConfigurePuppet(_defs[defIndex], netId, at, yaw, maxHealth, health);
        Register(enemy);
        return enemy;
    }

    /// <summary>Client physics step: puppets ease toward the host's transforms,
    /// mirrored attacks fly at the local player.</summary>
    private void PuppetPhysics(float dt)
    {
        for (int i = 0; i < _alive.Count; i++)
        {
            Enemy e = _alive[i];
            if (!e.IsDead) e.StepPuppet(dt);
        }
        bool hasPlayer = PlayerRef.TryGet(out Node3D playerNode, out IDamageable? playerHit);
        EnemyAttacks.Tick(dt, hasPlayer ? playerNode.GlobalPosition : Vector3.Zero, hasPlayer ? playerHit : null);
    }

    /// <summary>Host: refresh the living players once per physics step.</summary>
    private void RefreshTargets()
    {
        _targets.Clear();
        Net.NetSession.Instance?.FillTargets(_targets);
    }

    /// <summary>Host: nearest living player to <paramref name="p"/>.</summary>
    private bool NearestTarget(Vector3 p, out Vector3 feet, out IDamageable? hit)
    {
        feet = Vector3.Zero;
        hit = null;
        float best = float.MaxValue;
        for (int i = 0; i < _targets.Count; i++)
        {
            if (!GodotObject.IsInstanceValid(_targets[i].Node) || !_targets[i].Hit.IsAlive) continue;
            Vector3 f = _targets[i].Node.GlobalPosition;
            float d = Game.Arena.Current.Underwater ? f.DistanceSquaredTo(p)
                : (f.X - p.X) * (f.X - p.X) + (f.Z - p.Z) * (f.Z - p.Z);
            if (d >= best) continue;
            best = d;
            feet = f;
            hit = _targets[i].Hit;
        }
        return hit is not null;
    }

    /// <summary>Host: where this spawn burst rings — each living player in turn,
    /// so a player who wanders off still has company.</summary>
    private Vector3 SpawnAnchor(Vector3 fallback)
    {
        if (_targets.Count == 0) return fallback;
        Vector3 center = Vector3.Zero;
        int count = 0;
        foreach (var target in _targets)
            if (GodotObject.IsInstanceValid(target.Node) && target.Hit.IsAlive)
            { center += target.Node.GlobalPosition; count++; }
        return count > 0 ? center / count : fallback;
    }

    /// <summary>Host: cull strays far from every player.</summary>
    private void CullStraysCoop()
    {
        if (_alive.Count == 0 || _targets.Count == 0) return;
        float maxSq = DespawnRadius * DespawnRadius;
        int batch = System.Math.Min(CullBatch, _alive.Count);
        for (int scanned = 0; scanned < batch; scanned++)
        {
            if (_alive.Count == 0) return;
            _cullCursor = (_cullCursor + 1) % _alive.Count;
            Enemy e = _alive[_cullCursor];
            if (!e.IsAlive || e.IsBoss) continue;
            Vector3 p = e.GlobalPosition;
            bool near = false;
            for (int i = 0; i < _targets.Count && !near; i++)
            {
                Vector3 d = _targets[i].Node.GlobalPosition - p;
                d.Y = 0f;
                near = d.LengthSquared() <= maxSq;
            }
            if (!near) e.Despawn();
        }
    }
}
