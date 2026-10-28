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
public sealed partial class StageDirector
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
        /// <summary>Between two maps: the safe rest room (shop, campfire, CONTINUE
        /// portal). No enemies, run clock held. See StageDirector.RestRoom.cs.</summary>
        RestRoom,
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
    private IReadOnlyList<StageDef> _stages;
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

    private float _portalLeft;
    private float _portalSyncIn;
    private readonly float _portalSecondsOverride;

    /// <summary>How long a boss portal stays open: the tier's
    /// <see cref="Meta.DifficultyTier.PortalSeconds"/>, or --portal-seconds=N.</summary>
    public float PortalSeconds => _portalSecondsOverride > 0f ? _portalSecondsOverride : Meta.DifficultyTiers.Active.PortalSeconds;

    /// <summary>Seconds until the open boss portal pulls everyone through, or
    /// -1 when no boss portal is open.</summary>
    public float PortalSecondsLeft => State == PhaseState.PortalOpen ? _portalLeft : -1f;

    /// <summary>How the last boss portal was taken: "entered" or "timeout".</summary>
    public string PortalExitReason { get; private set; } = "";

    /// <summary>True from the moment the last phase boss dies: the run is won,
    /// even if the player then dies in the Final Swarm.</summary>
    public bool Won { get; private set; }

    /// <summary>Phase bosses killed this run (one per phase cleared).</summary>
    public int BossesDefeated { get; private set; }

    /// <summary>The endless phase after the last boss, once it has begun.</summary>
    public FinalSwarm? Swarm { get; private set; }

    public StageDef Stage => _stages[Mathf.Clamp(_index, 0, _stages.Count - 1)];

    /// <summary>Length of the current phase's wave section.</summary>
    public float PhaseLength => _phaseSecondsOverride > 0f ? _phaseSecondsOverride
        : Stage.PhaseSeconds * Meta.Challenges.PhaseTimeScale;

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
        ? _portal.GlobalPosition
        : State == PhaseState.RestRoom && Rest is { } rest ? rest.ExitPosition : null;

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
            else if (arg.StartsWith("--portal-seconds="))
                _portalSecondsOverride = arg["--portal-seconds=".Length..].ToFloat();
        }
    }

    /// <summary>Build the first map. Called once, after the player exists.</summary>
    public void Begin()
    {
        // From here on bosses come from phases, not from the director's timer;
        // both at once would put two bosses on the field at 4:00.
        _enemies.TimedBosses = false;
        // The boundary check walks every map there is, not just this run's route.
        bool checkBoundary = System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--check-boundary") >= 0;
        if (checkBoundary) _stages = Data.Stages.All;
        EnterPhase(Mathf.Clamp(_startPhase - 1, 0, _stages.Count - 1));

        if (checkBoundary)
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
            all &= CheckTotemPlacement(stage);   // Tide Totems (StageDirector.Totems.cs)
            // Maps with big blockers: walls, spawn ring, finds, pathing (StageDirector.Blockers.cs).
            if (MapBlockers.Count > 0) all &= await CheckBlockers(stage);
        }
        GD.Print(all ? "BOUNDARY PASS" : "BOUNDARY FAIL");
        tree.Quit();
    }

    public void Tick(float dt)
    {
        if (_run.Over || _index < 0) return;

        GuardFall();
        // The boss portal's countdown runs everywhere, so the HUD and the
        // portal's own numbers move smoothly; the host corrects clients twice
        // a second and alone decides what happens at zero.
        if (State == PhaseState.PortalOpen)
        {
            _portalLeft = Mathf.Max(0f, _portalLeft - dt);
            if (_portal is not null && GodotObject.IsInstanceValid(_portal)) _portal.Countdown = _portalLeft;
        }
        // Co-op client: the host runs the phase flow and sends each step
        // (StageDirector.Coop.cs).
        if (Net.Coop.IsClient) return;

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
                if (Net.Coop.IsHost)
                {
                    _portalSyncIn -= dt;
                    if (_portalSyncIn <= 0f)
                    {
                        _portalSyncIn = 0.5f;
                        Net.NetSession.Instance?.SendPortalTimer(_portalLeft);
                    }
                }
                bool entered = _portal is not null && (_portal.Contains(_player.GlobalPosition)
                                                       || Net.Coop.IsHost && Net.NetSession.Instance?.AnyRemoteInside(_portal) == true);
                // At zero the portal pulls everyone through, wherever they
                // stand: the map is cleared, nothing is gained by waiting,
                // and a portal that simply shut would strand the run.
                bool closing = !entered && _portal is not null && _portalLeft <= 0f;
                if (closing) Log("portal closing: everyone is pulled through");
                if (entered || closing)
                {
                    PortalExitReason = entered ? "entered" : "timeout";
                    if (RestRoomEnabled)
                    {
                        Log($"portal entered -> rest room, then phase {_index + 2}/{_stages.Count}");
                        EnterRestRoom();
                    }
                    else
                    {
                        Log($"portal entered -> phase {_index + 2}/{_stages.Count}");
                        EnterPhase(NextIndex());
                    }
                }
                break;

            case PhaseState.RestRoom:
                StepRestRoom(dt);
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

}
