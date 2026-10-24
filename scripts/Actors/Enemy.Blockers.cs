using Godot;
using Wavebreak.Game;

namespace Wavebreak.Actors;

/// <summary>
/// Steering around the big blockers of maps that have them
/// (<see cref="MapBlockers"/>): when the straight line to the player crosses a
/// building or a cliff, head for the corner of it that makes the shorter way
/// round. The full test runs every fourth tick, staggered by instance; in
/// between the enemy keeps walking to the corner it chose. On maps without
/// blockers this is one count check.
/// </summary>
public sealed partial class Enemy
{
    private int _detourMemo = -1;
    private int _detourTick;
    private Vector3 _detourPoint;
    private bool _detouring;

    private Vector3 SteerAroundBlockers(Vector3 position, Vector3 heading, Vector3 target, float distance)
    {
        if (MapBlockers.Count == 0 || MapBlockers.SteeringOff || distance < 0.8f)
        {
            _detouring = false;
            return heading;
        }

        bool due = ((++_detourTick + (int)(GetInstanceId() & 3)) & 3) == 0;
        if (_detouring)
        {
            Vector3 d = _detourPoint - position;
            d.Y = 0f;
            // Reached the corner: look again at once rather than circling it.
            if (d.LengthSquared() < 1.2f) due = true;
            else if (!due) return d.Normalized();
        }
        else if (!due) return heading;

        if (MapBlockers.Detour(position, target, ref _detourMemo) is { } w)
        {
            _detourPoint = w;
            _detouring = true;
            Vector3 d = w - position;
            d.Y = 0f;
            return d.LengthSquared() > 1e-4f ? d.Normalized() : heading;
        }
        _detouring = false;
        return heading;
    }
}
