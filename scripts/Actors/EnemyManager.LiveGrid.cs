using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Combat;
using Wavebreak.Core;
using Wavebreak.Data;

namespace Wavebreak.Actors;

public sealed partial class EnemyManager
{
    // ------------------------------------------------------ live list churn

    private Enemy NewEnemy()
    {
        Enemy enemy = new() { Name = "Enemy" };
        enemy.Despawned = _despawnHandler ??= OnEnemyDespawned;
        return enemy;
    }

    private void PrewarmPool(int index, int count)
    {
        Pool<Enemy> pool = _pools[index];
        EnemyDef def = _defs[index];

        for (int i = 0; i < count; i++)
        {
            Enemy? enemy = pool.Get();
            if (enemy is null) break;
            enemy.PoolIndex = index;
            enemy.Despawned ??= _despawnHandler;
            // Configure now, so the mesh and material work happens here rather
            // than in the middle of the first wave.
            enemy.Configure(def, 1f, 1f, 1f);
            pool.Release(enemy);
        }
    }

    private void Register(Enemy enemy)
    {
        enemy.AliveIndex = _alive.Count;
        _alive.Add(enemy);
        _living++;
        _gridValid = false;
    }

    private void OnEnemyDespawned(Enemy enemy)
    {
        if (enemy.IsPooled) return;                  // already parked
        _pendingRelease.Add(enemy);
        if (_living > 0) _living--;
    }

    /// <summary>
    /// Return dead enemies to their pools and compact the live list.
    ///
    /// Compaction rather than swap-removal per entry: several enemies can die in
    /// one frame, and each swap would invalidate indices the other pending
    /// entries recorded. One pass over a list capped at <see cref="MaxAlive"/> is
    /// cheaper than getting that bookkeeping right.
    /// </summary>
    private void Sweep()
    {
        if (_pendingRelease.Count == 0) return;

        for (int i = 0; i < _pendingRelease.Count; i++)
            ReleaseToPool(_pendingRelease[i]);
        _pendingRelease.Clear();

        int write = 0;
        for (int read = 0; read < _alive.Count; read++)
        {
            Enemy e = _alive[read];
            if (!GodotObject.IsInstanceValid(e)) continue;
            if (e.IsPooled) continue;                // parked by the loop above
            _alive[write] = e;
            e.AliveIndex = write;
            write++;
        }
        if (write < _alive.Count) _alive.RemoveRange(write, _alive.Count - write);

        _gridValid = false;
    }

    private void ReleaseToPool(Enemy enemy)
    {
        int index = enemy.PoolIndex;
        if (index >= 0 && index < _pools.Count) _pools[index].Release(enemy);
        else if (GodotObject.IsInstanceValid(enemy)) enemy.QueueFree();
    }

    // ------------------------------------------------------- neighbour grid

    private static long CellKey(int cx, int cz) => ((long)cx << 32) | (uint)cz;

    private static int CellOf(float v) => (int)MathF.Floor(v / CellSize);

    private void RebuildGrid()
    {
        _cellHead.Clear();
        int count = _alive.Count;
        if (_cellNext.Length < count)
            Array.Resize(ref _cellNext, Math.Max(count, _cellNext.Length * 2));

        for (int i = 0; i < count; i++)
        {
            Enemy e = _alive[i];
            if (!e.IsAlive) { _cellNext[i] = -1; continue; }

            Vector3 p = e.GlobalPosition;
            long key = CellKey(CellOf(p.X), CellOf(p.Z));
            _cellNext[i] = _cellHead.TryGetValue(key, out int head) ? head : -1;
            _cellHead[key] = i;
        }
        _gridValid = true;
    }

    /// <summary>
    /// Crowd-avoidance push for the enemy at <paramref name="selfIndex"/>.
    ///
    /// Bounded to <see cref="MaxSeparationNeighbours"/> contributors: in a dense
    /// pile the direction is decided by the nearest few anyway, and the bound is
    /// what stops the cost going quadratic exactly when the frame can least
    /// afford it.
    /// </summary>
    private Vector3 SeparationFor(int selfIndex, Vector3 position)
    {
        if (!_gridValid) return Vector3.Zero;

        Vector3 push = Vector3.Zero;
        int cx = CellOf(position.X);
        int cz = CellOf(position.Z);
        float radius = SeparationRadius;
        float radiusSq = radius * radius;
        int considered = 0;

        for (int ox = -1; ox <= 1; ox++)
        for (int oz = -1; oz <= 1; oz++)
        {
            if (!_cellHead.TryGetValue(CellKey(cx + ox, cz + oz), out int i)) continue;

            while (i >= 0)
            {
                int current = i;
                i = _cellNext[i];
                if (current == selfIndex) continue;

                Enemy other = _alive[current];
                if (!other.IsAlive) continue;

                Vector3 delta = position - other.GlobalPosition;
                delta.Y = 0f;
                float distSq = delta.LengthSquared();
                if (distSq >= radiusSq) continue;

                if (distSq < 1e-5f)
                {
                    // Exactly stacked. Deterministic per-pair offset so the two
                    // separate instead of sitting in a stable draw forever.
                    float angle = (selfIndex * 2.399963f) + current * 0.7f;
                    push += new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle));
                }
                else
                {
                    float dist = MathF.Sqrt(distSq);
                    push += delta / dist * (1f - dist / radius);
                }

                if (++considered >= MaxSeparationNeighbours) return push;
            }
        }

        return push;
    }

    private Enemy? NearestInGrid(Vector3 p, float maxRange)
    {
        int cx = CellOf(p.X);
        int cz = CellOf(p.Z);
        int span = Math.Max(1, (int)MathF.Ceiling(maxRange / CellSize));

        Enemy? best = null;
        float limitSq = maxRange * maxRange;
        float bestSq = limitSq;

        for (int ox = -span; ox <= span; ox++)
        for (int oz = -span; oz <= span; oz++)
        {
            if (!_cellHead.TryGetValue(CellKey(cx + ox, cz + oz), out int i)) continue;

            while (i >= 0)
            {
                Enemy e = _alive[i];
                i = _cellNext[i];
                if (!e.IsAlive) continue;

                float sq = (e.AimPoint - p).LengthSquared();
                if (sq >= limitSq) continue;
                sq *= PriorityFactor(e);
                if (sq >= bestSq) continue;
                bestSq = sq;
                best = e;
            }
        }

        return best;
    }
}
