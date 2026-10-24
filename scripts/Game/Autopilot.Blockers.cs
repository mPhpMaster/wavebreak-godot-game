using Godot;

namespace Wavebreak.Game;

/// <summary>
/// The bot walks round the big blockers (<see cref="MapBlockers"/>) the same
/// way enemies do: when its wish runs into a building or a cliff within the
/// next few metres, it heads for the nearer corner instead. When the portal
/// is open it routes to the portal itself, so a bot on the far side of the
/// harbour's old town still finds the square.
/// </summary>
public sealed partial class Autopilot
{
    private int _blockMemo = -1;

    private Vector3 AvoidBlockers(Vector3 me, Vector3 wish)
    {
        if (MapBlockers.Count == 0 || wish.LengthSquared() < 1e-6f) return wish;
        if (Main.Instance?.Stages.PortalPosition is Vector3 portal
            && MapBlockers.Detour(me, portal, ref _blockMemo) is { } w)
        {
            Vector3 d = w - me;
            d.Y = 0f;
            if (d.LengthSquared() > 1e-4f) return d.Normalized() * wish.Length();
        }
        return MapBlockers.Deflect(me, wish, ref _blockMemo, 12f);
    }
}
