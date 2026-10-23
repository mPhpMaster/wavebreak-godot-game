using Godot;
using Wavebreak.Actors;

namespace Wavebreak.Game;

/// <summary>
/// --perf: every 5 seconds print FPS, frame/physics time, draw calls, object
/// and node counts and the live crowd, so "it's heavy" can be measured rather
/// than guessed. Run without --fixed-fps to see real frame rates.
/// </summary>
public sealed partial class PerfProbe : Node
{
    private double _t;
    private int _frames;
    private double _worst;

    public static void Attach(Node parent)
    {
        if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--perf") < 0) return;
        Prof.On = true;
        parent.AddChild(new PerfProbe { ProcessMode = ProcessModeEnum.Always });
    }

    public override void _Process(double delta)
    {
        _t += delta;
        _frames++;
        if (delta > _worst) _worst = delta;
        if (_t < 5.0) return;

        double fps = _frames / _t;
        GD.Print($"PERF fps={fps:0.0} worst={_worst * 1000:0}ms " +
                 $"process={Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000:0.0}ms " +
                 $"physics={Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000:0.0}ms " +
                 $"draws={Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame):0} " +
                 $"objects={Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame):0} " +
                 $"prims={Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame) / 1000:0}k " +
                 $"nodes={Performance.GetMonitor(Performance.Monitor.ObjectNodeCount):0} " +
                 $"vram={Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed) / 1048576:0}MB " +
                 $"enemies={EnemyManager.Instance?.AliveCount ?? 0} " +
                 $"t={Main.Instance?.Run.Seconds ?? 0:0}");
        GD.Print("PROF " + Prof.Report(_frames));
        _t = 0;
        _frames = 0;
        _worst = 0;
    }
}
