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

        if (_boss is { } stray && _enemies.ReturnStrayBoss(stray, _player.GlobalPosition) is { } back)
            Log($"boss was off the map; returned to {back.X:0},{back.Z:0}");

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
        Log($"phase {_index + 1} boss '{_bossDef.DisplayName}' arrives ({_boss.MaxHealth:0} hp)");
        EventBus.Publish(new PhaseBossArrived(_index + 1, _bossDef.DisplayName));
        if (Net.Coop.IsHost) Net.NetSession.Instance?.SendBoss(_boss);
    }

    /// <summary>This map's boss from the run's plan (<see cref="BossPlan"/>).
    /// Asked only by whoever runs the phase flow; clients are sent the result.</summary>
    private EnemyDef? ChooseBoss() => PlannedBosses[Mathf.Clamp(_index, 0, _stages.Count - 1)];

    /// <summary>The boss of each map on the route, in order.</summary>
    public EnemyDef?[] PlannedBosses => BossPlan.For(_stages, _roster, _run.Seed ^ ((ulong)RouteLoop * 0x9E3779B97F4A7C15UL));

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
            // Endless: no win, no swarm. Raise the tier and go round again on a
            // new set of maps, through a portal and rest room like any other map.
            // A late joiner replaying this already holds the host's count.
            if (!_catchingUp)
            {
                Meta.DifficultyTier next = Meta.EndlessMode.NextLoop();
                Log($"endless loop {Meta.EndlessMode.Loop} cleared; next tier {next.Id}");
                EventBus.Publish(new PopupRequested(_player.GlobalPosition + new Vector3(0f, 3.2f, 0f),
                    Meta.Loc.F("LOOP {0} CLEARED  ·  {1}", Meta.EndlessMode.Loop, Meta.Loc.T(next.Name)), new Color(1f, 0.55f, 0.3f), 1.6f));
            }
            _loopRestart = true;   // the portal and rest room lead to the next loop's first map
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
