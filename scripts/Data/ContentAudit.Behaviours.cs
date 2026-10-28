using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Combat;
using Wavebreak.Core;
using Wavebreak.Game;
using Wavebreak.Progression;
using Wavebreak.Stats;

namespace Wavebreak.Data;

public static partial class ContentAudit
{
    // ------------------------------------------------------------ behaviours

    /// <summary>Numbers a behaviour needs to be fair: a tell long enough to
    /// read, shots slow enough to dodge, ranges that make the move reachable.</summary>
    private static void AuditBehaviourData(EnemyDef def)
    {
        string id = def.Id;
        switch (def.Behavior)
        {
            case EnemyBehavior.Shooter:
                Check(def.AbilityRange >= 4f && def.AbilityCount >= 1 && def.WindUp >= 0.3f, $"shooter {id} range/count/wind-up");
                // The player walks at 6 m/s; anything much faster cannot be
                // side-stepped from range and stops being a dodge.
                Check(def.AbilitySpeed > 2f && def.AbilitySpeed <= 12f, $"shooter {id} shot speed {def.AbilitySpeed} not dodgeable");
                break;
            case EnemyBehavior.Charger:
                Check(def.AbilityRange >= 4f && def.WindUp >= 0.4f && def.AbilitySpeed > def.MoveSpeed * 2f, $"charger {id} range/wind-up/speed");
                break;
            case EnemyBehavior.Exploder:
                Check(def.AbilityRadius > 0.5f && def.WindUp >= 0.5f && def.AbilityRange > 0.5f, $"exploder {id} radius/fuse");
                break;
            case EnemyBehavior.Leaper:
                Check(def.AbilityRange >= 4f && def.AbilityRadius > 0.5f && def.WindUp >= 0.3f, $"leaper {id} range/radius/wind-up");
                break;
        }

        if (def.SplitCount > 0)
            Check(Catalog.EnemyById(def.SplitInto) is { IsBoss: false } && def.SplitCount <= 8, $"splitter {id} child {def.SplitInto}");

        if (!def.DirectorSpawns && id == Visuals.ReefGuardianModels.Id)
            Check(def.UnderwaterOnly && !def.IsBoss, "cave guardian is a scripted underwater encounter (verified by --check-guardian)");
        else if (!def.DirectorSpawns)
            Check(Catalog.Enemies.Any(e => e.SplitInto == id || e.BossMoves.Any(m => m.SummonId == id)),
                  $"summon-only {id} is never summoned or split into");

        if (Visuals.CityGiantModels.IsGiant(def))
        {
            Check(def.Height >= 26f && def.Radius >= 3f, $"city giant {id} size");
            return; // Custom 3D encounters are exercised by --check-city.
        }

        if (!def.IsBoss)
        {
            Check(def.BossTier == 0 && def.BossMoves.Count == 0, $"non-boss {id} has boss data");
            return;
        }

        Check(def.BossTier is >= 1 and <= 3, $"boss {id} tier {def.BossTier}");
        Check(def.BossMoves.Count >= 3, $"boss {id} has {def.BossMoves.Count} moves (want 3+)");
        Check(def.BossMoves.Any(m => m.MinPhase >= 2), $"boss {id} has no phase-two move");
        Check(def.BossMoves.Any(m => m.MinPhase <= 1), $"boss {id} has no phase-one move");
        foreach (BossAttack m in def.BossMoves)
        {
            Check(m.WindUp >= 0.4f && m.Weight > 0f, $"boss {id} {m.Move}: wind-up {m.WindUp} too short to read");
            if (m.Move == BossMove.Summon)
                Check(Catalog.EnemyById(m.SummonId) is { IsBoss: false }, $"boss {id} summons unknown '{m.SummonId}'");
            if (m.Move is BossMove.RadialBurst or BossMove.Barrage)
                Check(m.Speed > 2f && m.Speed <= 12f, $"boss {id} {m.Move} shot speed {m.Speed} not dodgeable");
        }
    }

    /// <summary>
    /// Every behaviour and every boss move, fought for real: the enemy is
    /// configured and stepped through <see cref="Enemy.Step"/>, and its attacks
    /// travel through <see cref="EnemyAttacks"/> at a dummy, far below the
    /// arena. A move passes when it *connects* (counted by EnemyAttacks, so a
    /// contact swing cannot pass for it) — or, for a summon, when the adds
    /// exist. This is the claim "the boss can slam" made by the code that slams.
    /// </summary>
    private static void AuditBehaviours(WeaponSystem system)
    {
        Vector3 feet = system.MuzzlePosition + new Vector3(0f, -200f, 0f);
        EnemyAttacks.Clear();

        foreach (EnemyDef def in Catalog.Enemies)
        {
            if (Visuals.CityGiantModels.IsGiant(def)) continue;
            if (def.IsBoss)
            {
                for (int i = 0; i < def.BossMoves.Count; i++) ProbeMove(system, def, i, feet);
                ProbePhase(system, def, feet);
            }
            // Healers never attack; burrowers and blinkers hit by contact, which
            // the director and kill audits cover. The probe counts ranged hits only.
            else if (def.Behavior is not (EnemyBehavior.Chaser or EnemyBehavior.Healer
                                          or EnemyBehavior.Burrower or EnemyBehavior.Blinker))
            {
                ProbeMove(system, def, -1, feet);
            }
            if (def.SplitCount > 0) ProbeSplit(system, def, feet);
        }

        EnemyAttacks.Clear();
        EnemyManager.Instance?.DespawnBelow(-100f);
        AbilityCounts.Clear();
        ConnectCounts.Clear();
    }

    private static void ProbeMove(WeaponSystem system, EnemyDef def, int moveIndex, Vector3 feet)
    {
        BossAttack? move = moveIndex >= 0 ? def.BossMoves[moveIndex] : null;
        float distance = move?.Move switch
        {
            BossMove.Slam => MathF.Min(2.5f, move.Radius * 0.6f),
            BossMove.Charge => Mathf.Clamp(move.MinRange + 2f, 5f, move.Radius - 2f),
            BossMove.RadialBurst => 4f,
            null => def.Behavior == EnemyBehavior.Shooter ? def.AbilityRange : def.AbilityRange * 0.6f,
            _ => 7f,
        };

        var dummy = new FakeTarget(feet + new Vector3(0f, 0.9f, 0f));
        var enemy = new Enemy { Name = $"Audit_{def.Id}_{moveIndex}" };
        system.AddChild(enemy);
        enemy.Configure(def, 1f, 1f, 1f);
        enemy.Spawn(feet + new Vector3(distance, 0f, 0f));
        // Ability ready at once: a charger that walks into contact range
        // before its cooldown expires never charges, and that is a property
        // of the stage, not of the behaviour under test.
        enemy.ForceMove(moveIndex, delay: 0f);

        int connects = EnemyAttacks.Connects;
        int alive = EnemyManager.Instance?.AliveCount ?? 0;
        bool summon = move?.Move == BossMove.Summon;
        float seconds = 0f;
        bool ok = false;
        const float dt = 1f / 60f;
        for (int frame = 0; frame < 900 && !ok; frame++)
        {
            if (enemy.IsAlive) enemy.Step(dt, feet, dummy, Vector3.Zero);
            EnemyAttacks.Tick(dt, feet, dummy);
            seconds += dt;
            ok = summon ? (EnemyManager.Instance?.AliveCount ?? 0) > alive : EnemyAttacks.Connects > connects;
        }

        string what = move is null ? def.Behavior.ToString() : move.Move.ToString();
        if (enemy.IsAlive) enemy.Despawn();
        enemy.QueueFree();
        EnemyAttacks.Clear();
        EnemyManager.Instance?.DespawnBelow(-100f);

        Check(ok, $"{def.Id} {what} never {(summon ? "summoned" : "connected")} (from {distance:0.#}m)");
        Line($"behaviour {def.Id,-18} {what,-12} {(ok ? "ok" : "FAILED")} after {seconds:0.00}s from {distance:0.#}m dummyDamage={dummy.Damage:0}");
    }

    private static void ProbePhase(WeaponSystem system, EnemyDef def, Vector3 feet)
    {
        var enemy = new Enemy { Name = $"Audit_{def.Id}_phase" };
        system.AddChild(enemy);
        enemy.Configure(def, 1f, 0f, 1f);
        enemy.Spawn(feet + new Vector3(30f, 0f, 0f));
        int before = enemy.Phase;
        enemy.TakeDamage(new DamageInfo { Amount = enemy.MaxHealth * 0.55f, Source = "audit" });
        int after = enemy.Phase;
        enemy.Despawn();
        enemy.QueueFree();
        Check(before == 1 && after == 2, $"boss {def.Id} phase {before}->{after} at 45% health");
    }

    private static void ProbeSplit(WeaponSystem system, EnemyDef def, Vector3 feet)
    {
        if (EnemyManager.Instance is not { } em) { Check(false, "split: no manager"); return; }
        var enemy = new Enemy { Name = $"Audit_{def.Id}_split" };
        system.AddChild(enemy);
        enemy.Configure(def, 1f, 0f, 1f);
        enemy.Spawn(feet + new Vector3(-30f, 0f, 0f));
        int before = em.AliveCount;
        enemy.TakeDamage(new DamageInfo { Amount = enemy.MaxHealth * 2f, Source = "audit" });
        int born = em.AliveCount - before;
        enemy.QueueFree();
        em.DespawnBelow(-100f);
        Check(born == def.SplitCount, $"splitter {def.Id} produced {born}, expected {def.SplitCount}");
        Line($"behaviour {def.Id,-18} Split        {(born == def.SplitCount ? "ok" : "FAILED")} {born}x {def.SplitInto}");
    }

}
