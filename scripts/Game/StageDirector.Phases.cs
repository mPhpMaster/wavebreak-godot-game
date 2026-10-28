using System.Collections.Generic;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Core;
using Wavebreak.Data;

namespace Wavebreak.Game;

public sealed partial class StageDirector
{
    // ------------------------------------------------------------- phases

    private void EnterPhase(int index)
    {
        _enemies.Quiet = false;
        bool first = _index < 0;
        _index = index;
        StageDef stage = Stage;
        FreeRestRoom();

        if (_portal is not null && GodotObject.IsInstanceValid(_portal)) _portal.QueueFree();
        _portal = null;
        _boss = null;
        _bossDef = null;

        // Remove before freeing: RemoveChild takes the old floor out of the
        // physics space this instant, so for no frame do two floors overlap.
        if (_arena is not null && GodotObject.IsInstanceValid(_arena))
        {
            _root.RemoveChild(_arena);
            _arena.QueueFree();
        }
        _arena = new Arena(stage);
        _root.AddChild(_arena);
        _root.MoveChild(_arena, 0);

        if (!first)
        {
            // Orbs still on the floor were earned; send them to the player rather
            // than deleting them with the map. They chase across the new map.
            Pickup.AttractAll();
            _enemies.ClearField(ArrivalBreather);
        }
        ApplyScaling(1f);
        _enemies.StageEnemyIds = stage.EnemyIds is { Length: > 0 } ids ? new HashSet<string>(ids) : null;

        PopulateFinds(index);

        _player.Teleport(ReefArrival.Position(Arena.Centre + Vector3.Up * 0.3f + Net.Coop.SpawnOffset, first));
        _camera.SnapToTarget();

        _run.BeginPhase(index + 1, _stages.Count, stage.Id, stage.DisplayName);
        State = PhaseState.Waves;
        Log($"phase {index + 1}/{_stages.Count} '{stage.DisplayName}' begins (boss in {PhaseLength:0}s, " +
            $"hp x{stage.EnemyHealthScale:0.00} dmg x{stage.EnemyDamageScale:0.00} spd x{stage.EnemySpeedScale:0.00})");
        EventBus.Publish(new PhaseStarted(index + 1, _stages.Count, stage.Id, stage.DisplayName, stage.Tagline));
        if (Net.Coop.IsHost) Net.NetSession.Instance?.SendPhase(index);
        if (Net.Coop.InRun) Net.NetSession.Instance?.PlaceRemotes(first);
    }

    /// <summary>
    /// Chests and hidden shrines for the new map, parented to the arena so they
    /// leave with it. Seeded from the run and the phase: the layout of a map is
    /// fixed, but what is worth finding on it moves from run to run.
    /// </summary>
    private void PopulateFinds(int index)
    {
        if (_arena is null) return;
        StageDef stage = Stage;
        ulong salt = (ulong)(index + 1) * 0x9E3779B97F4A7C15UL;
        if (!PerformanceScenario.Reference && stage.Breakables > 0 && (stage.Underwater || stage.Layout == "city"))
            Breakables.PrepareMeshes();

        // Map shrines first, so the chest spawner sees them as obstacles and
        // keeps chests out of the charge rings.
        ShrineSpawner.Populate(_arena, new Rng(_run.Seed ^ 0x5A1E5D00UL ^ salt), stage.InRadius - 6f, stage);
        CuteShrineSpawner.Populate(_arena, new Rng(_run.Seed ^ 0xC07E5D00UL ^ salt), stage.InRadius - 6f, stage);

        int chests = Mathf.Min(stage.Chests, ChestSpawner.MaxWorldChests);
        ChestSpawner.Instance?.Populate(_arena, new Rng(_run.Seed ^ salt), stage.InRadius - 6f, chests);
        Interactables.Instance?.Populate(_arena, new Rng(_run.Seed ^ 0x5A0D5A0DUL ^ salt), stage.InRadius - 6f, stage, index + 1);

        // The spawner rings them at 80-90% of the radius without knowing about
        // terrain; nudge any that landed inside a mound or under a platform.
        foreach (Meta.Shrine shrine in Meta.SecretSpawner.Populate(_arena, new Rng(_run.Seed ^ 0x5EC2E75UL ^ salt), stage.InRadius))
        {
            shrine.Position = _arena.ClearSpotNear(shrine.Position, 2.5f);
            if (stage.Underwater) Visuals.ReefPlacement.Ground(shrine, 2.5f);
        }
    }

    private void ApplyScaling(float overtime, float liveCap = 1f)
    {
        StageDef stage = Stage;
        _enemies.LiveCapScale = liveCap;
        _enemies.PhaseHealthScale = stage.EnemyHealthScale * overtime;
        _enemies.PhaseDamageScale = stage.EnemyDamageScale * overtime;
        _enemies.PhaseSpeedScale = stage.EnemySpeedScale;
    }

}
