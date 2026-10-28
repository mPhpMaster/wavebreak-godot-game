using System;
using Godot;
using Wavebreak.Net;

namespace Wavebreak.Game;

public static class ReefArrival
{
    public static Vector3 Position(Vector3 ordinary, bool first)
    {
        if (!first || !Arena.Current.Underwater || NetSession.Instance?.LateJoined == true) return ordinary;
        foreach (string arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--check-", StringComparison.Ordinal) || arg.StartsWith("--reef-tour", StringComparison.Ordinal)
                || arg == "--coop-check-swim") return ordinary;
        return ordinary with { Y = WaterSurfaceHeight.BaseHeight + 24f };
    }
}
