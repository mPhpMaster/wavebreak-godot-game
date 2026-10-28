using System.Linq;
using Godot;
using Wavebreak.Combat;
using Wavebreak.Core;
using Wavebreak.Stats;

namespace Wavebreak.Actors;

public sealed partial class Player
{
    // ------------------------------------------------------------------ loop

    private float _animProbeAt = -1f;
    /// <summary>--check-walk: print the rig's clip state a few times while the
    /// autopilot moves, proving locomotion clips keep cycling.</summary>
    private void ProbeAnimation()
    {
        if (_animProbeAt < 0f)
        {
            _animProbeAt = System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--check-walk") >= 0 ? 2f : float.MaxValue;
            return;
        }
        if (_time < _animProbeAt || _rig is null) return;
        _animProbeAt += 0.7f;
        GD.Print($"WALK t={_time:0.0} v={_moveVelocity.Length():0.0} {_rig.DebugClip}");
    }

    public override void _PhysicsProcess(double delta)
    {
        using var __prof = Wavebreak.Game.Prof.Scope("Player.phys");
        float dt = (float)delta;
        _time += dt;

        UpdateAim();
        ProbeAnimation();
        if (StepCityTraversal(dt)) return;
        if (UpdateSwimmingMode())
        {
            StepSwimming(dt);
            return;
        }

        bool alive = Health is null || Health.IsAlive;
        bool grounded = IsOnFloor();
        Vector3 velocity = Velocity;

        if (grounded) _airJumpsLeft = AirJumps;

        _dashCooldown = Mathf.Max(0f, _dashCooldown - dt);
        _slideCooldown = Mathf.Max(0f, _slideCooldown - dt);

        Vector3 wish = alive ? ReadMoveInput() : Vector3.Zero;
        float walk = Stats.Get(Stat.MoveSpeed);

        bool dashPressed = alive && (Game.Arena.IsCity
            ? !_cityDashHandled && ReadCityDash()
            : Pressed("dash", _hasDash, ref _requestedDash));
        bool slidePressed = alive && Pressed("slide", _hasSlide, ref _requestedSlide);
        bool slideHeld = alive && _inputAvailable && _hasSlide && Input.IsActionPressed("slide");

        if (dashPressed) TryDash(wish, walk);

        if (slidePressed)
        {
            if (!grounded && !_slamming && HeightAboveGround() >= SlamMinHeight)
                StartSlam();
            else if (grounded && !_sliding)
                TryStartSlide(walk);
        }

        if (_dashTime > 0f)
        {
            // The burst owns horizontal motion outright: steering mid-dash would
            // just be a slower, worse dash.
            _dashTime -= dt;
            _moveVelocity = _dashDirection * DashSpeed;
            velocity.Y = Mathf.Max(velocity.Y, 0f) * 0.2f; // flatten the arc a touch
            SpawnAfterimage(dt);
            if (_dashTime <= 0f) _moveVelocity = _dashDirection * walk * DashExitSpeedScale;
        }
        else if (_slamming)
        {
            // Horizontal speed collapses so the slam lands where you aimed it.
            _moveVelocity = _moveVelocity.MoveToward(Vector3.Zero, 60f * dt);
            velocity.Y = -SlamDownSpeed;
        }
        else if (_sliding)
        {
            UpdateSlide(wish, walk, grounded, slideHeld, dt);
        }
        else
        {
            UpdateWalk(wish, walk, grounded, dt);
        }

        // Gravity: heavier on the way down, and none while dashing so a dash off
        // a ledge travels flat instead of sagging.
        if (!grounded && _dashTime <= 0f && !_slamming)
        {
            float scale = velocity.Y < 0f ? FallGravityScale : 1f;
            velocity.Y = Mathf.Max(velocity.Y - _gravity * scale * dt, -TerminalFallSpeed);
        }

        // Knockback lives outside the controller's own velocity so that steering
        // into a shove neither cancels it instantly nor compounds with it.
        _externalVelocity = _externalVelocity.MoveToward(Vector3.Zero, KnockbackDecay * dt);

        if (alive && !_slamming)
            velocity.Y = ApplyJump(velocity.Y, grounded, wish, walk, dt);

        _moveVelocity = _moveVelocity.LimitLength(Game.Arena.IsCity && _cityBallistic ? Game.GrapplePhysics.BoostSpeed : MaxPlanarSpeed);
        PeakSpeed = Mathf.Max(PeakSpeed, _moveVelocity.Length());

        Velocity = new Vector3(
            _moveVelocity.X + _externalVelocity.X,
            velocity.Y,
            _moveVelocity.Z + _externalVelocity.Z) + SwimmingRecoil(dt);

        if (!grounded) _lastFallSpeed = Mathf.Max(0f, -Velocity.Y);

        MoveAndSlide();
        KeepInsideArena();

        // Running into a wall should cost the speed you ran into it with, or
        // stored momentum comes back the moment you slide off the wall.
        if (GetSlideCollisionCount() > 0 && IsOnWall())
        {
            Vector3 n = GetWallNormal() with { Y = 0f };
            if (n.LengthSquared() > 0.001f)
            {
                n = n.Normalized();
                float into = _moveVelocity.Dot(n);
                if (into < 0f) _moveVelocity -= n * into;
            }
        }

        // Landing is detected across MoveAndSlide, against the floor state this
        // frame *started* with. Comparing to a flag saved after the previous
        // MoveAndSlide would compare the same state with itself and never fire.
        bool nowGrounded = IsOnFloor();
        if (nowGrounded && !grounded) OnLanded(_lastFallSpeed);
        else if (nowGrounded && _slamming) FinishSlam(); // slammed from a ledge onto the floor we stood on
        if (nowGrounded) _lastFallSpeed = 0f;

        UpdateFacing(wish, dt);
        UpdatePose(grounded, walk, dt);
        UpdateVisuals(alive, dt);
        UpdateCameraFeel(walk);
    }

    /// <summary>
    /// Hard boundary at the arena edge. The wall's collider is a low ring and a
    /// jump peaks at 6 m before the double jump, so the wall alone could be
    /// cleared -- and outside there is floor but no enemies, no pickups and no
    /// way back. Clamp the position and cancel only the outward part of the
    /// velocity, so running along the edge still works.
    /// </summary>
    private void KeepInsideArena()
    {
        // The current map's own polygon, not a circle: maps change shape per
        // phase, and a circle either loses a hexagon's corners or leaks them.
        // (--check-boundary, run per map, lives in Game.StageDirector.)
        Vector3 at = Game.Arena.ClampInside(GlobalPosition, BodyRadius + 0.6f, out Vector3 normal);
        if (normal == Vector3.Zero) return;

        GlobalPosition = at;
        float outMove = _moveVelocity.Dot(normal);
        if (outMove > 0f) _moveVelocity -= normal * outMove;
        float outPush = _externalVelocity.Dot(normal);
        if (outPush > 0f) _externalVelocity -= normal * outPush;
        if (_dashTime > 0f) _dashDirection = (_dashDirection - normal * Mathf.Max(0f, _dashDirection.Dot(normal))).Normalized();
    }

    private bool Pressed(string action, bool available, ref bool requested)
    {
        // Net.Coop.LocalHold: a co-op menu is open; the world runs, this player waits.
        bool pressed = requested || (!Net.Coop.LocalHold && _inputAvailable && available && Input.IsActionJustPressed(action));
        requested = false;
        return pressed;
    }

}
