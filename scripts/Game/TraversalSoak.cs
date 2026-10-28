using System;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Data;

namespace Wavebreak.Game;

/// <summary>120 seconds of live director/combat plus rapid hook/swim traversal, without saved changes.</summary>
public sealed partial class TraversalSoak : Node
{
    private readonly Player _player;
    private readonly string _prefix;
    private readonly TraversalSoakMetrics _all = new(), _steady = new();
    private TraversalSoakMetrics _window = new(4096);
    private double _time, _windowTime;
    private float _acquire, _boost;
    private int _episode = -1, _windowIndex, _leftAcquires, _rightAcquires, _peakEnemies;
    private ulong _readyAt, _previousFrame, _playStartedAt;
    private bool _begun, _steadyStarted;
    private double _simulationTime;
    private int _coreStrikes;
    private float _duration = 120;
    private Vector3 _wish;

    public TraversalSoak(Player player, string prefix) { _player = player; _prefix = prefix; }

    public override void _Ready()
    {
        _readyAt = _previousFrame = Time.GetTicksUsec();
        foreach (string arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--soak-seconds=") && float.TryParse(arg["--soak-seconds=".Length..], out float seconds))
                _duration = Mathf.Clamp(seconds, 90, 180);
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled); Engine.MaxFps = 0;
        RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true); Prof.On = true;
        _player.MoveOverride = () => _wish;
    }

    public override void _Process(double delta)
    {
        ulong now = Time.GetTicksUsec();
        double frameMs = (now - _previousFrame) / 1000.0;
        _previousFrame = now;
        if (!_begun)
        {
            _begun = true;
            GD.Print($"SOAK STARTUP engine_ms={Time.GetTicksMsec()} ready_to_first_frame_ms={(now - _readyAt) / 1000.0:F2} first_delta_ms={delta * 1000:F2}");
            var q = Meta.GraphicsSettings.Current;
            GD.Print($"SOAK CONFIG reference={PerformanceScenario.Reference} map={Arena.Current.Id} seconds={_duration} scale={q.RenderScale} aa={q.AntiAlias} shadows={q.Shadows} ssao={q.Ssao} density={q.PropDensity} seed={Main.Instance!.Run.Seed}");
            SeedCrowd();
            _player.MoveOverride = () => _wish;
            _playStartedAt = _previousFrame = Time.GetTicksUsec();
            _all.BeginCounters(); _window.BeginCounters();
            return;
        }
        _time = (now - _playStartedAt) / 1000000.0; _windowTime += frameMs / 1000;
        _all.Add(frameMs, GetViewport()); _window.Add(frameMs, GetViewport());
        if (_time > 10)
        {
            if (!_steadyStarted) { _steady.BeginCounters(); _steadyStarted = true; }
            _steady.Add(frameMs, GetViewport());
        }
        _peakEnemies = Math.Max(_peakEnemies, EnemyManager.Instance!.AliveCount);
        if (_windowTime >= 10)
        {
            GD.Print(_window.Report("t" + (++_windowIndex * 10)));
            GD.Print("SOAK SCOPES " + Prof.Report(_window.Count));
            GD.Print($"SOAK GAME t={_time:F1} enemies={EnemyManager.Instance.AliveCount} kills={Main.Instance!.Run.Kills} boosts={_player.HookBoosts} position={_player.GlobalPosition} speed={_player.Velocity.Length():F1}");
            _window = new(4096); _windowTime = 0;
        }
        if (_time < _duration) return;
        GD.Print(_all.Report("all")); GD.Print(_steady.Report("steady"));
        GD.Print($"SOAK COMPLETE left_acquires={_leftAcquires} right_acquires={_rightAcquires} boosts={_player.HookBoosts} core_strikes={_coreStrikes} peak_enemies={_peakEnemies} kills={Main.Instance!.Run.Kills} anchor_queries={GrappleAnchors.QueryCount} simulated_seconds={_simulationTime:F2}");
        SetProcess(false); SetPhysicsProcess(false); ReleaseInputs();
        _ = Finish();
    }

    private void SeedCrowd()
    {
        EnemyManager manager = EnemyManager.Instance!;
        if (Arena.IsCity)
            foreach (Vector3 at in new[] { new Vector3(0, 0, -48), new Vector3(48, 0, 0), new Vector3(-48, 0, 0), new Vector3(0, 0, 48) })
                manager.SpawnAt(Catalog.EnemyById("city_bellwarden")!, at);
        else
            for (int i = 0; i < 20; i++)
                manager.SpawnAt(Catalog.EnemyById(i % 2 == 0 ? "brine_leech" : "lantern_jelly")!, new Vector3(-15 + i * 1.5f, 12, -25));
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_begun) return;
        _simulationTime += delta;
        int episode = (int)(_simulationTime / 20);
        if (episode != _episode)
        {
            _episode = episode; ReleaseInputs();
            Vector3 at = Arena.IsCity ? (episode % 6) switch
            {
                0 => new(0, 8, -10), 1 => new(0, 15, -250), 2 => new(250, 18, 0),
                3 => new(0, 15, 250), 4 => new(-250, 15, 0), _ => new(18, 34, 22),
            } : (episode % 6) switch
            {
                0 => new(-30, 24, -20), 1 => new(60, 30, 30), 2 => ReefCavePath.Entrance + Vector3.Left * 10,
                3 => ReefCavePath.Points[35], 4 => ReefCavePath.Points[69], _ => ReefCavePath.Exit + Vector3.Down * 6,
            };
            _player.Teleport(at); _acquire = _boost = 0;
            PlayerCamera.Instance!.SnapToTarget();
            GD.Print($"SOAK EPISODE index={episode} position={at}");
        }
        float angle = (float)_simulationTime * .22f;
        _wish = new Vector3(Mathf.Sin(angle), 0, -Mathf.Cos(angle));
        PlayerCamera.Instance!.FaceDirection(_wish);
        PlayerCamera.Instance.SetPitchDegrees(Arena.IsCity ? 18 : -10);
        _acquire -= (float)delta; _boost -= (float)delta;
        if (Arena.IsCity)
        {
            if (_acquire <= 0)
            {
                _acquire = 1.5f;
                Attach(HookSide.Left, -1); Attach(HookSide.Right, 1);
            }
            if (_boost <= 0)
            {
                _boost = 2.3f; _player.RequestJump();
                if (_player.TryCoreStrike()) _coreStrikes++;
            }
        }
        else
        {
            bool rise = (int)(_simulationTime / 4) % 3 == 0, dive = (int)(_simulationTime / 4) % 3 == 1;
            if (rise) Input.ActionPress("jump"); else Input.ActionRelease("jump");
            if (dive) Input.ActionPress("slide"); else Input.ActionRelease("slide");
            if (_boost <= 0) { _boost = 3.2f; _player.RequestDash(); }
        }
    }

    private void Attach(HookSide side, int sign)
    {
        string action = side == HookSide.Left ? "city_hook_left" : "city_hook_right";
        Input.ActionRelease(action); _player.ReleaseHook(side);
        Vector3 from = _player.CenterPosition;
        for (int i = 0; i < 6; i++)
        {
            float angle = sign * (i + 1) * Mathf.Pi / 6;
            Vector3 heading = _wish.Rotated(Vector3.Up, angle) + Vector3.Up * .6f;
            if (!GrappleAnchors.Ray(_player, from, heading.Normalized(), from, out Vector3 anchor)) continue;
            Input.ActionPress(action);
            if (_player.AttachHook(side, anchor))
            { if (side == HookSide.Left) _leftAcquires++; else _rightAcquires++; }
            return;
        }
    }

    private static void ReleaseInputs()
    {
        foreach (string action in new[] { "city_hook_left", "city_hook_right", "jump", "slide" }) Input.ActionRelease(action);
    }

    private async System.Threading.Tasks.Task Finish()
    {
        if (DisplayServer.GetName() != "headless") await Screenshot.Capture(GetTree(), this, _prefix + ".png");
        GetTree().Quit();
    }
    public override void _ExitTree() => ReleaseInputs();
}
