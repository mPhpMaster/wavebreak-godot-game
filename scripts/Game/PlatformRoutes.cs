using System.Collections.Generic;
using Godot;
using Wavebreak.Data;

namespace Wavebreak.Game;

public static class PlatformRoutes
{
    private static readonly List<PlatformRoute> Routes = new();
    private static object? _owner;
    public static IReadOnlyList<PlatformRoute> All => Routes;
    public static void Begin(object owner) { _owner = owner; Routes.Clear(); }
    public static void End(object owner) { if (ReferenceEquals(owner, _owner)) { Routes.Clear(); _owner = null; } }
    public static void Add(PlatformRoute route) => Routes.Add(route);
    public static Vector3 Next(Vector3 from, Vector3 target)
    {
        foreach (PlatformRoute route in Routes)
        {
            Vector3 across = route.RampSide.Cross(Vector3.Up);
            Vector3 relative = target - route.Centre;
            float half = route.HalfSize;
            if (Mathf.Abs(relative.Dot(route.RampSide)) > half || Mathf.Abs(relative.Dot(across)) > half
                || target.Y < route.Height - .45f || from.Y >= route.Height - .2f) continue;
            relative = from - route.Centre;
            float along = relative.Dot(route.RampSide), lateral = relative.Dot(across);
            float outside = half + 1.4f;
            if (along >= half - .6f && along <= half + route.RampLength + 2.5f && Mathf.Abs(lateral) < 1.7f)
                return route.Centre + route.RampSide * (half - .9f) + Vector3.Up * route.Height;
            float side = lateral >= 0 ? 1 : -1;
            if (along < -half && Mathf.Abs(lateral) < outside - .3f)
                return route.Centre - route.RampSide * outside + across * (side * outside);
            if (along < outside && Mathf.Abs(lateral) < half + route.RampLength)
                return route.Centre + route.RampSide * outside + across * (side * outside);
            return route.Centre + route.RampSide * (half + route.RampLength + .9f);
        }
        return target;
    }
}
