using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Combat;
using Wavebreak.Core;
using Wavebreak.Data;

namespace Wavebreak.Actors;

public sealed partial class EnemyManager
{
    // ------------------------------------------------------- phase control
    // Hooks for Game.StageDirector, which runs the run as a sequence of maps.
    // Additive on purpose: with none of these touched the director behaves
    // exactly as it did with a single arena and a boss timer.

    /// <summary>When false the director never spawns a boss on its own timer;
    /// whoever cleared it is expected to call <see cref="SpawnBoss"/>.</summary>
    public bool TimedBosses { get; set; } = true;

    /// <summary>Developer panel "freeze AI": enemies hold still and the director stops spawning.</summary>
    public static bool DevFrozen { get; set; }

    /// <summary>Per-phase multipliers on top of the time curve. 1 = no change.</summary>
    public float PhaseHealthScale { get; set; } = 1f;
    public float PhaseDamageScale { get; set; } = 1f;
    public float PhaseSpeedScale { get; set; } = 1f;

    /// <summary>Fraction of the normal live cap to allow. Lowered during a phase
    /// boss fight: weapons aim at the nearest enemy, so with a full crowd around
    /// the player the boss is almost never the target and the fight cannot end.</summary>
    public float LiveCapScale { get; set; } = 1f;

    /// <summary>Multiplier on credit income and the credit bank: how fast the
    /// crowd refills. Raised by the Final Swarm. 1 = no change.</summary>
    public float SpawnRateScale { get; set; } = 1f;

    /// <summary>The current map's enemy list (StageDef.EnemyIds), or null for
    /// the whole roster. Filters ordinary and elite spawns, never bosses.</summary>
    public HashSet<string>? StageEnemyIds { get; set; }

    /// <summary>
    /// Put a specific boss on the field now, on the spawn ring around the
    /// player, and announce it. Returns null when it could not be placed this
    /// frame (pool exhausted, no floor found); callers should retry.
    /// </summary>
    public Enemy? SpawnBoss(EnemyDef def, float healthScale = 1f)
    {
        int index = _defs.IndexOf(def);
        if (index < 0 || !PlayerRef.TryGet(out Node3D player, out _)) return null;
        UpdateDifficulty(_runSeconds);
        // Scaled for this one spawn only; the next Tick recomputes it anyway.
        // A boss takes its own co-op row in place of the crowd's.
        float hp = DifficultyHp;
        int team = Net.CoopScaling.Connected;
        DifficultyHp = hp * MathF.Max(0.01f, healthScale) * Net.CoopScaling.BossHealth(team) / Net.CoopScaling.Health(team);
        RefreshTargets();
        Enemy? boss = SpawnOne(index, Net.Coop.Active ? SpawnAnchor(player.GlobalPosition) : player.GlobalPosition);
        DifficultyHp = hp;
        if (boss is not null)
            Announce(BossTitle(def), new Color(1f, 0.25f, 0.25f), 1.7f);
        return boss;
    }

    /// <summary>
    /// Put a boss that is well past the map's edge back on the spawn ring
    /// around <paramref name="around"/>. One 1.6.0 verification run lost a
    /// phase boss 1.7 km off the map and spent four minutes walking it back.
    /// Host / single player only; returns where it went, or null.
    /// </summary>
    public Vector3? ReturnStrayBoss(Enemy boss, Vector3 around)
    {
        float limit = Game.Arena.Radius + 25f;
        Vector3 p = boss.GlobalPosition;
        if (Net.Coop.IsClient || !boss.IsAlive || boss.Def is not { } def || Game.Arena.Radius <= 0f
            || p.X * p.X + p.Z * p.Z <= limit * limit
            || around.X * around.X + around.Z * around.Z > limit * limit) return null;   // the player is on the map
        if (Net.Coop.IsHost) around = SpawnAnchor(around);
        bool found = def.Id == Visuals.ReefLeviathanModels.Id
            ? TryLeviathanSpawn(def, around, out Vector3 point)
            : TryFindSpawnPoint(around, out point);
        if (!found) return null;
        boss.GlobalPosition = point;
        boss.Velocity = Vector3.Zero;
        return point;
    }

    /// <summary>
    /// Remove every enemy without a kill and empty the credit bank, for a map
    /// change. Unlike <see cref="Reset"/> it keeps the director's wave clock,
    /// so arriving on a new map does not replay the opening elite schedule.
    /// <paramref name="breather"/> is how long before the first spawn on the
    /// new map, so the title card is read in peace.
    /// </summary>
    /// <summary>Set while the players are in the rest room: nothing may spawn
    /// (<see cref="SpawnAt"/> refuses) and stragglers are removed each tick.</summary>
    public bool Quiet { get; set; }

    public void ClearField(float breather)
    {
        for (int i = 0; i < _alive.Count; i++)
        {
            Enemy e = _alive[i];
            if (GodotObject.IsInstanceValid(e) && e.IsAlive) e.Despawn();
        }
        Sweep();
        _credits = 0f;
        _spawnTimer = MathF.Max(0f, breather);
        _eliteWaveUntil = -1f;
        // The floor height learned on the old map means nothing on the new one.
        _groundKnown = false;
        _lastGroundY = 0f;
        _navCheckAt = 0f;
        _gridValid = false;
    }
}

