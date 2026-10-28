using System;
using Godot;

namespace Wavebreak.Game;

/// <summary>Read-only test switches preserve an equivalent pre-tuning renderer.</summary>
public static class PerformanceScenario
{
    public static readonly bool Reference = Array.IndexOf(OS.GetCmdlineUserArgs(), "--perf-reference") >= 0;
}
