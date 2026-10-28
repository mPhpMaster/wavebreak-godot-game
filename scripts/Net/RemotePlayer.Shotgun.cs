using Godot;

namespace Wavebreak.Net;

public sealed partial class RemotePlayer
{
    internal int ShotgunPosesReceived { get; private set; }
    internal void ApplyShotgunPose(Vector3 heading)
    {
        ShotgunPosesReceived++;
        if (new Vector2(heading.X, heading.Z).LengthSquared() > .01f)
        { _netYaw = Mathf.Atan2(-heading.X, -heading.Z); _yawNode.Rotation = _yawNode.Rotation with { Y = _netYaw }; }
        _rig.AimShotgun(heading, true);
    }
    internal float ShotgunGripError => _rig.ShotgunGripError;
}
