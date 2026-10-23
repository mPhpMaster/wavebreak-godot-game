using System.Collections.Generic;
using System.Diagnostics;

namespace Wavebreak.Game;

/// <summary>Cheap named timers for --perf: <c>using var _ = Prof.Scope("x");</c>.
/// Does nothing unless --perf is on.</summary>
public static class Prof
{
    public static bool On;
    private static readonly Dictionary<string, (double Ms, int Calls)> Totals = new();

    public readonly struct Timer : System.IDisposable
    {
        private readonly string? _name;
        private readonly long _start;
        public Timer(string? name) { _name = name; _start = name is null ? 0 : Stopwatch.GetTimestamp(); }
        public void Dispose()
        {
            if (_name is null) return;
            double ms = (Stopwatch.GetTimestamp() - _start) * 1000.0 / Stopwatch.Frequency;
            Totals.TryGetValue(_name, out var t);
            Totals[_name] = (t.Ms + ms, t.Calls + 1);
        }
    }

    public static Timer Scope(string name) => new(On ? name : null);

    /// <summary>Per-call averages since the last report, highest first.</summary>
    public static string Report(int frames)
    {
        var list = new List<(string, double, int)>();
        foreach (var (k, v) in Totals) list.Add((k, v.Ms, v.Calls));
        list.Sort((a, b) => b.Item2.CompareTo(a.Item2));
        var sb = new System.Text.StringBuilder();
        foreach (var (k, ms, calls) in list) sb.Append($"{k}={ms / System.Math.Max(1, frames):0.00}ms/f({calls}) ");
        Totals.Clear();
        return sb.ToString();
    }
}
