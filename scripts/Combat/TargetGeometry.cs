using System;
using Wavebreak.Actors;
using Wavebreak.Visuals;

namespace Wavebreak.Combat;

internal static class TargetGeometry
{
    internal static float ExtraRadius(IDamageable target) => target is Enemy { Def: { } def }
        && def.Id == ReefLeviathanModels.Id ? MathF.Max(0, def.Radius - Targeting.AssumedTargetRadius) : 0;

    internal static float DistanceSquared(IDamageable target, Godot.Vector3 from)
    {
        Godot.Vector3 delta = target.Position - from;
        if (!Game.Arena.Current.Underwater && !Game.Arena.IsCity) delta.Y = 0;
        float extra = ExtraRadius(target);
        if (extra == 0) return delta.LengthSquared();
        float distance = MathF.Max(0, delta.Length() - extra);
        return distance * distance;
    }
}
