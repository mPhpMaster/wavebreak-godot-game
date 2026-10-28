using Godot;

namespace Wavebreak.Actors;

/// <summary>What co-op sends about the local player (Net/NetSession.Run).
/// Read-only views of state the controller already keeps.</summary>
public sealed partial class Player
{
    /// <summary>Body facing, radians (the yaw the model is turned to).</summary>
    public float FacingYaw => _yaw;

    /// <summary>Pose bits for the network: 0-2 the rig pose, 8 grounded.</summary>
    public int NetPoseFlags
    {
        get
        {
            if (IsSwimming) return IsBreaching ? 64 | (int)Visuals.RigPose.Air : 32 | 8 | (int)Visuals.RigPose.Locomotion;
            Visuals.RigPose pose = _dashTime > 0f ? Visuals.RigPose.Dash
                : _slamming ? Visuals.RigPose.Slam
                : _sliding ? Visuals.RigPose.Slide
                : !IsOnFloor() ? Visuals.RigPose.Air
                : Visuals.RigPose.Locomotion;
            return (int)pose | (IsOnFloor() ? 8 : 0) | (Grappling ? 128 : 0);
        }
    }

    /// <summary>Planar velocity plus vertical, for the remote rig's stride.</summary>
    public Vector3 NetVelocity => IsSwimming ? Velocity
        : new(_moveVelocity.X + _externalVelocity.X, Velocity.Y, _moveVelocity.Z + _externalVelocity.Z);
}
