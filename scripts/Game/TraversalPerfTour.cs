using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Data;

namespace Wavebreak.Game;

/// <summary>Repeatable same-scene measurements, isolated from the user's window/settings.</summary>
public sealed partial class TraversalPerfTour : Node
{
    private readonly Player _player;
    private readonly string _prefix;
    private readonly List<double> _frames = new();
    private double _time, _cpu, _gpu, _physics, _draws, _primitives;
    private long _allocated;
    private int _phase = -1;
    private bool _ready;
    public TraversalPerfTour(Player player, string prefix) { _player = player; _prefix = prefix; }
    public override void _Ready()
    {
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        Engine.MaxFps = 0;
        RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);
        Prof.On = true;
        Meta.GraphicsConfig quality = Meta.GraphicsSettings.Current;
        GD.Print(FormattableString.Invariant($"TOUR QUALITY scale={quality.RenderScale} aa={quality.AntiAlias} shadows={quality.Shadows} ssao={quality.Ssao} glow={quality.Glow} fog={quality.Fog} ssr={quality.Ssr} ssil={quality.Ssil} density={quality.PropDensity} viewport={GetViewport().GetVisibleRect().Size}"));
    }

    private void Begin()
    {
        _phase++;
        _time = 0; _frames.Clear(); _cpu = _gpu = _physics = _draws = _primitives = 0;
        EnemyManager manager = EnemyManager.Instance!;
        if (_phase == 0)
        {
            manager.ClearField(9999f);
            if (Arena.IsCity)
                foreach (Vector3 at in new[] { new Vector3(0, 0, -48), new Vector3(48, 0, 0), new Vector3(-48, 0, 0), new Vector3(0, 0, 48) })
                    manager.SpawnAt(Catalog.EnemyById("city_bellwarden")!, at);
            else
                for (int i = 0; i < 20; i++)
                    manager.SpawnAt(Catalog.EnemyById(i % 2 == 0 ? "brine_leech" : "lantern_jelly")!, new Vector3(-15 + i * 1.5f, 12, -25));
        }
        Vector3 atPlayer = Arena.IsCity ? _phase switch
        {
            0 => new Vector3(0, 1, -10), 1 => new Vector3(18, 33.1f, 22), _ => new Vector3(64, 38, -48),
        } : _phase switch
        {
            0 => new Vector3(-30, 24, -20), 1 => ReefCavePath.Entrance + new Vector3(-18, 1, 0), _ => ReefCavePath.Points[35],
        };
        if (_phase == 3)
        {
            atPlayer = new Vector3(0, 15, -250);
            manager.ClearField(9999f);
            foreach (Vector3 offset in new[] { new Vector3(0, 0, -36), new Vector3(0, 0, 36), new Vector3(48, 0, 0), new Vector3(-48, 0, 0) })
                manager.SpawnAt(Catalog.EnemyById("city_bellwarden")!, new Vector3(0, 0, -250) + offset);
        }
        _player.Teleport(atPlayer);
        _player.MoveOverride = Arena.IsCity ? () => Vector3.Forward * 0.2f : _phase == 1 ? () => Vector3.Right * 0.15f : () => Vector3.Forward * 0.15f;
        PlayerCamera.Instance!.FaceDirection(!Arena.IsCity && _phase == 1 ? Vector3.Right : Vector3.Forward);
        PlayerCamera.Instance.SetPitchDegrees(Arena.IsCity ? -8 : -12);
        PlayerCamera.Instance.SnapToTarget();
        if (_phase == 3)
        {
            Input.ActionPress("city_hook_left"); Input.ActionPress("city_hook_right");
            _player.AttachHook(HookSide.Left, new Vector3(-24.88f, 32, -250));
            _player.AttachHook(HookSide.Right, new Vector3(24.88f, 36, -250));
            _player.RequestJump();
            GD.Print($"TOUR HOOKS left={_player.LeftHookAttached} right={_player.RightHookAttached}");
        }
        _allocated = GC.GetTotalAllocatedBytes(false);
        GD.Print($"TOUR BEGIN map={Arena.Current.Id} phase={_phase} position={atPlayer}");
    }

    public override void _Process(double delta)
    {
        if (!_ready)
        {
            _time += delta;
            if (_time < 1) return;
            _ready = true; Begin(); return;
        }
        _time += delta;
        if (_time > 2)
        {
            _frames.Add(delta * 1000);
            Rid viewport = GetViewport().GetViewportRid();
            _cpu += RenderingServer.ViewportGetMeasuredRenderTimeCpu(viewport) + RenderingServer.GetFrameSetupTimeCpu();
            _gpu += RenderingServer.ViewportGetMeasuredRenderTimeGpu(viewport);
            _physics += Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000;
            _draws += Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
            _primitives += Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame);
        }
        else _allocated = GC.GetTotalAllocatedBytes(false);
        if (_time < 10 || _frames.Count == 0) return;
        _frames.Sort(); int n = _frames.Count;
        GD.Print(FormattableString.Invariant($"TOUR RESULT map={Arena.Current.Id} phase={_phase} n={n} mean_ms={_frames.Average():F2} p50_ms={_frames[n / 2]:F2} p95_ms={_frames[(int)(n * .95)]:F2} cpu_ms={_cpu / n:F2} gpu_ms={_gpu / n:F2} physics_ms={_physics / n:F2} draws={_draws / n:F0} prims={_primitives / n:F0} alloc_kb_frame={(GC.GetTotalAllocatedBytes(false) - _allocated) / 1024.0 / n:F2} enemies={EnemyManager.Instance!.AliveCount}"));
        GD.Print("TOUR SCOPES " + Prof.Report(n));
        if (_phase == 3) GD.Print($"TOUR BOOSTS count={_player.HookBoosts} remaining_left={_player.LeftHookAttached} remaining_right={_player.RightHookAttached}");
        SetProcess(false);
        _ = FinishPhase();
    }

    private async System.Threading.Tasks.Task FinishPhase()
    {
        if (DisplayServer.GetName() != "headless")
            await Screenshot.Capture(GetTree(), this, _prefix + "-" + _phase + ".png");
        bool expanded = Array.IndexOf(OS.GetCmdlineUserArgs(), "--perf-expanded") >= 0;
        if (_phase >= (expanded ? 3 : 2))
        { GD.Print("TOUR COMPLETE"); GetTree().Quit(); }
        else { Begin(); SetProcess(true); }
    }
}
