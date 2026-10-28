using System.Linq;
using Godot;
using Wavebreak.Combat;
using Wavebreak.Core;
using Wavebreak.Stats;

namespace Wavebreak.Actors;

public sealed partial class Player
{
    // ------------------------------------------------------------------ pose

    private void UpdateFacing(Vector3 wish, float dt)
    {
        Vector3 heading = _shotgunFacingFor > 0 ? _shotgunFacing : _dashTime > 0f ? _dashDirection
                        : _moveVelocity.LengthSquared() > 0.25f ? _moveVelocity
                        : wish.LengthSquared() > 0.01f ? wish
                        : Vector3.Zero;
        _shotgunFacingFor = Mathf.Max(0, _shotgunFacingFor - dt);

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
            // Only a co-op revive ever brings a fallen player back up; in
            // single-player this is already 0 and stays 0.
            _deathTilt = Mathf.MoveToward(_deathTilt, 0f, dt * 3f);
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

}
