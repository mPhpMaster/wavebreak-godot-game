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
    private int _reports;

    /// <summary>Top node kinds by count (script class, else engine class), so
    /// whatever piles up over a run shows by name.</summary>
    private static string NodeCensus(Node root)
    {
        var counts = new System.Collections.Generic.Dictionary<string, int>();
        var stack = new System.Collections.Generic.Stack<Node>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            Node n = stack.Pop();
            string key = n.GetType() == typeof(Node) || n.GetType().Namespace == "Godot"
                ? n.GetClass() + ":" + n.GetParent()?.GetType().Name
                : n.GetType().Name;
            counts.TryGetValue(key, out int had);
            counts[key] = had + 1;
            foreach (Node c in n.GetChildren()) stack.Push(c);
        }
        var top = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, int>>(counts);
        top.Sort((a, b) => b.Value.CompareTo(a.Value));
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < System.Math.Min(14, top.Count); i++) sb.Append(top[i].Key).Append('=').Append(top[i].Value).Append(' ');
        return sb.ToString();
    }

    public static void Attach(Node parent)
    {
        if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--perf") < 0) return;
        Prof.On = true;
        parent.AddChild(new PerfProbe { ProcessMode = ProcessModeEnum.Always });
        RenderingServer.ViewportSetMeasureRenderTime(parent.GetViewport().GetViewportRid(), true);
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
                 $"cpu={RenderingServer.ViewportGetMeasuredRenderTimeCpu(GetViewport().GetViewportRid()) + RenderingServer.GetFrameSetupTimeCpu():0.0}ms " +
                 $"gpu={RenderingServer.ViewportGetMeasuredRenderTimeGpu(GetViewport().GetViewportRid()):0.0}ms " +
                 $"enemies={EnemyManager.Instance?.AliveCount ?? 0} " +
                 $"t={Main.Instance?.Run.Seconds ?? 0:0}");
        GD.Print("PROF " + Prof.Report(_frames));
        if (++_reports % 6 == 0) GD.Print("NODES " + NodeCensus(GetTree().Root));
        _t = 0;
        _frames = 0;
        _worst = 0;
    }
}
