using System.Linq;
using Godot;
using Wavebreak.Combat;
using Wavebreak.Core;
using Wavebreak.Stats;

namespace Wavebreak.Actors;

/// <summary>
/// The player avatar: a kinematic capsule driven by WASD relative to the active
/// camera's yaw, carrying the run's <see cref="StatSheet"/> and its
/// <see cref="PlayerHealth"/>.
///
/// Movement verbs, all on top of the same walk:
///   Space          jump; again in the air for a double jump
///   Shift / RMB    dash — a short burst with i-frames, on Stat.DashCooldown
///   Ctrl / C       slide on the ground (keeps and builds momentum);
///                  in the air, a ground slam that hits everything it lands on
///
/// Momentum is the thread that ties them together. Speed above the walking speed
/// is not clamped away the instant it exists: it bleeds off with friction on the
/// ground and barely at all in the air. So dash -> jump, slide -> jump and
/// slide down a slope all carry speed forward, and chaining them is how you get
/// fast. Walking alone still feels exactly as direct as before.
///
/// The node origin sits at the *feet*, not at the capsule centre. That means a
/// spawner can place the player at ground level with y = 0 and be right, which is
/// worth more than the half-metre of offset arithmetic it costs here.
/// </summary>
public sealed partial class Player : CharacterBody3D, IDamageable
{
    // ------------------------------------------------------------------ shape

    /// <summary>Total capsule height, tip to tip.</summary>
    public const float BodyHeight = 1.8f;

    /// <summary>Capsule radius. Also the horizontal half-width of the avatar,
    /// which enemy melee ranges are implicitly tuned against.</summary>
    public const float BodyRadius = 0.4f;

    // ------------------------------------------------------------------ walk
    // Walking is deliberately snappy: in a game whose defensive layer is "walk
    // out of the way", input latency reads as unfairness. Acceleration is high
    // enough to feel direct but finite so knockback and slopes still register.

    private const float GroundAcceleration = 90f;
    private const float AirAcceleration = 30f;
    private const float TurnSpeed = 14f;

    /// <summary>How fast speed *above* walking speed bleeds off, m/s². Low in the
    /// air is what makes jumping out of a dash or slide worth doing.</summary>
    private const float GroundOverspeedFriction = 16f;
    private const float AirOverspeedDrag = 1.6f;

    /// <summary>How quickly an overspeed velocity can be steered, rad/s. High
    /// enough to correct a line, low enough that momentum still means something.</summary>
    private const float MomentumSteer = 5.5f;

    /// <summary>Absolute ceiling on planar speed. Chained tech should be fast,
    /// not a way to leave the arena in a frame.</summary>
    private const float MaxPlanarSpeed = 34f;

    /// <summary>How fast an external impulse (knockback) bleeds off, in m/s².</summary>
    private const float KnockbackDecay = 26f;

    // ------------------------------------------------------------------ jump

    /// <summary>Grace period after walking off a ledge during which a jump still
    /// counts, and the mirror of it: a jump pressed just before landing.</summary>
    private const float CoyoteTime = 0.12f;
    private const float JumpBufferTime = 0.14f;

    /// <summary>Releasing jump early cuts upward velocity to this fraction, which
    /// is what turns one button into a range of hop heights.</summary>
    private const float ShortHopCut = 0.45f;

    /// <summary>Jumps available after leaving the ground.</summary>
    private const int AirJumps = 1;

    /// <summary>The air jump is a little weaker than the first, so it extends a
    /// jump rather than doubling it.</summary>
    private const float AirJumpHeightScale = 0.7f;

    /// <summary>Fall faster than you rise. Symmetric arcs feel floaty; this is
    /// the single biggest lever on whether a jump feels weighty.</summary>
    private const float FallGravityScale = 1.55f;
    private const float TerminalFallSpeed = 40f;

    // ------------------------------------------------------------------ dash

    private const float DashSpeed = 26f;
    private const float DashDuration = 0.17f;
    private const float DashInvulnerability = 0.24f;

    /// <summary>Speed kept after the burst, as a fraction of walking speed. Above
    /// 1, so a dash flows into momentum instead of hitting a wall at the end.</summary>
    private const float DashExitSpeedScale = 1.55f;

    // ----------------------------------------------------------------- slide

    private const float SlideMinEntrySpeedScale = 0.55f;
    private const float SlideBoostScale = 1.45f;
    private const float SlideFriction = 5.5f;
    private const float SlideSteer = 2.2f;
    private const float SlideMaxDuration = 1.3f;
    private const float SlideCooldown = 0.35f;

    /// <summary>A slide that ends below this fraction of walking speed is over.</summary>
    private const float SlideExitSpeedScale = 0.6f;

    /// <summary>Jumping out of a slide multiplies the carried speed by this.</summary>
    private const float SlideJumpBoost = 1.12f;

    // ------------------------------------------------------------------ slam

    private const float SlamDownSpeed = 42f;
    private const float SlamMinHeight = 1.4f;
    private const float SlamBaseRadius = 4.2f;
    private const float SlamBaseDamage = 14f;
    private const float SlamKnockback = 9f;

    // ---------------------------------------------------------------- palette

    private Color _bodyColor = new(0.28f, 0.76f, 1.00f);

    /// <summary>The look id of the chosen character skin, resolved into a model
    /// by <see cref="Visuals.PlayerLooks"/>.</summary>
    public string LookId { get; private set; } = Visuals.PlayerLooks.Default;

    /// <summary>Applies a character skin: the model by look id, and the tint as
    /// the body colour (it drives the i-frame shimmer, and is the whole body
    /// colour for looks that fall back to a procedural model). Safe before or
    /// after <see cref="_Ready"/>.</summary>
    public void ApplySkin(Color tint, string lookId)
    {
        _bodyColor = tint;
        SetLook(lookId);
    }
    private static readonly Color DashColor = new(0.55f, 0.90f, 1.00f);
    private static readonly Color SlamColor = new(1.00f, 0.72f, 0.30f);

    // --------------------------------------------------------------- identity

    /// <summary>The live player, or null between runs.</summary>
    public static Player? Instance { get; private set; }

    /// <summary>Every number this run. Items add modifiers here; nothing writes
    /// to a field on the player itself.</summary>
    public StatSheet Stats { get; } = new();

    /// <summary>Health, shield, i-frames and regen.</summary>
    public PlayerHealth Health { get; private set; } = null!;

    /// <summary>Camera forward flattened onto the XZ plane and normalised.</summary>
    public Vector3 AimDirection { get; private set; } = Vector3.Forward;

    /// <summary>Mid-torso point. Aim at this, not at <c>GlobalPosition</c>.</summary>
    public Vector3 CenterPosition => GlobalPosition + Vector3.Up * (BodyHeight * 0.5f);

    /// <summary>Horizontal velocity the controller is producing, ignoring knockback.</summary>
    public Vector3 PlanarVelocity => _moveVelocity;

    /// <summary>Seconds until the dash is ready again; 0 means ready.</summary>
    public float DashCooldownRemaining => _dashCooldown;

    /// <summary>Full dash cooldown at the current stats, for a HUD meter.</summary>
    public float DashCooldownTotal => Stats.Get(Stat.DashCooldown);

    public bool IsDashing => _dashTime > 0f;
    public bool IsSliding => _sliding;
    public bool IsSlamming => _slamming;

    /// <summary>Counts of each verb this run, and the fastest planar speed
    /// reached. Printed in the run summary so a headless test can prove the
    /// moves fire, not merely that the code compiles.</summary>
    public int Jumps { get; private set; }
    public int AirJumpsUsed { get; private set; }
    public int Dashes { get; private set; }
    public int Slides { get; private set; }
    public int Slams { get; private set; }
    public int SlamHits { get; private set; }
    public float PeakSpeed { get; private set; }

    /// <summary>
    /// When set, replaces the keyboard as the movement source: a normalised XZ
    /// world direction, or zero to stand still. The autopilot drives this so a
    /// headless run exercises the real movement code rather than teleporting.
    /// </summary>
    public System.Func<Vector3>? MoveOverride { get; set; }

    // ----------------------------------------------------------------- fields

    private Node3D _visual = null!;
    private Node3D _pose = null!;        // squash/lean live here, under the yaw
    private Visuals.CharacterRig _rig = null!; // the model and its procedural animator
    private OmniLight3D _lamp = null!;

    private Camera3D? _camera;
    private Vector3 _moveVelocity;      // XZ, controller-authored (may exceed walk speed)
    private Vector3 _externalVelocity;  // XZ, knockback and shoves
    private float _gravity = 24f;
    private float _yaw;
    private float _coyote;
    private float _jumpBuffer;
    private int _airJumpsLeft;
    private float _lastFallSpeed;

    private float _dashTime;
    private float _dashCooldown;
    private Vector3 _dashDirection;
    private float _afterimageTimer;

    private bool _sliding;
    private float _slideTime;
    private float _slideCooldown;

    private bool _slamming;
    private float _slamStartY;

    // Requests from code (autopilot, tests) that act like a button press.
    private bool _requestedJump;
    private bool _requestedDash;
    private bool _requestedSlide;

    // Pose springs.
    private float _squash;              // + = stretched tall, - = squashed flat
    private float _squashVelocity;
    private Vector3 _lean;              // x = pitch, z = roll, in radians
    private CapsuleMesh? _ghostCapsule;
    private Vector3 _lastPlanar;

    private float _flash;
    private float _deathTilt;
    private float _time;
    private bool _inputAvailable = true;
    private bool _hasDash;
    private bool _hasSlide;
    private bool _hasJump;

    // ------------------------------------------------------------- lifecycle

    public override void _EnterTree()
    {
        Instance = this;
        // PlayerCamera resolves its follow target by group.
        AddToGroup("player");
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    public override void _Ready()
    {
        // Layer 1 / mask 1: the player is blocked by the world but not by the
        // crowd — you can always walk out of a swarm. Contact damage is resolved
        // by distance, not by collision.
        CollisionLayer = 1;
        CollisionMask = 1;

        MotionMode = MotionModeEnum.Grounded;
        UpDirection = Vector3.Up;
        FloorMaxAngle = Mathf.DegToRad(50f);
        FloorSnapLength = 0.4f;
        FloorConstantSpeed = true;
        SlideOnCeiling = true;

        _gravity = ReadGravity();

        // Headless tests may run without the project's input map; asking for a
        // missing action logs an error every frame.
        _inputAvailable = InputMap.HasAction("move_left") && InputMap.HasAction("move_right")
                       && InputMap.HasAction("move_forward") && InputMap.HasAction("move_back");
        _hasJump = InputMap.HasAction("jump");
        _hasDash = InputMap.HasAction("dash");
        _hasSlide = InputMap.HasAction("slide");

        BuildBody();

        // Initialise before AddChild: AddChild runs the child's _Ready
        // immediately, and _Ready is where health reads its max from the sheet.
        Health = new PlayerHealth { Name = "Health" };
        Health.Initialize(this);
        AddChild(Health);

        _airJumpsLeft = AirJumps;
    }

    private static float ReadGravity()
    {
        Variant setting = ProjectSettings.GetSetting("physics/3d/default_gravity");
        return setting.VariantType == Variant.Type.Nil ? 24f : setting.AsSingle();
    }

    // ------------------------------------------------------------------ body

    /// <summary>
    /// Builds the avatar. The model (a <see cref="Visuals.CharacterRig"/>, which
    /// animates its own limbs) hangs under two nodes: the yaw node turns it to
    /// face the heading, and the pose node underneath bends it — squash, stretch
    /// and lean — so neither pose nor limb animation ever fights with facing.
    /// </summary>
    private void BuildBody()
    {
        AddChild(new CollisionShape3D
        {
            Name = "Collider",
            Shape = new CapsuleShape3D { Radius = BodyRadius, Height = BodyHeight },
            Position = new Vector3(0f, BodyHeight * 0.5f, 0f),
        });

        _visual = new Node3D { Name = "Visual" };
        AddChild(_visual);

        _pose = new Node3D { Name = "Pose" };
        _visual.AddChild(_pose);

        SetLook(LookId);

        _lamp = new OmniLight3D
        {
            Name = "Lamp",
            Position = new Vector3(0f, BodyHeight + 0.4f, 0f),
            LightColor = new Color(0.65f, 0.86f, 1f),
            LightEnergy = 1.5f,
            OmniRange = 14f,
            OmniAttenuation = 1.4f,
            ShadowEnabled = false,
        };
        AddChild(_lamp);
    }

    /// <summary>
    /// Wear a different look (character select, unlocks). Only the model under
    /// the pose node is replaced; collision, facing and the pose springs stay, so
    /// this is safe mid-run.
    /// </summary>
    public void SetLook(string lookId)
    {
        LookId = lookId;
        if (_pose is null) return; // applied in BuildBody
        if (_rig is not null && IsInstanceValid(_rig)) _rig.QueueFree();
        _rig = Visuals.CharacterRig.Create(Visuals.PlayerLooks.Get(lookId));
        // The controller knows its state better than position deltas could tell
        // it (a dash and a knockback move the body the same way).
        _rig.ManualMotion = true;
        _pose.AddChild(_rig);
        _rig.EnableShadow();
    }

    // ---------------------------------------------------------- code requests

    /// <summary>Press jump from code (autopilot, tests). Same buffer as the key.</summary>
    public void RequestJump() => _requestedJump = true;

    /// <summary>Press dash from code.</summary>
    public void RequestDash() => _requestedDash = true;

    /// <summary>Press slide from code: a slide on the ground, a slam in the air.</summary>
    public void RequestSlide() => _requestedSlide = true;

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
        float dt = (float)delta;
        _time += dt;

        UpdateAim();
        ProbeAnimation();

        bool alive = Health is null || Health.IsAlive;
        bool grounded = IsOnFloor();
        Vector3 velocity = Velocity;

        if (grounded) _airJumpsLeft = AirJumps;

        _dashCooldown = Mathf.Max(0f, _dashCooldown - dt);
        _slideCooldown = Mathf.Max(0f, _slideCooldown - dt);

        Vector3 wish = alive ? ReadMoveInput() : Vector3.Zero;
        float walk = Stats.Get(Stat.MoveSpeed);

        bool dashPressed = alive && Pressed("dash", _hasDash, ref _requestedDash);
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

        _moveVelocity = _moveVelocity.LimitLength(MaxPlanarSpeed);
        PeakSpeed = Mathf.Max(PeakSpeed, _moveVelocity.Length());

        Velocity = new Vector3(
            _moveVelocity.X + _externalVelocity.X,
            velocity.Y,
            _moveVelocity.Z + _externalVelocity.Z);

        if (!grounded) _lastFallSpeed = Mathf.Max(0f, -Velocity.Y);

        MoveAndSlide();

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

    private bool Pressed(string action, bool available, ref bool requested)
    {
        bool pressed = requested || (_inputAvailable && available && Input.IsActionJustPressed(action));
        requested = false;
        return pressed;
    }

    // ------------------------------------------------------------------ walk

    /// <summary>
    /// Ordinary movement, with momentum. Below walking speed it is the original
    /// snappy controller. Above it — after a dash, slide or slam bounce — the
    /// extra speed is steered toward the input rather than discarded, and bleeds
    /// off at a rate that depends on whether you are on the ground.
    /// </summary>
    private void UpdateWalk(Vector3 wish, float walk, bool grounded, float dt)
    {
        Vector3 target = wish * walk;
        float speed = _moveVelocity.Length();

        if (speed <= walk + 0.05f)
        {
            float acceleration = grounded ? GroundAcceleration : AirAcceleration;
            _moveVelocity = _moveVelocity.MoveToward(target, acceleration * dt);
            return;
        }

        float drag = grounded ? GroundOverspeedFriction : AirOverspeedDrag;
        // Letting go of the keys on the ground is a deliberate stop: brake hard.
        if (grounded && wish.LengthSquared() < 0.01f) drag *= 2.5f;

        float newSpeed = Mathf.Max(walk, speed - drag * dt);
        Vector3 dir = _moveVelocity / speed;
        if (wish.LengthSquared() > 0.01f)
            dir = RotateToward(dir, wish.Normalized(), MomentumSteer * dt);

        _moveVelocity = dir * newSpeed;
    }

    // ------------------------------------------------------------------ dash

    private void TryDash(Vector3 wish, float walk)
    {
        if (_dashCooldown > 0f || _dashTime > 0f) return;

        Vector3 dir = wish.LengthSquared() > 0.01f ? wish.Normalized()
                    : _moveVelocity.LengthSquared() > 0.25f ? _moveVelocity.Normalized()
                    : FacingDirection();

        _sliding = false;
        _slamming = false;
        _dashDirection = dir;
        _dashTime = DashDuration;
        _dashCooldown = Mathf.Max(0.05f, Stats.Get(Stat.DashCooldown));
        _afterimageTimer = 0f;
        Dashes++;

        Health?.GrantInvulnerability(DashInvulnerability);

        _squash = -0.18f;
        _squashVelocity = 0f;
        PlayerCamera.Instance?.KickFov(9f);
        PlayerCamera.Instance?.Shake(0.05f, 0.12f);
        FlashHit(0.25f);
        EventBus.Publish(new PlayerMoved(MoveVerb.Dash, GlobalPosition));
    }

    // ----------------------------------------------------------------- slide

    private void TryStartSlide(float walk)
    {
        if (_slideCooldown > 0f) return;

        float speed = _moveVelocity.Length();
        if (speed < walk * SlideMinEntrySpeedScale) return;

        Vector3 dir = _moveVelocity / speed;
        _moveVelocity = dir * Mathf.Max(speed, walk) * SlideBoostScale;
        _sliding = true;
        _slideTime = 0f;
        Slides++;
        _squash = -0.1f;
        PlayerCamera.Instance?.KickFov(5f);
        SpawnDust(GlobalPosition, 0.9f, 0.35f);
        EventBus.Publish(new PlayerMoved(MoveVerb.Slide, GlobalPosition));
    }

    /// <summary>
    /// A slide keeps the speed it was entered with and loses it slowly. Going
    /// downhill adds speed along the slope, which is what makes terrain a
    /// resource. Ends on release, when too slow, off a ledge, or after a while.
    /// </summary>
    private void UpdateSlide(Vector3 wish, float walk, bool grounded, bool held, float dt)
    {
        _slideTime += dt;

        float speed = _moveVelocity.Length();
        Vector3 dir = speed > 0.01f ? _moveVelocity / speed : FacingDirection();

        if (wish.LengthSquared() > 0.01f)
            dir = RotateToward(dir, wish.Normalized(), SlideSteer * dt);

        // Friction ramps up late in the slide so it ends on its own.
        float friction = SlideFriction * (1f + Mathf.Max(0f, _slideTime - SlideMaxDuration * 0.6f) * 4f);
        speed = Mathf.Max(0f, speed - friction * dt);

        if (grounded)
        {
            Vector3 n = GetFloorNormal();
            Vector3 downhill = new Vector3(n.X, 0f, n.Z);
            if (downhill.LengthSquared() > 0.0004f)
            {
                // Gravity's component along the floor, projected onto our heading.
                float slope = downhill.Length();
                speed += _gravity * slope * Mathf.Max(0f, dir.Dot(downhill.Normalized())) * dt;
            }
        }

        _moveVelocity = dir * speed;

        bool tooSlow = speed < walk * SlideExitSpeedScale;
        bool released = _inputAvailable && _hasSlide && !held && _slideTime > 0.15f && MoveOverride is null;
        if (!grounded || tooSlow || released || _slideTime > SlideMaxDuration * 1.6f)
            EndSlide();
    }

    private void EndSlide()
    {
        if (!_sliding) return;
        _sliding = false;
        _slideCooldown = SlideCooldown;
    }

    // ------------------------------------------------------------------ slam

    private void StartSlam()
    {
        _slamming = true;
        _sliding = false;
        _dashTime = 0f;
        _slamStartY = GlobalPosition.Y;
        Slams++;
        _squash = 0.25f;
        PlayerCamera.Instance?.KickFov(-4f);
        EventBus.Publish(new PlayerMoved(MoveVerb.SlamStart, GlobalPosition));
    }

    /// <summary>
    /// The slam lands: everything in the ring takes damage scaled by how far you
    /// fell and by the run's damage stat, and is thrown outward. Hitting enemies
    /// also gives a small bounce, so a slam into a crowd flows into the next move.
    /// </summary>
    private void FinishSlam()
    {
        _slamming = false;

        float drop = Mathf.Max(0f, _slamStartY - GlobalPosition.Y);
        float heightBonus = 1f + Mathf.Min(drop, 12f) * 0.08f;
        float radius = SlamBaseRadius * Stats.Get(Stat.AreaSize) * (0.85f + 0.15f * heightBonus);
        float amount = SlamBaseDamage * Stats.GetMultiplier(Stat.Damage) * heightBonus;

        int hits = 0;
        Vector3 at = GlobalPosition;
        if (EnemyManager.Instance is { } em)
        {
            foreach (Enemy e in em.AliveList.ToArray())
            {
                if (!IsInstanceValid(e) || !e.IsAlive) continue;
                Vector3 d = e.GlobalPosition - at;
                d.Y = 0f;
                float dist = d.Length();
                if (dist > radius) continue;

                var info = new DamageInfo
                {
                    Amount = amount * (1f - 0.4f * dist / radius),
                    Knockback = SlamKnockback * Stats.Get(Stat.Knockback),
                    Direction = dist > 0.01f ? d / dist : Vector3.Forward,
                    Source = "slam",
                    CanCrit = false,
                };
                e.TakeDamage(info);
                if (info.Nullified || info.Amount <= 0f) continue;

                hits++;
                SlamHits++;
                EventBus.Publish(new EnemyDamaged(e, info));
                EventBus.Publish(new PopupRequested(
                    e.AimPoint + Vector3.Up * 0.35f,
                    Mathf.RoundToInt(Mathf.Max(1f, info.Amount)).ToString(),
                    SlamColor, 1.15f));
            }
        }

        if (hits > 0)
        {
            // Pop back up off the crowd; keeps the air jump available too.
            Velocity = Velocity with { Y = Mathf.Sqrt(2f * _gravity * 1.3f) };
            _airJumpsLeft = AirJumps;
        }

        _squash = -0.45f;
        _squashVelocity = 0f;
        PlayerCamera.Instance?.Shake(0.22f + 0.02f * Mathf.Min(hits, 8), 0.3f);
        PlayerCamera.Instance?.KickFov(6f);
        SpawnShockwave(at, radius);
        FlashHit(0.5f);
        // Strength is crowd size: a slam into nothing is a thud, into a pack a boom.
        EventBus.Publish(new PlayerMoved(MoveVerb.SlamImpact, at, Mathf.Min(hits, 8) / 8f));
    }

    // ------------------------------------------------------------------ jump

    private float ApplyJump(float verticalVelocity, bool grounded, Vector3 wish, float walk, float dt)
    {
        _coyote = grounded ? CoyoteTime : Mathf.Max(0f, _coyote - dt);

        bool pressed = _requestedJump
                       || (_inputAvailable && _hasJump && Input.IsActionJustPressed("jump"));
        _requestedJump = false;
        _jumpBuffer = pressed ? JumpBufferTime : Mathf.Max(0f, _jumpBuffer - dt);

        float apex = Mathf.Max(0.01f, Stats.Get(Stat.JumpHeight));

        if (_jumpBuffer > 0f && _coyote > 0f)
        {
            // Stat.JumpHeight is the apex in metres: v = sqrt(2gh).
            verticalVelocity = Mathf.Sqrt(2f * _gravity * apex);
            _jumpBuffer = 0f;
            _coyote = 0f;
            Jumps++;

            if (_sliding)
            {
                // The slide-hop: leave the ground carrying the slide's speed and a
                // little more. This is the core of moving fast.
                _moveVelocity *= SlideJumpBoost;
                EndSlide();
                _slideCooldown = 0f;
            }

            _squash = 0.3f;
            _squashVelocity = 0f;
            SpawnDust(GlobalPosition, 0.7f, 0.3f);
            EventBus.Publish(new PlayerMoved(MoveVerb.Jump, GlobalPosition));
        }
        else if (_jumpBuffer > 0f && !grounded && _airJumpsLeft > 0 && _dashTime <= 0f)
        {
            _airJumpsLeft--;
            AirJumpsUsed++;
            verticalVelocity = Mathf.Sqrt(2f * _gravity * apex * AirJumpHeightScale);
            _jumpBuffer = 0f;

            // An air jump lets you redirect: snap toward the input without
            // losing the speed you had.
            float speed = _moveVelocity.Length();
            if (wish.LengthSquared() > 0.01f)
                _moveVelocity = wish.Normalized() * Mathf.Max(speed, walk * wish.Length());

            _squash = 0.22f;
            _squashVelocity = 0f;
            _rig.PlayFlip(); // the double jump reads as a somersault
            SpawnRing(GlobalPosition, 1.1f, DashColor, 0.25f);
            EventBus.Publish(new PlayerMoved(MoveVerb.AirJump, GlobalPosition));
        }
        else if (verticalVelocity > 0f && _inputAvailable && _hasJump
                 && !Input.IsActionPressed("jump") && MoveOverride is null)
        {
            verticalVelocity *= 1f - (1f - ShortHopCut) * Mathf.Min(1f, dt * 30f);
        }

        return verticalVelocity;
    }

    private void OnLanded(float fallSpeed)
    {
        if (_slamming)
        {
            FinishSlam();
            return;
        }

        float impact = Mathf.Clamp(fallSpeed / 22f, 0f, 1f);
        if (impact < 0.15f) return;

        _squash = -0.32f * impact;
        _squashVelocity = 0f;
        if (impact > 0.55f) PlayerCamera.Instance?.Shake(0.05f + 0.08f * impact, 0.18f);
        SpawnDust(GlobalPosition, 0.6f + impact * 0.8f, 0.3f);
        // Same threshold as the dust: a hop's landing stays silent, a real drop
        // is heard. Listeners scale by impact rather than re-deriving it.
        EventBus.Publish(new PlayerMoved(MoveVerb.Land, GlobalPosition, impact));
    }

    // ------------------------------------------------------------------ aim

    private Vector3 ReadMoveInput()
    {
        if (MoveOverride is not null)
        {
            Vector3 driven = MoveOverride() with { Y = 0f };
            return driven.LengthSquared() > 1f ? driven.Normalized() : driven;
        }

        if (!_inputAvailable) return Vector3.Zero;

        Vector2 input = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
        if (input.LengthSquared() < 0.0001f) return Vector3.Zero;

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

    // ------------------------------------------------------------------ pose

    private void UpdateFacing(Vector3 wish, float dt)
    {
        Vector3 heading = _dashTime > 0f ? _dashDirection
                        : _moveVelocity.LengthSquared() > 0.25f ? _moveVelocity
                        : wish.LengthSquared() > 0.01f ? wish
                        : Vector3.Zero;

        if (heading != Vector3.Zero)
        {
            float target = Mathf.Atan2(-heading.X, -heading.Z);
            float rate = _sliding ? TurnSpeed * 0.5f : TurnSpeed;
            _yaw = Mathf.LerpAngle(_yaw, target, 1f - Mathf.Exp(-rate * dt));
        }

        _visual.Rotation = new Vector3(-_deathTilt * 1.45f, _yaw, 0f);
    }

    /// <summary>
    /// The body bends to explain the motion: it stretches leaving the ground and
    /// squashes landing (a damped spring, so it overshoots a little and settles)
    /// and leans into acceleration. None of it touches collision — it is all on
    /// the pose node, under the yaw.
    ///
    /// Two layers share the work. This one is whole-body and physical (springs,
    /// lean); the rig underneath does the limbs and the verb poses — running
    /// stride, slide crouch, slam tuck, dash stretch, double-jump flip. The verb
    /// leans here are therefore gentler than they were for the capsule: the rig
    /// already folds the body, and the two would otherwise add up to a faceplant.
    /// </summary>
    private void UpdatePose(bool grounded, float walk, float dt)
    {
        // Spring toward the rest pose (0); the rig crouches for the slide itself.
        float rest = _sliding ? -0.12f : _slamming ? 0.1f : 0f;
        const float stiffness = 260f, damping = 18f;
        _squashVelocity += (stiffness * (rest - _squash) - damping * _squashVelocity) * dt;
        _squash += _squashVelocity * dt;
        _squash = Mathf.Clamp(_squash, -0.6f, 0.5f);

        // Acceleration in the body's own frame drives the lean.
        Vector3 planar = _moveVelocity;
        Vector3 accel = dt > 0f ? (planar - _lastPlanar) / dt : Vector3.Zero;
        _lastPlanar = planar;

        Basis yawBasis = new(Vector3.Up, _yaw);
        Vector3 local = yawBasis.Inverse() * accel;
        float speed01 = Mathf.Clamp(planar.Length() / Mathf.Max(1f, walk), 0f, 2.5f);

        // Forward acceleration pitches the top forward (-Z is forward, so a
        // negative local Z is "speeding up").
        float pitch = Mathf.Clamp(local.Z * 0.012f, -0.35f, 0.35f) - speed01 * 0.05f;
        float roll = Mathf.Clamp(-local.X * 0.012f, -0.35f, 0.35f);
        if (_sliding) pitch = 0.15f;          // the rig leans back into the slide
        if (_dashTime > 0f) pitch = -0.25f;   // and throws the chest into the dash
        if (_slamming) pitch = -0.15f;

        Vector3 targetLean = new(pitch, 0f, roll);
        _lean = _lean.Lerp(targetLean, 1f - Mathf.Exp(-12f * dt));

        float stretch = 1f + _squash;
        float widen = 1f / Mathf.Sqrt(Mathf.Max(0.3f, stretch));
        _pose.Scale = new Vector3(widen, stretch, widen);
        _pose.Rotation = new Vector3(_lean.X, 0f, _lean.Z);

        // Footsteps and stride come from the rig, which is told what the
        // controller is doing rather than guessing from position changes.
        _rig.Velocity = new Vector3(_moveVelocity.X, Velocity.Y, _moveVelocity.Z);
        _rig.Grounded = grounded && !_slamming;
        _rig.Pose = _dashTime > 0f ? Visuals.RigPose.Dash
                  : _slamming ? Visuals.RigPose.Slam
                  : _sliding ? Visuals.RigPose.Slide
                  : !grounded ? Visuals.RigPose.Air
                  : Visuals.RigPose.Locomotion;
    }

    private void UpdateVisuals(bool alive, float dt)
    {
        _flash = Mathf.MoveToward(_flash, 0f, dt * 3.5f);

        float iframes = Health is not null && Health.InvulnerableFor > 0f
            ? 0.5f + 0.5f * Mathf.Sin(_time * 26f)
            : 0f;

        // Verb tints as a glow on the model (dash streak, slam heat), the i-frame
        // shimmer as a pulsing glow in the skin's colour, and hits as the rig's
        // white flash.
        if (_dashTime > 0f) _rig.SetGlow(DashColor, 1.6f);
        else if (_slamming) _rig.SetGlow(SlamColor, 1.2f);
        else if (iframes > 0f) _rig.SetGlow(_bodyColor, iframes * 0.9f);
        else _rig.SetGlow(_bodyColor, 0f);
        if (_flash > 0.05f) _rig.SetFlash(Mathf.Min(1f, _flash));

        if (!alive)
        {
            _deathTilt = Mathf.MoveToward(_deathTilt, 1f, dt * 2.2f);
            _lamp.LightEnergy = Mathf.MoveToward(_lamp.LightEnergy, 0.25f, dt * 1.5f);
        }
        else
        {
            _lamp.LightEnergy = 1.5f + _flash * 2.5f;
        }
    }

    private void UpdateCameraFeel(float walk)
    {
        if (PlayerCamera.Instance is not { } rig) return;
        float over = _moveVelocity.Length() - walk;
        rig.FovBoost = Mathf.Max(0f, over) * 0.9f;
    }

    // -------------------------------------------------------------------- fx

    private Node? FxParent => GetParent();

    private void SpawnAfterimage(float dt)
    {
        _afterimageTimer -= dt;
        if (_afterimageTimer > 0f || FxParent is null) return;
        _afterimageTimer = 0.03f;

        var mat = new StandardMaterial3D
        {
            AlbedoColor = DashColor with { A = 0.45f },
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            EmissionEnabled = true,
            Emission = DashColor,
        };
        // The ghost is the hero's own silhouette when the model has one (the
        // procedural looks bake it), and a capsule for imported models.
        Mesh? silhouette = _rig.SilhouetteMesh;
        _ghostCapsule ??= new CapsuleMesh { Radius = BodyRadius, Height = BodyHeight, RadialSegments = 10, Rings = 3 };
        var ghost = new MeshInstance3D
        {
            Mesh = silhouette ?? _ghostCapsule,
            MaterialOverride = mat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        FxParent.AddChild(ghost);
        ghost.GlobalTransform = silhouette is not null
            ? _rig.GlobalTransform
            : _pose.GlobalTransform.TranslatedLocal(Vector3.Up * (BodyHeight * 0.5f));
        Fade(ghost, mat, 0.22f, 0.85f);
    }

    private void SpawnDust(Vector3 at, float size, float seconds) =>
        SpawnRing(at, size, new Color(0.75f, 0.78f, 0.85f), seconds);

    private void SpawnShockwave(Vector3 at, float radius) =>
        SpawnRing(at, radius, SlamColor, 0.4f, startScale: 0.2f, thickness: 0.35f);

    /// <summary>A flat ring on the ground that grows and fades: dust for hops and
    /// landings, the shockwave for a slam. One primitive, three readings.</summary>
    private void SpawnRing(Vector3 at, float radius, Color color, float seconds,
                           float startScale = 0.4f, float thickness = 0.18f)
    {
        if (FxParent is null) return;

        var mat = new StandardMaterial3D
        {
            AlbedoColor = color with { A = 0.7f },
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        var ring = new MeshInstance3D
        {
            Mesh = new TorusMesh
            {
                InnerRadius = Mathf.Max(0.05f, 1f - thickness),
                OuterRadius = 1f,
                Rings = 24,
                RingSegments = 6,
            },
            MaterialOverride = mat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        FxParent.AddChild(ring);
        ring.GlobalPosition = at + Vector3.Up * 0.06f;
        ring.Scale = new Vector3(radius * startScale, 0.15f, radius * startScale);

        Tween t = ring.CreateTween();
        t.SetParallel();
        t.TweenProperty(ring, "scale", new Vector3(radius, 0.15f, radius), seconds)
         .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        t.TweenProperty(mat, "albedo_color:a", 0f, seconds);
        t.Chain().TweenCallback(Callable.From(ring.QueueFree));
    }

    private static void Fade(Node3D node, StandardMaterial3D mat, float seconds, float endScale)
    {
        Tween t = node.CreateTween();
        t.SetParallel();
        t.TweenProperty(mat, "albedo_color:a", 0f, seconds);
        t.TweenProperty(node, "scale", node.Scale * endScale, seconds);
        t.Chain().TweenCallback(Callable.From(node.QueueFree));
    }

    // ---------------------------------------------------------------- effects

    /// <summary>White-hot pop on the body.</summary>
    public void FlashHit(float strength = 1f) => _flash = Mathf.Max(_flash, Mathf.Clamp(strength, 0f, 1f));

    /// <summary>
    /// Shove the player. The impulse decays on its own and is kept separate from
    /// walking speed, so being hit displaces you without taking control away.
    /// </summary>
    public void ApplyKnockback(Vector3 impulse)
    {
        impulse.Y = 0f;
        if (impulse.LengthSquared() < 0.0001f) return;
        _externalVelocity = (_externalVelocity + impulse).LimitLength(14f);
    }

    /// <summary>Places the player and clears all momentum and movement state.</summary>
    public void Teleport(Vector3 position)
    {
        GlobalPosition = position;
        Velocity = Vector3.Zero;
        _moveVelocity = Vector3.Zero;
        _externalVelocity = Vector3.Zero;
        _lastPlanar = Vector3.Zero;
        _dashTime = 0f;
        _sliding = false;
        _slamming = false;
        _airJumpsLeft = AirJumps;
    }

    // ------------------------------------------------------------ IDamageable

    public void TakeDamage(DamageInfo info)
    {
        if (Health is null)
        {
            info.Nullified = true;
            return;
        }
        Health.TakeDamage(info);
    }

    public bool IsAlive => Health is null || Health.IsAlive;

    // Explicit: Node3D.Position is the *local* one.
    Vector3 IDamageable.Position => CenterPosition;
}
