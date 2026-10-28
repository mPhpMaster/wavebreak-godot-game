using Godot;
using Wavebreak.Data;
using Wavebreak.Game;
using Wavebreak.Stats;
using Wavebreak.Visuals;

namespace Wavebreak.Actors;

public sealed partial class Player
{
    private readonly HookAttachment[] _hooks = { new(), new() };
    private readonly GrappleTether?[] _cables = new GrappleTether?[2];
    public bool LeftHookAttached => _hooks[0].Attached;
    public bool RightHookAttached => _hooks[1].Attached;
    public Vector3 LeftHookAnchor => _hooks[0].Anchor;
    public Vector3 RightHookAnchor => _hooks[1].Anchor;
    public bool Grappling => LeftHookAttached || RightHookAttached;
    public Vector3 HookAnchor => LeftHookAttached ? LeftHookAnchor : RightHookAnchor;
    public bool HookTargetAvailable { get; private set; }
    public int HookBoosts { get; private set; }
    public float HookBoostCooldown => _boostCooldown;
    public bool HookBoosting => _boostWindow > 0f;
    private bool _cityBallistic, _cityMode;
    private float _cityPitch, _aimIn, _boostCooldown, _boostWindow;
    private readonly Vector3[] _aimAnchors = new Vector3[2];
    private readonly bool[] _aimAvailable = new bool[2];

    public void ReleaseHook()
    {
        ReleaseHook(HookSide.Left); ReleaseHook(HookSide.Right);
    }

    public void ReleaseHook(HookSide side)
    {
        bool was = Grappling;
        _hooks[(int)side].Release();
        _cables[(int)side]?.Set(Vector3.Zero, Vector3.Zero, false);
        if (was && !Grappling) _cityBallistic = !IsOnFloor();
    }

    internal bool AttachHook(Vector3 anchor) => AttachHook(HookSide.Left, anchor);
    internal bool AttachHook(HookSide side, Vector3 anchor)
    {
        if (!IsAlive || !GrappleAnchors.Valid(this, CenterPosition, anchor)) return false;
        HookAttachment hook = _hooks[(int)side];
        hook.Anchor = anchor; hook.Length = CenterPosition.DistanceTo(anchor);
        hook.Attached = true; hook.SightIn = 0.08f;
        _sliding = _slamming = false; _dashTime = 0f;
        return true;
    }

    private bool StepCityTraversal(float dt)
    {
        bool city = Arena.IsCity;
        if (city && IsSwimming) UpdateSwimmingMode();
        if (_cityMode != city)
        {
            _cityMode = city; ReleaseHook(); _aimIn = 0f;
            if (PlayerCamera.Instance is { } cam)
            {
                cam.SetCityCollision(city);
                if (city) { _cityPitch = cam.MaxPitchDegrees; cam.MaxPitchDegrees = 82f; }
                else { cam.MaxPitchDegrees = _cityPitch; cam.SetPitchDegrees(Mathf.RadToDeg(cam.Pitch)); }
            }
        }
        if (!city) { _cityBallistic = false; return false; }
        if (IsOnFloor() && !Grappling) _cityBallistic = false;
        _boostCooldown = Mathf.Max(0f, _boostCooldown - dt);
        _boostWindow = Mathf.Max(0f, _boostWindow - dt);
        bool control = IsAlive && !Net.Coop.LocalHold && !GetTree().Paused;
        UpdateHooks(control, dt);
        StepCoreStrike(dt);
        _cityDashHandled = false;
        if (Grappling)
        {
            _dashCooldown = Mathf.Max(0f, _dashCooldown - dt);
            if (control && ReadCityDash())
            {
                _cityDashHandled = true;
                TryDash(ReadMoveInput(), Stats.Get(Stat.MoveSpeed));
            }
        }
        if (!Grappling) { DrawHooks(); return false; }
        bool jump = control && (_requestedJump || Input.IsActionJustPressed("jump"));
        _requestedJump = false;
        Vector3 wish = ReadMoveInput();
        Vector3 velocity = GrapplePhysics.StepDual(CenterPosition, Velocity, _hooks[0], _hooks[1], wish,
            _gravity, dt, HookBoosting ? GrapplePhysics.BoostSpeed : GrapplePhysics.MaxSpeed);
        if (jump && _boostCooldown <= 0f)
        {
            Vector3 direction = velocity.LengthSquared() > 25f ? velocity.Normalized() : (HookAnchor - CenterPosition).Normalized();
            velocity = (velocity + direction * 18f + Vector3.Up * 7f).LimitLength(GrapplePhysics.BoostSpeed);
            _boostCooldown = 2.2f; _boostWindow = 0.45f; HookBoosts++;
            Core.EventBus.Publish(new Core.PlayerMoved(Core.MoveVerb.Dash, GlobalPosition));
            PlayerCamera.Instance?.Shake(0.035f, 0.12f);
        }
        Velocity = velocity;
        _moveVelocity = velocity with { Y = 0f }; _externalVelocity = Vector3.Zero;
        _lastFallSpeed = Mathf.Max(0f, -velocity.Y);
        MoveAndSlide(); KeepInsideArena();
        _moveVelocity = Velocity with { Y = 0f };
        PeakSpeed = Mathf.Max(PeakSpeed, _moveVelocity.Length());
        UpdateFacing(wish, dt); UpdatePose(IsOnFloor(), Stats.Get(Stat.MoveSpeed), dt);
        UpdateVisuals(IsAlive, dt); UpdateCameraFeel(Stats.Get(Stat.MoveSpeed));
        DrawHooks(); return true;
    }

    private void UpdateHooks(bool control, float dt)
    {
        _aimIn -= dt;
        bool pressed = Input.IsActionJustPressed("city_hook_left") || Input.IsActionJustPressed("city_hook_right");
        if (_aimIn <= 0f || pressed)
        {
            _aimIn = 1f / 15f;
            Camera3D? camera = GetViewport().GetCamera3D();
            for (int i = 0; i < 2; i++)
                _aimAvailable[i] = control && camera is not null && CityRooftops.Find(this,
                    CenterPosition, -camera.GlobalBasis.Z, (HookSide)i, out _aimAnchors[i],
                    _hooks[1 - i].Attached ? _hooks[1 - i].Anchor : null);
            HookTargetAvailable = _aimAvailable[0] || _aimAvailable[1];
        }
        for (int i = 0; i < 2; i++)
        {
            HookAttachment hook = _hooks[i];
            bool held = control && Input.IsActionPressed(i == 0 ? "city_hook_left" : "city_hook_right");
            if (!held) { hook.BlockedUntilRelease = false; ReleaseHook((HookSide)i); continue; }
            if (!hook.Attached && !hook.BlockedUntilRelease && _aimAvailable[i]) AttachHook((HookSide)i, _aimAnchors[i]);
            if (!hook.Attached) continue;
            hook.SightIn -= dt;
            if (CenterPosition.DistanceSquaredTo(hook.Anchor) > (GrapplePhysics.Range + 2f) * (GrapplePhysics.Range + 2f)
                || hook.SightIn <= 0f && !GrappleAnchors.Valid(this, CenterPosition, hook.Anchor))
            { ReleaseHook((HookSide)i); hook.BlockedUntilRelease = true; }
            if (hook.SightIn <= 0f) hook.SightIn = 0.08f;
        }
    }

    public Vector3 HookOrigin(HookSide side)
    {
        Vector3 right = new(Mathf.Cos(_yaw), 0f, -Mathf.Sin(_yaw));
        return CenterPosition + right * (side == HookSide.Left ? -0.32f : 0.32f);
    }

    private void DrawHooks()
    {
        for (int i = 0; i < 2; i++)
        {
            if (_cables[i] is null && _hooks[i].Attached)
            {
                _cables[i] = new GrappleTether { Name = i == 0 ? "GrappleCable" : "RightGrappleCable" };
                AddChild(_cables[i]!);
            }
            _cables[i]?.Set(HookOrigin((HookSide)i), _hooks[i].Anchor, _hooks[i].Attached && IsAlive);
        }
    }
}
