using Godot;

namespace Wavebreak.Actors;

/// <summary>
/// The run's third-person camera.
///
/// The rig is three nodes deep and each level has exactly one job:
/// <code>
/// PlayerCamera (this)   chases the player's position, carries yaw
///  └─ SpringArm3D       carries pitch, pulls in when the world blocks the view
///      └─ ArmEnd        moved by the arm; nothing of ours lives here
///          └─ Camera3D  local transform is reserved for screen shake
/// </code>
///
/// The <c>ArmEnd</c> spacer is not decoration. <see cref="SpringArm3D"/> rewrites
/// the <em>entire</em> origin of every direct child each frame, so a shake offset
/// written onto the camera directly would be erased before it was ever drawn.
/// Giving the arm a spacer to move leaves the camera's own local transform free.
///
/// Splitting follow (this node) from shake (the camera) is also what keeps the two
/// from fighting: the smoothing below chases a target the shake never perturbs, so
/// a violent shake cannot drag the rig off the player or bleed into the next frame's
/// lerp. Shake is purely a read-only garnish on the last node in the chain.
///
/// The children are built in <see cref="_Ready"/> when the scene does not already
/// supply them, so dropping a bare PlayerCamera into a scene yields a working camera
/// with no wiring. It also sets itself <see cref="Node3D.TopLevel"/>, so it behaves
/// identically whether it is parented to the world or to the player it follows.
/// </summary>
[GlobalClass]
public partial class PlayerCamera : Node3D
{
    /// <summary>
    /// The camera driving the current run, or null before one enters the tree.
    /// Systems that want feedback (<see cref="Shake"/>) or the aim basis
    /// (<see cref="FlatForward"/>) go through here rather than holding a reference.
    /// </summary>
    public static PlayerCamera? Instance { get; private set; }

    // ---- target ----

    /// <summary>Explicit follow target. Left empty, the camera finds
    /// <see cref="TargetGroup"/> instead, which is how it survives a player that
    /// spawns after the camera does.</summary>
    [Export] public NodePath? TargetPath { get; set; }

    /// <summary>Group searched for a follow target when <see cref="TargetPath"/> is unset.</summary>
    [Export] public StringName TargetGroup { get; set; } = "player";

    /// <summary>Height above the target's origin that the rig actually orbits.
    /// Aiming at the feet makes a third-person camera feel like it is falling over.</summary>
    [Export] public float HeightOffset { get; set; } = 1.6f;

    // ---- framing ----

    [Export(PropertyHint.Range, "1,40,0.1")] public float Distance { get; set; } = 9.5f;
    [Export(PropertyHint.Range, "1,40,0.1")] public float MinDistance { get; set; } = 4.0f;
    [Export(PropertyHint.Range, "1,40,0.1")] public float MaxDistance { get; set; } = 16.0f;

    /// <summary>Metres of zoom per wheel notch.</summary>
    [Export] public float ZoomStep { get; set; } = 1.0f;

    [Export(PropertyHint.Range, "40,110,1")] public float FieldOfView { get; set; } = 70.0f;

    /// <summary>Physics layers the arm collides with. Layer 1 by default, which is
    /// where level geometry conventionally lives; the follow target is always excluded.</summary>
    [Export(PropertyHint.Layers3DPhysics)] public uint CollisionMask { get; set; } = 1;

    /// <summary>Sweeps a sphere instead of a ray. Costs a little more and stops the
    /// camera clipping through corners it would otherwise squeak past.</summary>
    [Export] public bool SphereCast { get; set; } = true;

    // ---- feel ----

    /// <summary>Radians of rotation per pixel of mouse movement.</summary>
    [Export(PropertyHint.Range, "0.0005,0.02,0.0001")]
    public float MouseSensitivity { get; set; } = 0.0025f;

    /// <summary>Look down limit, in degrees. Negative pitch raises the camera and
    /// tilts it downward, which is the resting pose for this genre.</summary>
    [Export(PropertyHint.Range, "-89,0,1")] public float MinPitchDegrees { get; set; } = -78.0f;

    /// <summary>
    /// Look up limit, in degrees. Deliberately tight: the camera orbits at
    /// <see cref="Distance"/>, so every degree of "up" drops it sin(pitch) × distance
    /// below the pivot, and the pivot is only <see cref="HeightOffset"/> off the floor.
    /// At 9.5 m a 22° limit buries the camera two metres underground; 8° keeps it above
    /// the ground at normal zoom and leaves the spring arm to handle the rest.
    /// This genre looks downward anyway — the up limit only needs to reach the horizon.
    /// </summary>
    [Export(PropertyHint.Range, "0,89,1")] public float MaxPitchDegrees { get; set; } = 8.0f;

    [Export] public float StartPitchDegrees { get; set; } = -26.0f;

    /// <summary>Follow responsiveness, as an exponential rate rather than a per-frame
    /// fraction, so the result is identical at 30fps and 240fps. Higher is stiffer;
    /// around 12 tracks tightly without feeling welded to the player.</summary>
    [Export(PropertyHint.Range, "1,40,0.5")] public float FollowSharpness { get; set; } = 12.0f;

    /// <summary>How fast the arm returns to <see cref="Distance"/> after zooming or
    /// after the world stops blocking it.</summary>
    [Export(PropertyHint.Range, "1,40,0.5")] public float ZoomSharpness { get; set; } = 10.0f;

    [Export] public bool CaptureMouseOnStart { get; set; } = true;

    /// <summary>Whether a left click re-captures the mouse after Escape released it.
    /// Suppressed while the tree is paused so it cannot steal clicks from menus.</summary>
    [Export] public bool RecaptureOnClick { get; set; } = true;

    // ---- shake ----

    /// <summary>Oscillations per second of the shake wobble.</summary>
    [Export(PropertyHint.Range, "0.5,10,0.1")] public float ShakeFrequency { get; set; } = 2.4f;

    /// <summary>Radians of camera roll per metre of shake offset. A little roll reads
    /// as impact; a lot reads as a bug.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float ShakeRoll { get; set; } = 0.35f;

    private SpringArm3D _arm = null!;
    private Node3D _armEnd = null!;
    private Camera3D _camera = null!;

    private Node3D? _target;
    private float _retargetCooldown;

    private float _yaw;
    private float _pitch;
    private float _armLength;
    private bool _snapped;

    private float _shakeStrength;
    private float _shakeRemaining;
    private float _shakeDuration;
    private float _shakePhase;
    private bool _shakeDirty;

    // Field of view is the cheapest speed cue there is: widening it while the
    // player is fast makes the same velocity *feel* faster, and narrowing it
    // back when they stop makes the stop read as a stop.
    private float _fovKick;

    /// <summary>Degrees added to <see cref="FieldOfView"/> for sustained speed.
    /// Written every frame by whoever owns movement; the rig only smooths it.</summary>
    public float FovBoost { get; set; }

    /// <summary>Rotation about the world Y axis, in radians, wrapped to ±π.</summary>
    public float Yaw => _yaw;

    /// <summary>Pitch of the arm, in radians. Negative looks down.</summary>
    public float Pitch => _pitch;

    /// <summary>
    /// The camera's facing flattened onto the ground plane and normalised — the
    /// "forwards" that camera-relative movement and aiming should use. Never the
    /// zero vector, even when looking straight down.
    /// </summary>
    public Vector3 FlatForward => new(-Mathf.Sin(_yaw), 0f, -Mathf.Cos(_yaw));

    /// <summary>Ground-plane right, companion to <see cref="FlatForward"/>.</summary>
    public Vector3 FlatRight => new(Mathf.Cos(_yaw), 0f, -Mathf.Sin(_yaw));

    /// <summary>The rig's <see cref="Camera3D"/>, for projecting world points to the
    /// screen (damage popups, off-screen markers).</summary>
    public Camera3D Camera => _camera;

    /// <summary>The point the rig orbits: the target's position plus
    /// <see cref="HeightOffset"/>. Useful as an aim origin.</summary>
    public Vector3 PivotPosition => GlobalPosition;

    public override void _EnterTree()
    {
        // Claimed here rather than in _Ready so nodes that resolve the camera during
        // their own _Ready find it regardless of sibling order.
        Instance = this;
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    public override void _Ready()
    {
        // Immune to a parent's transform, so the rig follows identically whether it
        // is a child of the world or of the player it is chasing.
        TopLevel = true;

        _arm = FindOrCreateChild<SpringArm3D>(this, "SpringArm");
        _armEnd = FindOrCreateChild<Node3D>(_arm, "ArmEnd");

        // A camera authored under the arm belongs one level deeper; move it rather
        // than ending up with two.
        if (FindChild<Camera3D>(_arm) is { } stray) stray.Reparent(_armEnd, false);
        _camera = FindOrCreateChild<Camera3D>(_armEnd, "Camera");

        _armLength = Distance;
        _arm.SpringLength = _armLength;
        _arm.CollisionMask = CollisionMask;
        _arm.Margin = 0.3f;
        if (SphereCast) _arm.Shape = new SphereShape3D { Radius = 0.35f };

        FieldOfView = Meta.GraphicsSettings.Current.Fov;
        _camera.Fov = FieldOfView;
        _camera.Near = 0.05f;
        _camera.Far = 500.0f;
        _camera.Current = true;

        _pitch = Mathf.Clamp(
            Mathf.DegToRad(StartPitchDegrees),
            Mathf.DegToRad(MinPitchDegrees),
            Mathf.DegToRad(MaxPitchDegrees));
        Rotation = new Vector3(0f, _yaw, 0f);
        _arm.Rotation = new Vector3(_pitch, 0f, 0f);

        if (CaptureMouseOnStart) TryCaptureMouse();

        ResolveTarget(0f);
        SnapToTarget();
    }

    /// <summary>Follows <paramref name="target"/> from now on, snapping rather than
    /// sweeping across the level to reach it.</summary>
    public void SetTarget(Node3D? target)
    {
        _target = target;
        _snapped = false;

        // The player's own body would otherwise collapse the arm to nothing.
        _arm.ClearExcludedObjects();
        if (target is CollisionObject3D body) _arm.AddExcludedObject(body.GetRid());
    }

    /// <summary>Drops the rig onto its target immediately, skipping the smoothing.
    /// Use after a teleport so the camera does not fly across the map.</summary>
    public void SnapToTarget()
    {
        if (_target is null) return;
        GlobalPosition = _target.GlobalPosition + Vector3.Up * HeightOffset;
        _snapped = true;
    }

    /// <summary>
    /// Kicks the camera. <paramref name="strength"/> is the peak offset in metres
    /// (0.08 for a light hit, 0.3 for something that hurts, 0.6 for a boss landing);
    /// <paramref name="seconds"/> is how long it takes to settle.
    ///
    /// Overlapping calls take the maximum of both rather than summing, so a burst of
    /// twenty small hits in one frame stays legible instead of throwing the view off
    /// the screen — and a small hit landing during a big shake cannot cut it short.
    /// </summary>
    public void Shake(float strength, float seconds)
    {
        strength *= Meta.GraphicsSettings.ShakeScale;
        if (strength <= 0f || seconds <= 0f) return;

        _shakeStrength = Mathf.Max(_shakeStrength, strength);
        _shakeRemaining = Mathf.Max(_shakeRemaining, seconds);
        _shakeDuration = Mathf.Max(_shakeDuration, _shakeRemaining);
    }

    /// <summary>Cancels any shake in progress and re-centres the camera. For run end
    /// and scene transitions, where a lingering wobble would look like a glitch.</summary>
    public void StopShake()
    {
        _shakeStrength = 0f;
        _shakeRemaining = 0f;
        _shakeDuration = 0f;
        ApplyShakeOffset(Vector3.Zero, 0f);
    }

    /// <summary>Turns the camera to face <paramref name="direction"/> on the ground
    /// plane. Ignores vertical components and zero-length input.</summary>
    public void FaceDirection(Vector3 direction)
    {
        var flat = new Vector2(direction.X, direction.Z);
        if (flat.LengthSquared() < 0.0001f) return;
        // Inverse of FlatForward: forward is (-sin yaw, 0, -cos yaw).
        _yaw = Mathf.Atan2(-flat.X, -flat.Y);
    }

    /// <summary>
    /// Captures the mouse for steering, but only when that is what the person at
    /// the keyboard wants: never in an automated run (tests and captures open
    /// windows while the user is working in something else), and never while the
    /// game window is not the focused one. A captured mouse is clipped and hidden
    /// system-wide on Windows, so capturing from an unfocused or background window
    /// takes the user's pointer away from whatever they were actually doing.
    /// </summary>
    // ------------------------------------------------------------ cursor holds
    //
    // Every panel that needs the cursor (level-up, shrine, merchant/microwave
    // modals, pause menu) holds it here instead of remembering the mouse mode
    // itself. The capture state is recorded when the first hold starts and
    // given back when the last one ends, so overlapping or queued panels can
    // neither grab the mouse while another is still open nor forget that it
    // was captured (the camera used to stay dead after a level-up until the
    // player clicked).
    private static readonly System.Collections.Generic.HashSet<GodotObject> CursorHolds = new();
    private static bool _recaptureAfterHolds;

    /// <summary>Free the cursor for <paramref name="owner"/> until <see cref="ReleaseCursor"/>.</summary>
    public static void HoldCursor(GodotObject owner)
    {
        CursorHolds.RemoveWhere(o => !IsInstanceValid(o));
        if (CursorHolds.Count == 0) _recaptureAfterHolds = Input.MouseMode == Input.MouseModeEnum.Captured;
        CursorHolds.Add(owner);
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    /// <summary>End <paramref name="owner"/>'s hold; the last one out re-captures
    /// the mouse if it was captured when the first came in.</summary>
    public static void ReleaseCursor(GodotObject owner)
    {
        if (!CursorHolds.Remove(owner)) return;
        CursorHolds.RemoveWhere(o => !IsInstanceValid(o));
        if (CursorHolds.Count > 0) return;
        bool recapture = _recaptureAfterHolds;
        _recaptureAfterHolds = false;
        // In a live run the mouse always goes back to the camera when the last
        // panel closes. Deciding from "was it captured when the panel opened"
        // missed too many paths (a focus blip, an earlier Escape, a queued hand)
        // and players were left with a dead camera until they clicked.
        if (InLiveRun()) recapture = true;
        if (!recapture) return;
        TryCaptureMouse();
        // Again next frame: the click that picked a card is still being
        // delivered, and a Control under it may toggle the mode back.
        if (Engine.GetMainLoop() is SceneTree tree)
            tree.CreateTimer(0.05, processAlways: true).Timeout += () =>
            {
                if (!CursorHeld && InLiveRun() && Input.MouseMode != Input.MouseModeEnum.Captured) TryCaptureMouse();
            };
    }

    private static bool InLiveRun()
        => Game.Main.Instance is { } m && GodotObject.IsInstanceValid(m) && !m.Run.Over
           && !(m.Results?.IsShown ?? false);   // not Paused: the closing panel unpauses just after

    /// <summary>The mouse should come back captured when the holds end (the
    /// pause menu opened by a focus loss, which freed it first).</summary>
    public static void RecaptureAfterHolds() { if (CursorHolds.Count > 0) _recaptureAfterHolds = true; }

    /// <summary>True while any panel holds the cursor.</summary>
    public static bool CursorHeld
    {
        get { CursorHolds.RemoveWhere(o => !IsInstanceValid(o)); return CursorHolds.Count > 0; }
    }

    /// <summary>--check-input-guards only: capture even in an unfocused,
    /// automated window, so the check can see a panel hand the mouse back.</summary>
    internal static bool ForceCaptureForTest { get; set; }

    private static ulong _capturedAt;

    public static bool TryCaptureMouse()
    {
        if (Input.MouseMode != Input.MouseModeEnum.Captured) _capturedAt = Time.GetTicksMsec();
        if (ForceCaptureForTest) { Input.MouseMode = Input.MouseModeEnum.Captured; return true; }
        if (Game.Screenshot.Requested || Game.Screenshot.Auto) return false;
        if (!DisplayServer.WindowIsFocused()) return false;
        // Never under the results screen: the cursor must stay for its buttons.
        if (Game.Main.Instance?.Results is { IsShown: true }) return false;
        Input.MouseMode = Input.MouseModeEnum.Captured;
        return true;
    }

    public override void _Notification(int what)
    {
        // Alt-Tab or clicking another monitor does not release a captured mouse
        // on its own, so it stays trapped inside the game window. Free it the
        // moment focus leaves. Deliberately no automatic re-capture on return:
        // grabbing the cursor while someone is still reaching for another window
        // is the same trap in reverse. A click on the game re-captures it
        // (RecaptureOnClick).
        if (what == NotificationApplicationFocusOut || what == NotificationWMWindowFocusOut)
        {
            if (Input.MouseMode is Input.MouseModeEnum.Captured or Input.MouseModeEnum.Confined)
                Input.MouseMode = Input.MouseModeEnum.Visible;
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        bool captured = Input.MouseMode == Input.MouseModeEnum.Captured;

        if (captured && @event is InputEventMouseMotion motion)
        {
            // The first motion after (re)capturing carries everything the free
            // cursor travelled (a pause menu, settings), which flipped the
            // camera to its pitch limit. Drop it, and any absurd jump.
            if (Time.GetTicksMsec() - _capturedAt < 120 || motion.Relative.LengthSquared() > 250f * 250f) return;
            // Right/down on the mouse turns right/looks down, so both axes subtract:
            // Godot's +Y yaw turns left, and negative pitch aims the camera downward.
            // The user's setting scales the camera's own default, read every
            // event so a change in the pause menu takes effect at once.
            float sensitivity = MouseSensitivity * Meta.GameSettings.Sensitivity;
            _yaw = Mathf.Wrap(_yaw - motion.Relative.X * sensitivity, -Mathf.Pi, Mathf.Pi);
            _pitch = Mathf.Clamp(
                _pitch - motion.Relative.Y * sensitivity,
                Mathf.DegToRad(MinPitchDegrees),
                Mathf.DegToRad(MaxPitchDegrees));
            return;
        }

        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        {
            // Only swallow Escape when it actually did something. The first press
            // frees the cursor; a second reaches whatever owns the pause menu.
            if (!captured) return;
            Input.MouseMode = Input.MouseModeEnum.Visible;
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event is not InputEventMouseButton { Pressed: true } button) return;

        if (captured)
        {
            float zoom = button.ButtonIndex switch
            {
                MouseButton.WheelUp => -ZoomStep,
                MouseButton.WheelDown => ZoomStep,
                _ => 0f,
            };
            if (zoom != 0f)
            {
                Distance = Mathf.Clamp(Distance + zoom, MinDistance, MaxDistance);
                GetViewport().SetInputAsHandled();
            }
            return;
        }

        // Re-grab after Escape — but never while paused, where the click belongs to a menu.
        // Nor while the compact level-up hand is up: a click that misses a card
        // must not hide the cursor the player is choosing with.
        if (RecaptureOnClick && button.ButtonIndex == MouseButton.Left && !Net.Coop.IsPaused(GetTree()) && !UI.LevelUpPanel.AnyCompactOpen && !CursorHeld)
        {
            TryCaptureMouse();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        ResolveTarget(dt);

        if (_target is not null)
        {
            Vector3 desired = _target.GlobalPosition + Vector3.Up * HeightOffset;
            if (_snapped)
            {
                // Exponential smoothing: the fraction of the remaining gap closed is
                // derived from dt, so the camera lands in the same place whatever the
                // frame rate, instead of being stiffer on fast machines.
                GlobalPosition = GlobalPosition.Lerp(desired, Damp(FollowSharpness, dt));
            }
            else
            {
                GlobalPosition = desired;
                _snapped = true;
            }
        }

        Rotation = new Vector3(0f, _yaw, 0f);
        _arm.Rotation = new Vector3(_pitch, 0f, 0f);

        _armLength = Mathf.Lerp(_armLength, Distance, Damp(ZoomSharpness, dt));
        _arm.SpringLength = _armLength;

        _fovKick = Mathf.Lerp(_fovKick, 0f, Damp(7f, dt));
        // The speed boost and dash kick ride on top of the player's field of
        // view, scaled by the "speed & dash zoom" setting.
        float fov = FieldOfView + (Mathf.Clamp(FovBoost, 0f, 18f) + _fovKick) * Meta.GraphicsSettings.ScreenFxScale;
        _camera.Fov = Mathf.Lerp(_camera.Fov, fov, Damp(9f, dt));

        UpdateShake(dt);
    }

    /// <summary>
    /// A momentary field-of-view punch in degrees, for a dash or a slam. It
    /// decays on its own; overlapping kicks keep the larger rather than summing.
    /// </summary>
    public void KickFov(float degrees) => _fovKick = Mathf.Max(_fovKick, degrees);

    /// <summary>
    /// Frame-rate independent smoothing factor: the fraction of the distance to the
    /// goal to cover this frame for a decay rate of <paramref name="sharpness"/>.
    /// </summary>
    private static float Damp(float sharpness, float dt) => 1f - Mathf.Exp(-sharpness * dt);

    private void UpdateShake(float dt)
    {
        if (_shakeRemaining <= 0f)
        {
            // One tidy-up pass after the last shaking frame, then nothing.
            if (_shakeDirty) ApplyShakeOffset(Vector3.Zero, 0f);
            return;
        }

        _shakeRemaining -= dt;
        _shakePhase += dt * ShakeFrequency;

        // Quadratic falloff: hits punch immediately and settle without a long tail.
        float remaining = Mathf.Max(_shakeRemaining, 0f) / Mathf.Max(_shakeDuration, 0.0001f);
        float amplitude = _shakeStrength * remaining * remaining;

        var offset = new Vector3(
            Wobble(_shakePhase, 0f) * amplitude,
            Wobble(_shakePhase, 31.4f) * amplitude,
            0f);
        ApplyShakeOffset(offset, Wobble(_shakePhase, 57.1f) * amplitude * ShakeRoll);

        if (_shakeRemaining <= 0f)
        {
            _shakeStrength = 0f;
            _shakeRemaining = 0f;
            _shakeDuration = 0f;
        }
    }

    /// <summary>
    /// Shake lives here and only here: a local offset on the camera, inside the node
    /// the spring arm positions. The arm's own target is untouched, so shaking never
    /// feeds back into the follow smoothing or the occlusion test.
    /// </summary>
    private void ApplyShakeOffset(Vector3 offset, float roll)
    {
        _camera.Position = offset;
        _camera.Rotation = new Vector3(0f, 0f, roll);
        _shakeDirty = offset != Vector3.Zero || roll != 0f;
    }

    /// <summary>
    /// Smooth pseudo-noise in [-1, 1]. Two incommensurate sines never repeat on a
    /// timescale anyone will notice, and unlike per-frame random values this stays
    /// continuous, so the shake reads as a shove rather than as static.
    /// </summary>
    private static float Wobble(float t, float seed)
        => Mathf.Sin(t * 12.9898f + seed) * 0.6f + Mathf.Sin(t * 7.233f + seed * 2.7f) * 0.4f;

    /// <summary>
    /// Keeps <see cref="_target"/> pointing at a live node, re-searching on a timer
    /// when it is missing. Polling matters because the player may be spawned after
    /// the camera, and because a dead player is freed out from under us mid-run.
    /// </summary>
    private void ResolveTarget(float dt)
    {
        if (_target is { } current && IsInstanceValid(current) && current.IsInsideTree()) return;

        _target = null;
        _retargetCooldown -= dt;
        if (_retargetCooldown > 0f) return;
        _retargetCooldown = 0.25f;

        Node3D? found = null;
        if (TargetPath is not null && !TargetPath.IsEmpty) found = GetNodeOrNull<Node3D>(TargetPath);
        found ??= GetTree().GetFirstNodeInGroup(TargetGroup) as Node3D;
        if (found is not null) SetTarget(found);
    }

    private static T? FindChild<T>(Node parent) where T : Node
    {
        int count = parent.GetChildCount();
        for (int i = 0; i < count; i++)
            if (parent.GetChild(i) is T match) return match;
        return null;
    }

    /// <summary>Reuses a child authored in the scene, or builds one. Lets the rig be
    /// laid out in a .tscn for tweaking without the code assuming it was.</summary>
    private static T FindOrCreateChild<T>(Node parent, string name) where T : Node3D, new()
    {
        if (FindChild<T>(parent) is { } existing) return existing;
        var created = new T { Name = name };
        parent.AddChild(created);
        return created;
    }
}
