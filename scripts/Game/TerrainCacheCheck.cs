using System;
using System.Diagnostics;
using Godot;

namespace Wavebreak.Game;

public static class TerrainCacheCheck
{
    public static void Run(Action<bool, string> check)
    {
        var timer = Stopwatch.StartNew();
        using ArrayMesh original = UnderwaterTerrain.Build(Arena.Current.InRadius, true);
        double originalMs = timer.Elapsed.TotalMilliseconds;
        timer.Restart();
        using ArrayMesh cached = UnderwaterTerrain.Build(Arena.Current.InRadius, false);
        double cachedMs = timer.Elapsed.TotalMilliseconds;
        Vector3[] before = original.GetFaces(), after = cached.GetFaces();
        bool identical = before.Length == after.Length;
        for (int i = 0; identical && i < before.Length; i++) identical &= before[i] == after[i];
        check(identical && after.Length > 60000, "cached terrain preserves every original collision/render face exactly");
        GD.Print(FormattableString.Invariant($"TERRAIN CACHE original_ms={originalMs:F2} cached_ms={cachedMs:F2} vertices={after.Length} exact={identical}"));
    }
}
