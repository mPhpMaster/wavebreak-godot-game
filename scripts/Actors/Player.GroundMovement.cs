using System.Linq;
using Godot;
using Wavebreak.Combat;
using Wavebreak.Core;
using Wavebreak.Stats;

namespace Wavebreak.Actors;

public sealed partial class Player
{
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

        if (TryCoreStrike())
        {
            _dashCooldown = Mathf.Max(0.65f, Stats.Get(Stat.DashCooldown));
            Dashes++;
            EventBus.Publish(new PlayerMoved(MoveVerb.Dash, GlobalPosition));
            return;
        }
        if (Game.Arena.IsCity) ReleaseHook();

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
                       || (!Net.Coop.LocalHold && _inputAvailable && _hasJump && Input.IsActionJustPressed("jump"));
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
                 && !Input.IsActionPressed("jump") && MoveOverride is null
                 && !(Game.Arena.IsCity && _cityBallistic))
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

}
