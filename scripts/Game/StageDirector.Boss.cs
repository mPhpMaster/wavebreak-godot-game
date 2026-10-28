using System.Collections.Generic;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Core;
using Wavebreak.Data;

namespace Wavebreak.Game;

public sealed partial class StageDirector
{
    // --------------------------------------------------------------- boss

    private void UpdateBoss()
    {
        if (_boss is null)
        {
            if (_run.PhaseSeconds >= _retryAt) SendBoss();
        }
        else if (!_boss.IsAlive)
        {
            // Health is zeroed only by a kill; a despawn leaves it standing. A
            // boss that left without dying is sent again rather than waited on.
            if (_boss.Health <= 0f) OnBossKilled();
            else
            {
                Log("phase boss left the field without dying; sending it again");
                _boss = null;
                _retryAt = _run.PhaseSeconds + 1f;
            }
            return;
        }

        // One line every 30 s of a boss fight: how far along it is and why. A
        // fight that stalls is otherwise invisible in a headless run's log.
        if (_boss is { } b && _run.PhaseSeconds >= _nextBossReport)
        {
            _nextBossReport = _run.PhaseSeconds + 30f;
            Log($"boss {b.HealthFraction * 100f:0}% at {b.GlobalPosition.DistanceTo(_player.GlobalPosition):0}m, " +
                $"{_enemies.AliveCount} alive, overtime {OvertimeSeconds:0}s");
        }

        // The crowd thins while the boss is up, so it can actually become the
        // nearest target. Overtime hardens what does spawn but keeps the crowd
        // thin: letting it regrow hid the boss behind 150 bodies again, and one
        // verification run spent eight minutes on a 2,800 hp boss -- a feedback
        // loop that makes a slow kill slower, which is a soft lock in all but name.
        float steps = OvertimeSeconds / 30f;
        ApplyScaling(1f + OvertimeStepPer30s * steps, BossFightCrowd);
    }

    private void SendBoss()
    {
        _bossDef ??= ChooseBoss();
        if (_bossDef is null)
        {
            // A roster with no bosses at all: treat the phase as cleared so the
            // run still moves on instead of waiting for something that cannot come.
            Log("no boss in the roster; phase clears on its timer");
            OnBossKilled();
            return;
        }

        _boss = _enemies.SpawnBoss(_bossDef, Stage.BossHealthScale);
        if (_boss is null)
        {
            _retryAt = _run.PhaseSeconds + 1f;
            return;
        }
        _fought.Add(_bossDef.Id);
        Log($"phase {_index + 1} boss '{_bossDef.DisplayName}' arrives ({_boss.MaxHealth:0} hp)");
        EventBus.Publish(new PhaseBossArrived(_index + 1, _bossDef.DisplayName));
        if (Net.Coop.IsHost) Net.NetSession.Instance?.SendBoss(_boss);
    }

    /// <summary>
    /// The strongest boss available now that this run has not met yet.
    ///
    /// "Available" is the boss's own unlock time against the run clock, the
    /// same rule the old timer used, so a boss tuned to appear late still
    /// appears late. If nothing is unlocked yet (shortened test phases), the
    /// earliest-unlocking unfought boss stands in. Repeats only when every
    /// eligible boss has been fought.
    /// </summary>
    private EnemyDef? ChooseBoss()
    {
        if (Stage.BossId is { Length: > 0 } wanted)
            foreach (EnemyDef def in _roster)
                // Reef's explicit encounter must not be replaced by a generic roster unlock gate.
                if (def.IsBoss && def.Id == wanted
                    && (wanted == Visuals.ReefLeviathanModels.Id || def.UnlockAtSeconds <= _run.Seconds)
                    && EnemyMapEligibility.Allows(def, Stage)) return def;

        EnemyDef? best = null, bestFresh = null, earliest = null, earliestFresh = null;
        foreach (EnemyDef def in _roster)
        {
            if (def is null || !def.IsBoss || !EnemyMapEligibility.Allows(def, Stage)) continue;
            bool fresh = !_fought.Contains(def.Id);

            if (earliest is null || def.UnlockAtSeconds < earliest.UnlockAtSeconds) earliest = def;
            if (fresh && (earliestFresh is null || def.UnlockAtSeconds < earliestFresh.UnlockAtSeconds)) earliestFresh = def;

            if (def.UnlockAtSeconds > _run.Seconds) continue;
            if (Stronger(def, best)) best = def;
            if (fresh && Stronger(def, bestFresh)) bestFresh = def;
        }
        return bestFresh ?? earliestFresh ?? best ?? earliest;
    }

    private static bool Stronger(EnemyDef a, EnemyDef? b)
        => b is null || a.Cost > b.Cost || (a.Cost == b.Cost && a.Health > b.Health);

    private void OnBossKilled()
    {
        Log($"phase {_index + 1} boss down");
        if (Net.Coop.IsHost) Net.NetSession.Instance?.SendBossDown();
        _boss = null;
        BossesDefeated++;
        ApplyScaling(1f);
        // The boss drops a lot; collect it for the player rather than making the
        // walk to the portal a detour through the boss's corpse.
        Pickup.AttractAll();

        if (_index >= _stages.Count - 1 && Meta.EndlessMode.Active)
        {
            // Endless: no win, no swarm. Raise the tier and go round again from
            // map one, through a portal and rest room like any other map.
            Meta.DifficultyTier next = Meta.EndlessMode.NextLoop();
            Log($"endless loop {Meta.EndlessMode.Loop} cleared; next tier {next.Id}");
            EventBus.Publish(new PopupRequested(_player.GlobalPosition + new Vector3(0f, 3.2f, 0f),
                Meta.Loc.F("LOOP {0} CLEARED  ·  {1}", Meta.EndlessMode.Loop, Meta.Loc.T(next.Name)), new Color(1f, 0.55f, 0.3f), 1.6f));
            _loopRestart = true;   // the portal and rest room lead back to map one
        }
        else if (_index >= _stages.Count - 1)
        {
            Won = true;
            bool swarm = Game.FinalSwarm.Enabled;
            EventBus.Publish(new FinalBossDefeated(_run.Seconds, swarm));
            if (swarm)
            {
                Log("run won");
                Swarm = new FinalSwarm(_run, _enemies, Stage);
                State = PhaseState.FinalSwarm;
                Swarm.Begin();
                return;
            }
            State = PhaseState.Victory;
            _victoryIn = VictoryDelay;
            return;
        }

        _portal = new Portal();
        _root.AddChild(_portal);
        _portal.GlobalPosition = Arena.Centre;
        _portalLeft = PortalSeconds;
        _portalSyncIn = 0f;
        _portal.Countdown = _portalLeft;
        State = PhaseState.PortalOpen;
        Log($"portal open at {Arena.Centre}");
        EventBus.Publish(new PortalOpened(_index + 1, Arena.Centre));
    }

    /// <summary>A body that leaves the floor — over a wall, through a seam —
    /// is put back on the centre pad. Cheap insurance against the one bug that
    /// would end a run without a death or a win.</summary>
    private void GuardFall()
    {
        if (Arena.Current.Underwater && ReefCavePath.Floor(_player.GlobalPosition) > WaterSurfaceHeight.At(_player.GlobalPosition) - 2f)
            return; // Coastal cliffs are lateral obstacles, not a missing submerged floor.
        float floor = Arena.Current.Underwater ? ReefCavePath.Floor(_player.GlobalPosition) - 8f : -20f;
        if (_player.GlobalPosition.Y > floor) return;
        Log("player fell out of the world; returned to centre");
        _player.Teleport(Arena.Centre + Vector3.Up * 0.3f);
        _camera.SnapToTarget();
    }

    private void Log(string message) => GD.Print($"[stage] t={_run.Clock} {message}");
}
