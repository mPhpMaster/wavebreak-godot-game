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
    private const int BaseAirJumps = 1;

    /// <summary>Air jumps per airtime: the base one plus any granted by a
    /// character passive (<see cref="ExtraAirJumps"/>).</summary>
    private int AirJumps => BaseAirJumps + ExtraAirJumps;

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
    public void ApplySkin(Color tint, string lookId) => ApplySkin(tint, lookId, Visuals.SkinRecolor.None);

    /// <summary>As above, plus the skin's recolour of the model's own colours
    /// (<see cref="Visuals.SkinRecolor"/>); kept across later <see cref="SetLook"/> calls.</summary>
    public void ApplySkin(Color tint, string lookId, Visuals.SkinRecolor recolor)
    {
        _bodyColor = tint;
        _recolor = recolor;
        SetLook(lookId);
    }

    private Visuals.SkinRecolor _recolor = Visuals.SkinRecolor.None;
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

    /// <summary>Extra air jumps on top of the base one. Set by a character
    /// passive (Meta/CharacterPassives); 0 for everyone else.</summary>
    public int ExtraAirJumps { get; set; }

    /// <summary>Take <paramref name="seconds"/> off the dash cooldown, e.g. a
    /// passive refunding it on kills. Never below zero.</summary>
    public void RefundDash(float seconds) => _dashCooldown = Mathf.Max(0f, _dashCooldown - Mathf.Max(0f, seconds));

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
        CollisionMask = 1u | 64u;

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
        AddChild(new UI.SwimmingHint());
        AddChild(new Visuals.SwimmerWake(this, () => Velocity, () => IsSwimming && !IsBreaching && IsAlive));
        AddChild(new Audio.ReefWaterAudio());
        foreach (string giantArg in OS.GetCmdlineUserArgs())
            if (giantArg.StartsWith("--review-giants=")) AddChild(new Game.GiantVisualReview(this, giantArg["--review-giants=".Length..]));
        foreach (string caveArg in OS.GetCmdlineUserArgs())
            if (caveArg.StartsWith("--review-entrance=")) AddChild(new Game.CaveEntranceReview(this, caveArg["--review-entrance=".Length..]));
        foreach (string perfArg in OS.GetCmdlineUserArgs())
            if (perfArg.StartsWith("--perf-tour=")) AddChild(new Game.TraversalPerfTour(this, perfArg["--perf-tour=".Length..]));
        foreach (string soakArg in OS.GetCmdlineUserArgs())
            if (soakArg.StartsWith("--perf-soak=")) AddChild(new Game.TraversalSoak(this, soakArg["--perf-soak=".Length..]));
        if (OS.GetCmdlineUserArgs().Contains("--check-platform-pursuit")) AddChild(new Game.PlatformPursuitCheck());
        if (OS.GetCmdlineUserArgs().Contains("--check-swim-dash")) AddChild(new Game.SwimmingDashCheck());
        if (OS.GetCmdlineUserArgs().Contains("--check-coop-polish")) AddChild(new Game.CoopPolishCheck());
        if (OS.GetCmdlineUserArgs().Contains("--check-resource-polish")) AddChild(new Game.ResourcePolishCheck());
        if (OS.GetCmdlineUserArgs().Contains("--check-scoreboard")) AddChild(new Game.ScoreboardCheck());
        if (OS.GetCmdlineUserArgs().Contains("--check-overlay-regression")) AddChild(new Game.OverlayRegressionCheck());
        if (OS.GetCmdlineUserArgs().Contains("--check-expedition-weapons")) AddChild(new Game.ExpeditionWeaponsCheck(this));
        if (OS.GetCmdlineUserArgs().Contains("--check-leviathan")) AddChild(new Game.GuardianCheck(this, true));
        if (OS.GetCmdlineUserArgs().Contains("--check-guardian")) AddChild(new Game.GuardianCheck(this));
        if (OS.GetCmdlineUserArgs().Contains("--check-hook-targets")) AddChild(new Game.HookTargetsCheck(this));
        if (OS.GetCmdlineUserArgs().Contains("--check-hook-controls")) AddChild(new Game.HookControlsCheck(this));
        if (OS.GetCmdlineUserArgs().Contains("--check-rooftops")) AddChild(new Game.RooftopCheck(this));
        if (OS.GetCmdlineUserArgs().Contains("--check-dual-hooks")) AddChild(new Game.DualHookCheck(this));
        if (Meta.Launch.MenuExpeditionCheck is not null) AddChild(new Game.ExpeditionMenuRunCheck());
        if (OS.GetCmdlineUserArgs().Contains("--check-city"))
            AddChild(new Game.CityCheck(this, System.Array.Find(OS.GetCmdlineUserArgs(), a => a.StartsWith("--city-capture="))?["--city-capture=".Length..]));
        if (OS.GetCmdlineUserArgs().Contains("--check-swim")) AddChild(new Game.SwimmingCheck(this));
        if (OS.GetCmdlineUserArgs().Contains("--check-breach")) AddChild(new Game.BreachCheck(this));
        if (OS.GetCmdlineUserArgs().Contains("--check-swim-pose")) AddChild(new Game.SwimPoseCheck(this));
        if (OS.GetCmdlineUserArgs().Contains("--check-reef-expedition")) AddChild(new Game.ReefExpeditionCheck(this));
        if (OS.GetCmdlineUserArgs().Contains("--check-cave-ui"))
            AddChild(new Game.CaveUiCheck(this, System.Array.Find(OS.GetCmdlineUserArgs(), a => a.StartsWith("--cave-ui-capture="))?["--cave-ui-capture=".Length..]));
        if (OS.GetCmdlineUserArgs().Contains("--check-reef-systems")) AddChild(new Game.ReefSystemsCheck(this));
        if (OS.GetCmdlineUserArgs().Contains("--coop-check-city")) AddChild(new Game.CoopCityCheck());
        if (OS.GetCmdlineUserArgs().Contains("--coop-check-swim")) AddChild(new Game.CoopSwimmingCheck());
        if (OS.GetCmdlineUserArgs().Contains("--coop-check-reef")) AddChild(new Game.CoopReefHarvestCheck());
        if (OS.GetCmdlineUserArgs().Contains("--coop-check-cave-ui")) AddChild(new Game.CoopCaveUiCheck());
        foreach (string arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--reef-tour=") && arg.Length > "--reef-tour=".Length)
                AddChild(new Game.ReefVisualCapture(arg["--reef-tour=".Length..]));
    }

    /// <summary>Scale gravity for this run (low-gravity challenge). Jumps keep
    /// their apex (v = sqrt(2gh)), they just rise and fall slower.</summary>
    public void SetGravityScale(float scale) => _gravity = ReadGravity() * Mathf.Max(0.1f, scale);

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
        _rig.SetRecolor(_recolor);
        ApplyHat();
    }

    // ---------------------------------------------------------- code requests

    /// <summary>Press jump from code (autopilot, tests). Same buffer as the key.</summary>
    public void RequestJump() => _requestedJump = true;

    /// <summary>Press dash from code.</summary>
    public void RequestDash() => _requestedDash = true;

    /// <summary>Press slide from code: a slide on the ground, a slam in the air.</summary>
    public void RequestSlide() => _requestedSlide = true;

}





