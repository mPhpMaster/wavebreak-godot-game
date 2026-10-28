using Godot;
using Wavebreak.Game;
using Wavebreak.Stats;
using Wavebreak.Visuals;

namespace Wavebreak.Actors;

public sealed partial class Player
{
    public bool IsSwimming { get; private set; }
    public bool IsBreaching { get; private set; }
    public int Breaches { get; private set; }
    public int WaterEntries { get; private set; }
    public bool SwimmingBoostActive { get; private set; }
    public float SwimmingPitch => _rig.Rotation.X;
    public bool SwimmingAnimationVerified => _rig.SwimmingAnimationActive && _rig.SwimmingJointCount > 0 && _rig.LocomotionClipStopped;
    public int SwimmingAnimationFrames => _rig.SwimmingAnimationFrames;
    public const float SwimFloor = 0.7f;
    public const float BreachSpeed = 7f;
    private float _normalCameraPitch = 8f;
    private float _swimVerticalVelocity;

    private bool UpdateSwimmingMode()
    {
        bool swim = Arena.Current.Underwater;
        if (swim == IsSwimming) return swim;
        IsSwimming = swim;
        IsBreaching = false;
        SwimmingBoostActive = false;
        _swimWeaponRecoil = Vector3.Zero;
        _swimVerticalVelocity = 0;
        MotionMode = swim ? MotionModeEnum.Floating : MotionModeEnum.Grounded;
        FloorSnapLength = swim ? 0f : 0.4f;
        Velocity = Vector3.Zero;
        _moveVelocity = _externalVelocity = _lastPlanar = Vector3.Zero;
        _dashTime = _coyote = _jumpBuffer = _lastFallSpeed = 0f;
        _slamming = _sliding = _requestedJump = _requestedSlide = _requestedDash = false;
        _airJumpsLeft = AirJumps;
        _rig.Rotation = Vector3.Zero;
        _rig.Position = Vector3.Zero;
        _squash = _squashVelocity = 0f;
        _lean = Vector3.Zero;
        _rig.Swimming = swim;
        if (PlayerCamera.Instance is { } camera)
        {
            if (swim) { _normalCameraPitch = camera.MaxPitchDegrees; camera.MaxPitchDegrees = 78f; }
            else { camera.MaxPitchDegrees = _normalCameraPitch; camera.SetPitchDegrees(Mathf.RadToDeg(camera.Pitch)); }
        }
        return swim;
    }

    private void StepSwimming(float dt)
    {
        bool alive = Health is null || Health.IsAlive;
        bool control = alive && !Net.Coop.LocalHold;
        Vector3 wish = control ? ReadMoveInput() : Vector3.Zero;
        float speed = Stats.Get(Stat.MoveSpeed);
        bool rise = control && (_requestedJump || (_hasJump && Input.IsActionPressed("jump")));
        bool dive = control && (_requestedSlide || (_hasSlide && Input.IsActionPressed("slide")));
        _requestedJump = _requestedSlide = false;
        _slamming = _sliding = false;
        _jumpBuffer = _coyote = _lastFallSpeed = 0f;
        _dashCooldown = Mathf.Max(0f, _dashCooldown - dt);
        _slideCooldown = Mathf.Max(0f, _slideCooldown - dt);
        if (!IsBreaching && CenterPosition.Y > WaterSurfaceHeight.At(GlobalPosition) + 0.8f)
            IsBreaching = true;
        if (IsBreaching) { SwimmingBoostActive = false; StepBreach(wish, speed, dive, dt); FinishSwimVisuals(wish, speed, alive, dt); return; }

        Data.SwimmingIntent intent = SwimmingNavigation.WithVertical(wish, rise, dive);
        wish = intent.Direction;
        SwimmingBoostActive = intent.Reinforced;
        speed *= intent.SpeedScale;
        if (control && Pressed("dash", _hasDash, ref _requestedDash))
        {
            int before = Dashes;
            Vector3 launch = wish.LengthSquared() > .01f ? wish : -(GetViewport().GetCamera3D()?.GlobalBasis.Z ?? Vector3.Back);
            TryDash(launch, speed);
            if (Dashes > before) _dashTime = SwimmingDash.Duration;
        }
        float y;
        if (_dashTime > 0f)
        {
            _dashDirection = SwimmingDash.Steer(_dashDirection, wish, dt);
            float burstSpeed = SwimmingDash.Speed(speed, _dashTime);
            _dashTime = Mathf.Max(0f, _dashTime - dt);
            _moveVelocity = (_dashDirection with { Y = 0f }) * burstSpeed;
            y = _dashDirection.Y * burstSpeed;
            SwimmingBoostActive = true;
            SpawnAfterimage(dt);
        }
        else
        {
            _moveVelocity = _moveVelocity.MoveToward((wish with { Y = 0f }) * speed, 18f * dt);
            y = Mathf.MoveToward(_swimVerticalVelocity, wish.Y * speed, 20f * dt);
        }
        _swimVerticalVelocity = y;
        _externalVelocity = _externalVelocity.MoveToward(Vector3.Zero, 12f * dt);
        Vector3 current = ReefCurrents.VelocityAt(GlobalPosition);
        Velocity = new Vector3(_moveVelocity.X + _externalVelocity.X, y, _moveVelocity.Z + _externalVelocity.Z) + current + SwimmingRecoil(dt);
        Vector3 beforeMove = GlobalPosition;
        MoveAndSlide();
        KeepInsideArena();
        Vector3 at = GlobalPosition;
        float floor = ReefCavePath.Floor(at) + SwimFloor;
        if (at.Y < floor)
        {
            // A high coastal cliff is a side wall, not a floor that can launch the swimmer above water.
            if (floor > WaterSurfaceHeight.At(at) - BodyHeight)
            {
                GlobalPosition = beforeMove; _moveVelocity = Vector3.Zero;
                Velocity = Velocity with { Y = Mathf.Min(0, Velocity.Y) };
                _swimVerticalVelocity = Mathf.Min(0, _swimVerticalVelocity);
            }
            else { GlobalPosition = at with { Y = floor }; Velocity = Velocity with { Y = Mathf.Max(0f, Velocity.Y) }; _swimVerticalVelocity = Velocity.Y; }
        }
        float line = WaterSurfaceHeight.At(GlobalPosition) - BodyHeight * 0.5f;
        if (GlobalPosition.Y > line)
        {
            if (Velocity.Y >= BreachSpeed)
            {
                IsBreaching = true;
                Breaches++;
                _dashTime = 0f;
                WaterTransitionFx.Spawn(GetParent(), GlobalPosition with { Y = line + BodyHeight * 0.5f }, false, Velocity.Y);
            }
            else { GlobalPosition = GlobalPosition with { Y = line }; Velocity = Velocity with { Y = 0f }; }
        }
        FinishSwimVisuals(wish, speed, alive, dt);
    }

    private void StepBreach(Vector3 wish, float speed, bool dive, float dt)
    {
        _requestedDash = false;
        _moveVelocity = _moveVelocity.MoveToward((wish with { Y = 0f }) * speed, 6f * dt);
        _externalVelocity = _externalVelocity.MoveToward(Vector3.Zero, 12f * dt);
        float vertical = dive ? Mathf.Min(Velocity.Y, -speed) : Velocity.Y;
        Velocity = new Vector3(_moveVelocity.X + _externalVelocity.X, vertical - _gravity * dt,
            _moveVelocity.Z + _externalVelocity.Z);
        MoveAndSlide();
        KeepInsideArena();
        if (CenterPosition.Y <= WaterSurfaceHeight.At(GlobalPosition) && Velocity.Y <= 0f)
        {
            float impact = -Velocity.Y;
            IsBreaching = false;
            WaterEntries++;
            Velocity = Velocity with { Y = Mathf.Max(-6f, Velocity.Y * 0.25f) };
            _swimVerticalVelocity = Velocity.Y;
            _moveVelocity *= 0.8f;
            WaterTransitionFx.Spawn(GetParent(), GlobalPosition with { Y = WaterSurfaceHeight.At(GlobalPosition) }, true, impact);
        }
    }

    private void FinishSwimVisuals(Vector3 wish, float speed, bool alive, float dt)
    {
        PeakSpeed = Mathf.Max(PeakSpeed, Velocity.Length());
        UpdateFacing(wish with { Y = 0f }, dt);
        _pose.Scale = Vector3.One;
        _pose.Rotation = Vector3.Zero;
        _rig.Velocity = Velocity;
        _rig.Grounded = !IsBreaching;
        _rig.Pose = IsBreaching ? RigPose.Air : RigPose.Locomotion;
        SwimmingPresentation.Apply(_rig, true, Velocity, _time, IsBreaching, dt);
        UpdateVisuals(alive, dt);
        UpdateCameraFeel(speed);
    }
}
