using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Wavebreak.Game;

/// <summary>Frame distributions and resource counters, formatted outside the sampled frame.</summary>
public sealed class TraversalSoakMetrics
{
    private readonly List<double> _frames;
    public TraversalSoakMetrics(int capacity = 65536) { _frames = new(capacity); BeginCounters(); }
    private double _cpu, _gpu, _physics, _draws, _prims;
    private int _over17, _over33, _over50, _over100;
    private long _allocStart;
    private int _gc0, _gc1, _gc2;
    public void BeginCounters()
    {
        _allocStart = GC.GetTotalAllocatedBytes(false);
        _gc0 = GC.CollectionCount(0); _gc1 = GC.CollectionCount(1); _gc2 = GC.CollectionCount(2);
    }
    public int Count => _frames.Count;

    public void Add(double milliseconds, Viewport viewport)
    {
        _frames.Add(milliseconds);
        _cpu += RenderingServer.ViewportGetMeasuredRenderTimeCpu(viewport.GetViewportRid()) + RenderingServer.GetFrameSetupTimeCpu();
        _gpu += RenderingServer.ViewportGetMeasuredRenderTimeGpu(viewport.GetViewportRid());
        _physics += Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000;
        _draws += Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
        _prims += Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame);
        if (milliseconds > 16.67) _over17++;
        if (milliseconds > 33.33) _over33++;
        if (milliseconds > 50) _over50++;
        if (milliseconds > 100) _over100++;
    }

    public string Report(string name)
    {
        if (_frames.Count == 0) return "SOAK EMPTY " + name;
        double[] sorted = _frames.ToArray(); Array.Sort(sorted); int n = sorted.Length;
        double Percentile(double p) => sorted[Math.Min(n - 1, (int)(n * p))];
        return FormattableString.Invariant($"SOAK RESULT segment={name} n={n} mean_ms={sorted.Average():F2} p50_ms={Percentile(.5):F2} p95_ms={Percentile(.95):F2} p99_ms={Percentile(.99):F2} max_ms={sorted[^1]:F2} over17={_over17} over33={_over33} over50={_over50} over100={_over100} cpu_ms={_cpu / n:F2} gpu_ms={_gpu / n:F2} physics_ms={_physics / n:F2} draws={_draws / n:F0} prims={_prims / n:F0} alloc_kb_frame={(GC.GetTotalAllocatedBytes(false) - _allocStart) / 1024.0 / n:F2} gc0={GC.CollectionCount(0) - _gc0} gc1={GC.CollectionCount(1) - _gc1} gc2={GC.CollectionCount(2) - _gc2} heap_mb={GC.GetTotalMemory(false) / 1048576.0:F1} working_mb={System.Environment.WorkingSet / 1048576.0:F1} nodes={Performance.GetMonitor(Performance.Monitor.ObjectNodeCount):F0} vram_mb={Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed) / 1048576.0:F1}");
    }
}

