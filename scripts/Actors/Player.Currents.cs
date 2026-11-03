using Godot;
using Wavebreak.Game;

namespace Wavebreak.Actors;

public sealed partial class Player
{
    private Vector3 _currentDrift;

    /// <summary>Velocity the water currents add to the swimmer right now (m/s).</summary>
    public Vector3 CurrentDrift => _currentDrift;
    /// <summary>Inside a pushing current this frame.</summary>
    public bool InReefCurrent { get; private set; }

    /// <summary>
    /// The current's push eases in and out rather than snapping, so entering
    /// one feels like being caught and leaving it like drifting free. It is
    /// added on top of the swimmer's own velocity: swimming with the flow is
    /// faster, across it still moves you out at full swim speed.
    /// </summary>
    private Vector3 StepCurrentDrift(float dt)
    {
        Vector3 target = ReefCurrentField.VelocityAt(CenterPosition);
        InReefCurrent = target.LengthSquared() > 0.25f;
        float rate = target.LengthSquared() >= _currentDrift.LengthSquared() ? 9f : 6f;
        _currentDrift = _currentDrift.MoveToward(target, rate * dt);
        return _currentDrift;
    }
}
