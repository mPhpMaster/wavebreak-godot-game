using System.Linq;
using Godot;
using Wavebreak.Combat;
using Wavebreak.Core;
using Wavebreak.Stats;

namespace Wavebreak.Actors;

public sealed partial class Player
{
    // ------------------------------------------------------------------ aim

    private Vector3 ReadMoveInput()
    {
        if (MoveOverride is not null)
        {
            Vector3 driven = MoveOverride();
            if (!IsSwimming) driven.Y = 0f;
            return driven.LengthSquared() > 1f ? driven.Normalized() : driven;
        }

        if (!_inputAvailable || Net.Coop.LocalHold) return Vector3.Zero;

        Vector2 input = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
        if (input.LengthSquared() < 0.0001f) return Vector3.Zero;
        if (IsSwimming && PlayerCamera.Instance is { } camera)
            return Game.SwimmingNavigation.Wish(input, camera.Yaw, camera.Pitch);

        Vector3 forward = AimDirection;
        Vector3 right = new(-forward.Z, 0f, forward.X);

        // input.Y is positive for "back" because that is how GetVector orders its
        // arguments; negate it so forward is forward.
        Vector3 wish = forward * -input.Y + right * input.X;
        return wish.LengthSquared() > 1f ? wish.Normalized() : wish;
    }

    /// <summary>
    /// Refreshes <see cref="AimDirection"/>. The rig's own yaw is preferred: it is
    /// the number the player steers with, and reading it means screen shake can
    /// never leak into movement.
    /// </summary>
    private void UpdateAim()
    {
        if (PlayerCamera.Instance is { } rig && IsInstanceValid(rig))
        {
            AimDirection = rig.FlatForward;
            return;
        }

        if (_camera is null || !IsInstanceValid(_camera))
            _camera = GetViewport()?.GetCamera3D();

        if (_camera is null || !IsInstanceValid(_camera)) return;

        Basis basis = _camera.GlobalTransform.Basis;
        Vector3 forward = -basis.Z;
        forward.Y = 0f;

        if (forward.LengthSquared() < 0.0001f)
        {
            forward = basis.Y;
            forward.Y = 0f;
            if (forward.LengthSquared() < 0.0001f) return;
        }

        AimDirection = forward.Normalized();
    }

    private Vector3 FacingDirection() => new(-Mathf.Sin(_yaw), 0f, -Mathf.Cos(_yaw));

    private float HeightAboveGround()
    {
        var query = PhysicsRayQueryParameters3D.Create(
            GlobalPosition + Vector3.Up * 0.1f, GlobalPosition + Vector3.Down * 50f, CollisionMask);
        query.Exclude = new Godot.Collections.Array<Rid> { GetRid() };
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0) return 50f;
        return GlobalPosition.Y - ((Vector3)hit["position"]).Y;
    }

    private static Vector3 RotateToward(Vector3 from, Vector3 to, float maxRadians)
    {
        float a = Mathf.Atan2(from.X, from.Z);
        float b = Mathf.Atan2(to.X, to.Z);
        float diff = Mathf.Wrap(b - a, -Mathf.Pi, Mathf.Pi);
        float step = Mathf.Clamp(diff, -maxRadians, maxRadians);
        float r = a + step;
        return new Vector3(Mathf.Sin(r), 0f, Mathf.Cos(r));
    }

}
