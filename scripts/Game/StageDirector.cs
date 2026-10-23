using System.Collections.Generic;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Core;
using Wavebreak.Data;

namespace Wavebreak.Game;

/// <summary>
/// Runs a run as a sequence of phases, each on its own map:
///
///   waves (PhaseSeconds) -> phase boss -> portal -> next map -> ... -> last boss = win
///
/// It owns the arena node and the portal, and tells the enemy director what
/// phase it is in; it never spawns ordinary enemies itself. The time-driven
/// difficulty curve keeps running on the run clock across maps, and each stage
/// adds its own multiplier on top, so a new map is a step up rather than a
/// reset — arriving should feel like a promotion.
///
/// The boss is chosen from the catalog by data — the strongest boss that is
/// unlocked by now and that this run has not fought yet — so a boss added to
/// the roster slots into the phase flow without anyone editing this file.
///
/// It cannot soft-lock: a boss that lives past its grace window starts raising
/// the pressure on everything else, and a boss that vanishes without dying (a
/// pool reset, a fall out of the world) is simply sent again.
/// </summary>
public sealed class StageDirector
{
    public enum PhaseState
    {
        /// <summary>Ordinary waves; the phase timer is counting down to the boss.</summary>
        Waves,
        /// <summary>The phase boss is on the field.</summary>
        Boss,
        /// <summary>Boss dead, portal open, waiting for the player to step in.</summary>
        PortalOpen,
        /// <summary>The last boss died; the win is announced after a short beat.</summary>
        Victory,
        /// <summary>The last boss died and the run is won; an endless, escalating
        /// swarm follows until death (see <see cref="Game.FinalSwarm"/>).</summary>
        FinalSwarm,
    }

    /// <summary>Pressure added per 30 s a boss outlives its grace window, as a
    /// fraction on enemy health and damage. Linear and uncapped on purpose:
    /// overtime must end, one way or the other.</summary>
    private const float OvertimeStepPer30s = 0.12f;

    /// <summary>Seconds of quiet after arriving on a new map before the first
    /// spawn, so the title card is read rather than fought through.</summary>
    private const float ArrivalBreather = 3f;

    /// <summary>Beat between the last boss dying and the results screen: long
    /// enough to see it die and the orbs fly in.</summary>
    private const float VictoryDelay = 3f;

    /// <summary>Share of the normal live cap allowed during a boss fight.</summary>
    private const float BossFightCrowd = 0.35f;

    private readonly Node3D _root;
    private readonly RunState _run;
    private readonly EnemyManager _enemies;
    private readonly Player _player;
    private readonly PlayerCamera _camera;
    private readonly IReadOnlyList<StageDef> _stages;
    private readonly IReadOnlyList<EnemyDef> _roster;
    private readonly HashSet<string> _fought = new();

    private int _index = -1;
    private Arena? _arena;
    private Portal? _portal;
    private Enemy? _boss;
    private EnemyDef? _bossDef;
    private float _bossAt;
    private float _retryAt;
    private float _victoryIn;
    private float _nextBossReport;

    // Verification overrides, read from the command line. Shorter phases and a
    // later start let a capture reach every map in a minute of game time.
    private readonly float _phaseSecondsOverride;
    private readonly int _startPhase;
    private float _openPortalAt;

    public PhaseState State { get; private set; } = PhaseState.Waves;

    /// <summary>True from the moment the last phase boss dies: the run is won,
    /// even if the player then dies in the Final Swarm.</summary>
    public bool Won { get; private set; }

    /// <summary>Phase bosses killed this run (one per phase cleared).</summary>
    public int BossesDefeated { get; private set; }

    /// <summary>The endless phase after the last boss, once it has begun.</summary>
    public FinalSwarm? Swarm { get; private set; }

    public StageDef Stage => _stages[Mathf.Clamp(_index, 0, _stages.Count - 1)];

    /// <summary>Length of the current phase's wave section.</summary>
    public float PhaseLength => _phaseSecondsOverride > 0f ? _phaseSecondsOverride : Stage.PhaseSeconds;

    /// <summary>Seconds until the boss arrives; 0 once it has.</summary>
    public float SecondsToBoss => Mathf.Max(0f, PhaseLength - _run.PhaseSeconds);

    /// <summary>Seconds the boss has outlived its grace window, or 0.</summary>
    public float OvertimeSeconds => State == PhaseState.Boss
        ? Mathf.Max(0f, _run.PhaseSeconds - _bossAt - Stage.BossGraceSeconds) : 0f;

    /// <summary>The live boss's health fraction, or -1 without one.</summary>
    public float BossHealth => _boss is { IsAlive: true } b ? b.HealthFraction : -1f;

    public string BossName => _bossDef?.DisplayName ?? "";

    /// <summary>Where the live phase boss stands, for the autopilot.</summary>
    public Vector3? BossPosition => _boss is { IsAlive: true } b ? b.GlobalPosition : null;

    /// <summary>The open portal, for the HUD arrow and the autopilot.</summary>
    public Vector3? PortalPosition => State == PhaseState.PortalOpen && _portal is not null && GodotObject.IsInstanceValid(_portal)
        ? _portal.GlobalPosition : null;

    public StageDirector(Node3D root, RunState run, EnemyManager enemies, Player player, PlayerCamera camera,
                         IReadOnlyList<StageDef> stages, IReadOnlyList<EnemyDef> roster)
    {
        _root = root;
        _run = run;
        _enemies = enemies;
        _player = player;
        _camera = camera;
        _stages = stages;
        _roster = roster;

        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--phase-seconds="))
                _phaseSecondsOverride = arg["--phase-seconds=".Length..].ToFloat();
            else if (arg.StartsWith("--start-phase="))
                _startPhase = arg["--start-phase=".Length..].ToInt();
            else if (arg.StartsWith("--open-portal-at="))
                _openPortalAt = arg["--open-portal-at=".Length..].ToFloat();
        }
    }

    /// <summary>Build the first map. Called once, after the player exists.</summary>
    public void Begin()
    {
        // From here on bosses come from phases, not from the director's timer;
        // both at once would put two bosses on the field at 4:00.
        _enemies.TimedBosses = false;
        EnterPhase(Mathf.Clamp(_startPhase - 1, 0, _stages.Count - 1));

        if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--check-boundary") >= 0)
            _ = CheckBoundary();
    }

    /// <summary>
    /// --check-boundary: on every map in turn, run the player at the wall --
    /// at the middle of an edge and straight into a corner -- jumping, double
    /// jumping and dashing, and record how far past the wall's inner face it
    /// ever got. Prints a BOUNDARY line per map and BOUNDARY PASS/FAIL, then
    /// quits. Corners matter: on a hexagon they are where a circular clamp
    /// leaks, and where a player pinned by a crowd actually ends up.
    /// </summary>
    private async System.Threading.Tasks.Task CheckBoundary()
    {
        SceneTree tree = _root.GetTree();
        bool all = true;
        float limit = -(Player.BodyRadius + 0.6f);
        for (int i = 0; i < _stages.Count; i++)
        {
            if (i != _index) EnterPhase(i);
            // No enemies: this measures the wall, not whether the player
            // survives standing next to one.
            _enemies.ClearField(9999f);
            StageDef stage = Stage;
            float worst = float.MinValue;
            int sides = Mathf.Max(3, stage.Sides);
            foreach (float angle in new[] { 0f, Mathf.Pi / sides, 2.2f })
            {
                var dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                _player.Teleport(dir * (stage.InRadius - 10f) + Vector3.Up * 0.5f);
                _player.MoveOverride = () => dir;
                for (int frame = 0; frame < 240; frame++)
                {
                    if (frame % 24 == 0) _player.RequestJump();
                    if (frame % 70 == 10) _player.RequestDash();
                    await _root.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
                    worst = Mathf.Max(worst, Arena.EdgeExcess(_player.GlobalPosition));
                }
            }
            bool pass = worst <= limit + 0.05f;
            all &= pass;
            GD.Print($"BOUNDARY map={stage.Id} sides={sides} inradius={stage.InRadius:0.0} " +
                     $"worst={worst:0.00} limit={limit:0.00} {(pass ? "PASS" : "FAIL")}");
        }
        GD.Print(all ? "BOUNDARY PASS" : "BOUNDARY FAIL");
        tree.Quit();
    }

    public void Tick(float dt)
    {
        if (_run.Over || _index < 0) return;

        GuardFall();

        // --open-portal-at=N: skip the boss and open the portal at run time N.
        // For captures only -- a frame of the open portal otherwise depends on
        // when an unseeded-in-practice boss fight happens to end.
        if (_openPortalAt > 0f && _run.Seconds >= _openPortalAt && State is PhaseState.Waves or PhaseState.Boss)
        {
            _openPortalAt = 0f;
            _boss?.Despawn();
            OnBossKilled();
            return;
        }

        switch (State)
        {
            case PhaseState.Waves:
                if (_run.PhaseSeconds >= PhaseLength)
                {
                    State = PhaseState.Boss;
                    _bossAt = _run.PhaseSeconds;
                    _nextBossReport = _bossAt + 30f;
                    _retryAt = 0f;
                    // The boss walks onto an empty field. Thinning the live cap
                    // alone is not enough: by the third map 150 enemies are
                    // already standing round the player, every weapon aims at the
                    // nearest of them, and the first full run's last boss lost 5%
                    // of its health per 30 s. The crowd refills to the boss-fight
                    // cap after a short breather.
                    _enemies.ClearField(ArrivalBreather);
                    SendBoss();
                }
                break;

            case PhaseState.Boss:
                UpdateBoss();
                break;

            case PhaseState.PortalOpen:
                if (_portal is not null && _portal.Contains(_player.GlobalPosition))
                {
                    Log($"portal entered -> phase {_index + 2}/{_stages.Count}");
                    EnterPhase(_index + 1);
                }
                break;

            case PhaseState.FinalSwarm:
                Swarm?.Tick(dt);
                break;

            case PhaseState.Victory:
                _victoryIn -= dt;
                if (_victoryIn <= 0f)
                {
                    Log("run won");
                    _run.Finish(survived: true);
                }
                break;
        }
    }

    // ------------------------------------------------------------- phases

    private void EnterPhase(int index)
    {
        bool first = _index < 0;
        _index = index;
        StageDef stage = Stage;

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

        PopulateFinds(index);

        _player.Teleport(Arena.Centre + Vector3.Up * 0.3f);
        _camera.SnapToTarget();

        _run.BeginPhase(index + 1, _stages.Count, stage.Id, stage.DisplayName);
        State = PhaseState.Waves;
        Log($"phase {index + 1}/{_stages.Count} '{stage.DisplayName}' begins (boss in {PhaseLength:0}s, " +
            $"hp x{stage.EnemyHealthScale:0.00} dmg x{stage.EnemyDamageScale:0.00} spd x{stage.EnemySpeedScale:0.00})");
        EventBus.Publish(new PhaseStarted(index + 1, _stages.Count, stage.Id, stage.DisplayName, stage.Tagline));
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

        // Map shrines first, so the chest spawner sees them as obstacles and
        // keeps chests out of the charge rings.
        ShrineSpawner.Populate(_arena, new Rng(_run.Seed ^ 0x5A1E5D00UL ^ salt), stage.InRadius - 6f, stage);

        int chests = Mathf.Min(stage.Chests, ChestSpawner.MaxWorldChests);
        ChestSpawner.Instance?.Populate(_arena, new Rng(_run.Seed ^ salt), stage.InRadius - 6f, chests);
        Interactables.Instance?.Populate(_arena, new Rng(_run.Seed ^ 0x5A0D5A0DUL ^ salt), stage.InRadius - 6f, stage, index + 1);

        // The spawner rings them at 80-90% of the radius without knowing about
        // terrain; nudge any that landed inside a mound or under a platform.
        foreach (Meta.Shrine shrine in Meta.SecretSpawner.Populate(_arena, new Rng(_run.Seed ^ 0x5EC2E75UL ^ salt), stage.InRadius))
            shrine.Position = _arena.ClearSpotNear(shrine.Position, 2.5f);
    }

    private void ApplyScaling(float overtime, float liveCap = 1f)
    {
        StageDef stage = Stage;
        _enemies.LiveCapScale = liveCap;
        _enemies.PhaseHealthScale = stage.EnemyHealthScale * overtime;
        _enemies.PhaseDamageScale = stage.EnemyDamageScale * overtime;
        _enemies.PhaseSpeedScale = stage.EnemySpeedScale;
    }

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
                if (def.IsBoss && def.Id == wanted) return def;

        EnemyDef? best = null, bestFresh = null, earliest = null, earliestFresh = null;
        foreach (EnemyDef def in _roster)
        {
            if (def is null || !def.IsBoss) continue;
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
        _boss = null;
        BossesDefeated++;
        ApplyScaling(1f);
        // The boss drops a lot; collect it for the player rather than making the
        // walk to the portal a detour through the boss's corpse.
        Pickup.AttractAll();

        if (_index >= _stages.Count - 1)
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
        State = PhaseState.PortalOpen;
        Log($"portal open at {Arena.Centre}");
        EventBus.Publish(new PortalOpened(_index + 1, Arena.Centre));
    }

    /// <summary>A body that leaves the floor — over a wall, through a seam —
    /// is put back on the centre pad. Cheap insurance against the one bug that
    /// would end a run without a death or a win.</summary>
    private void GuardFall()
    {
        if (_player.GlobalPosition.Y > -20f) return;
        Log("player fell out of the world; returned to centre");
        _player.Teleport(Arena.Centre + Vector3.Up * 0.3f);
        _camera.SnapToTarget();
    }

    private void Log(string message) => GD.Print($"[stage] t={_run.Clock} {message}");
}
