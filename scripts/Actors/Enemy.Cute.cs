using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Combat;
using Wavebreak.Core;
using Wavebreak.Data;

namespace Wavebreak.Actors;

/// <summary>
/// The cute wave's enemy mechanics (<c>Data/Catalog.Enemies3.cs</c>), kept in
/// their own partial; the older enemy files carry one-line hooks only:
///
///   * four behaviours: <see cref="EnemyBehavior.Mimic"/> (a pot until you come
///     close), <see cref="EnemyBehavior.Bubbler"/> (trapping bubbles),
///     <see cref="EnemyBehavior.Puffer"/> (puff up, then roll a lane) and
///     <see cref="EnemyBehavior.Pincher"/> (sidle in, pinch the space in front);
///   * four traits: <see cref="EnemyDef.TetherLength"/> (a stinging link to an
///     ally), <see cref="EnemyDef.GloomRadius"/> (darkens the view),
///     <see cref="EnemyDef.TrailSlow"/> (slime puddles) and
///     <see cref="EnemyDef.BroodCount"/> (lays more of a smaller enemy).
///
/// Same rule as every other enemy file: a tell before anything lands. The
/// mimic's eyes peek out of the pot and the pot jiggles when you are near; the
/// lunge, the roll and the pinch all paint the floor first; the tether is drawn
/// on the floor the whole time it is live; and the gloom always leaves the
/// lantern-fish's own lure shining through it.
/// </summary>
public sealed partial class Enemy
{
    private static readonly Color TetherColor = new(1.00f, 0.52f, 0.95f, 0.72f);
    private static readonly Color PinchColor = new(1.00f, 0.45f, 0.38f, 0.85f);
    private static readonly Color SurpriseColor = new(1.00f, 0.86f, 0.40f, 0.9f);
    private static readonly Color PuffTextColor = new(1.00f, 0.90f, 0.55f);
    private static readonly Color BroodColor = new(1.00f, 0.62f, 0.70f, 0.8f);

    /// <summary>A mimic left alone this long gives up the act and waddles over.</summary>
    private const float MimicBoredAfter = 10f;

    /// <summary>Fraction of a hit a puffed-up puffer shrugs off.</summary>
    private const float PuffArmor = 0.6f;

    /// <summary>Half-width of a tether's sting zone on the floor, metres.</summary>
    private const float TetherReach = 0.5f;

    private bool _cute;
    private bool _dormant;
    private float _dormantFor;
    private float _peekTimer;
    private bool _rolled;
    private float _trailTimer;
    private Vector3 _lastDrop;
    private float _broodTimer;
    private Enemy? _partner;
    private float _partnerSearch;
    private float _stingTimer;
    private float _puffPopupAt;
    private string _tetherTag = "tether";
    private GroundDecal? _tetherDecal;
    private MeshInstance3D? _beam;
    private Node3D? _disguise;
    private Node3D? _peekEyes;

    private static readonly Dictionary<string, string> TetherTags = new();
    private static CylinderMesh? _beamMesh;
    private static StandardMaterial3D? _beamMaterial;

    /// <summary>A mimic still pretending to be a pot. For tests and UI.</summary>
    public bool IsDormant => _dormant;

    /// <summary>The ally this enemy's tether is linked to, if any.</summary>
    public Enemy? TetherPartner => _partner;

    /// <summary>Puffed up and armoured (wind-up and roll).</summary>
    private bool Puffed => Behavior == EnemyBehavior.Puffer && _act is Act.WindUp or Act.Dash;

    /// <summary>Lay the next brood now. For the audit.</summary>
    internal void ForceBrood() => _broodTimer = 0f;

    // ------------------------------------------------------------ lifecycle

    private void SpawnCute()
    {
        _cute = _def is { HasCuteTraits: true };
        _dormant = false;
        _dormantFor = 0f;
        _partner = null;
        _partnerSearch = 0f;
        _stingTimer = 0.3f;
        _trailTimer = 0f;
        _lastDrop = GlobalPosition;
        _peekTimer = 1.2f;
        _puffPopupAt = 0f;
        if (!_cute || _def is not { } def) return;

        _broodTimer = def.BroodEvery * 0.6f;
        if (def.TetherLength > 0f && !TetherTags.TryGetValue(def.Id, out string? tag))
            TetherTags[def.Id] = tag = def.Id + ":Tether";
        _tetherTag = def.TetherLength > 0f ? TetherTags[def.Id] : "tether";

        if (def.Behavior == EnemyBehavior.Mimic)
        {
            _dormant = true;
            ShowDisguise(true);
        }
    }

    private void EndCute()
    {
        EnemyAttacks.ReleaseDecal(_tetherDecal);
        _tetherDecal = null;
        if (_beam is not null && IsInstanceValid(_beam)) _beam.Visible = false;
        _partner = null;
        if (_dormant || (_disguise is not null && _disguise.Visible)) ShowDisguise(false);
        _dormant = false;
        ResetRoll();
    }

    // ------------------------------------------------------------ per frame

    /// <summary>Hook from <c>StepBehaviour</c>: after the body has moved and
    /// turned, before the ability clock.</summary>
    private void StepCute(float dt, Vector3 playerFeet, IDamageable? player, float distance, Vector3 heading)
    {
        EnemyDef def = _def!;
        if (_dormant)
        {
            StepDormant(dt, def, player, distance, heading);
            return;
        }
        if (_rolled && _act != Act.Dash) ResetRoll();

        if (def.TrailSlow > 0f) StepTrail(dt, def);
        if (def.TetherLength > 0f) StepTether(dt, def, playerFeet, player);
        if (def.GloomRadius > 0f && distance < def.GloomRadius && player is { IsAlive: true })
            CuteHazards.NoteGloom(AimPoint + new Vector3(0f, _halfHeight * 0.6f, 0f), 1f - distance / def.GloomRadius);
        if (def.BroodCount > 0) StepBrood(dt, def, distance);
    }

    /// <summary>Unit-scale steering override. False keeps the default.</summary>
    private bool CuteLocomotion(Vector3 heading, float distance, bool hasTarget, Vector3 separation, out Vector3 desired)
    {
        desired = Vector3.Zero;
        if (_dormant) return true;                       // a pot does not walk
        if (Behavior != EnemyBehavior.Pincher || !hasTarget || distance <= _def!.AbilityRange * 1.25f) return false;

        // Crabs come at you sideways: a zig-zag that swings about 55 degrees
        // either side of the line, so a straight shot keeps missing.
        float swing = MathF.Sin(_aliveSeconds * 2.3f + _hoverPhase * 3f) * 0.95f;
        desired = heading.Rotated(Vector3.Up, swing) + separation * SeparationWeight;
        if (desired.LengthSquared() > 1f) desired = desired.Normalized();
        return true;
    }

    // ----------------------------------------------------------------- mimic

    private void StepDormant(float dt, EnemyDef def, IDamageable? player, float distance, Vector3 heading)
    {
        _dormantFor += dt;
        _attackTimer = MathF.Max(_attackTimer, 0.3f);

        // The tell: eyes peek out every few seconds, and the pot jiggles when
        // someone walks close.
        _peekTimer -= dt;
        if (_peekTimer < -0.4f) _peekTimer = Dice.Range(2.0f, 3.6f);
        if (_peekEyes is not null) _peekEyes.Visible = _peekTimer < 0f || distance < def.AbilityRange * 1.6f;
        if (_disguise is not null)
        {
            float near = Mathf.Clamp(1f - (distance - def.AbilityRange) / (def.AbilityRange * 1.5f), 0f, 1f);
            float shake = MathF.Sin(_aliveSeconds * 30f) * 0.06f * near;
            _disguise.Rotation = new Vector3(0f, 0f, shake);
        }

        bool near2 = player is { IsAlive: true } && distance <= def.AbilityRange;
        if (near2 || _dormantFor >= MimicBoredAfter) Wake(near2, heading, distance);
    }

    private void Wake(bool lunge, Vector3 heading, float distance)
    {
        if (!_dormant) return;
        _dormant = false;
        ShowDisguise(false);
        EnemyAttacks.Flash(GlobalPosition, 1.3f, SurpriseColor);
        EventBus.Publish(new PopupRequested(AimPoint + Vector3.Up * (_halfHeight + 0.5f), "SURPRISE!", SurpriseColor, 1.1f));
        _rig?.PlayHit(1.5f);
        ContentAudit.NoteAbility(_def, "Wake");
        if (lunge && _act == Act.None && distance >= 1.2f) StartLunge(heading, distance);
        else _abilityTimer = MathF.Min(_abilityTimer, 0.8f);
    }

    private void StartLunge(Vector3 heading, float distance)
    {
        EnemyDef def = _def!;
        BeginWindUp(heading, def.WindUp);
        _actLaneLength = Mathf.Clamp(distance + 3f, 5f, def.AbilityRange + 3f);
        _dashSpeed = MathF.Max(def.AbilitySpeed, _speed * 2f);
        _tele = EnemyAttacks.AcquireDecal();
    }

    private void ShowDisguise(bool on)
    {
        if (on)
        {
            if (_disguise is null || !IsInstanceValid(_disguise))
            {
                _disguise = Visuals.CuteModels.MimicDisguise(_def?.Height ?? 0.95f, out Node3D eyes);
                _peekEyes = eyes;
                AddChild(_disguise);
            }
            _disguise.Visible = true;
            if (_peekEyes is not null) _peekEyes.Visible = false;
            if (_rig is not null && IsInstanceValid(_rig)) _rig.Visible = false;
            if (_bodyMesh is not null) _bodyMesh.Visible = false;
            return;
        }
        if (_disguise is not null && IsInstanceValid(_disguise)) _disguise.Visible = false;
        if (_rig is not null && IsInstanceValid(_rig)) _rig.Visible = true;
        if (_bodyMesh is not null) _bodyMesh.Visible = _rig is null;
    }

    // ------------------------------------------------------------ behaviours

    /// <summary>Hook from <c>BeginExpansion</c>: the ability is ready.</summary>
    private void BeginCute(EnemyDef def, float distance, Vector3 heading)
    {
        switch (def.Behavior)
        {
            case EnemyBehavior.Mimic:
                if (!_dormant && distance >= 2f && distance <= def.AbilityRange) StartLunge(heading, distance);
                break;

            case EnemyBehavior.Bubbler:
                if (distance <= MathF.Max(3f, def.AbilityRange) * 1.45f) BeginWindUp(heading, def.WindUp);
                break;

            case EnemyBehavior.Puffer:
                if (distance >= 2.5f && distance <= def.AbilityRange)
                {
                    BeginWindUp(heading, def.WindUp);
                    _actLaneLength = Mathf.Clamp(distance + 4f, 6f, def.AbilityRange + 4f);
                    _dashSpeed = MathF.Max(def.AbilitySpeed, _speed * 2f);
                    _tele = EnemyAttacks.AcquireDecal();
                }
                break;

            case EnemyBehavior.Pincher:
                if (distance <= def.AbilityRange)
                {
                    BeginWindUp(heading, def.WindUp);
                    // Locked now: the claw closes on this spot, not on you.
                    _actTo = GlobalPosition + heading * (_bodyRadius + def.AbilityRadius * 0.6f);
                    _tele = EnemyAttacks.AcquireDecal();
                }
                break;
        }
    }

    /// <summary>Hook from <c>WindUpExpansion</c>.</summary>
    private void WindUpCute(float t, Vector3 heading)
    {
        EnemyDef def = _def!;
        switch (def.Behavior)
        {
            case EnemyBehavior.Mimic:
            case EnemyBehavior.Puffer:
                if (t < 0.55f) _actDir = heading;           // then the lane is locked
                _actFrom = GlobalPosition;
                _tele?.SetLane(_actFrom, _actDir, _actLaneLength, LaneHalfWidth, t, LaneColor);
                break;
            case EnemyBehavior.Bubbler:
                _actDir = heading;                           // aims until it blows; the tell is the puff
                break;
            case EnemyBehavior.Pincher:
                _tele?.SetDisc(_actTo, def.AbilityRadius, t, PinchColor);
                break;
        }
    }

    /// <summary>Hook from <c>ReleaseExpansion</c>. False for a behaviour this
    /// file does not own.</summary>
    private bool ReleaseCute(Vector3 playerFeet)
    {
        EnemyDef def = _def!;
        float cooldown = def.AbilityCooldown * Dice.Range(0.85f, 1.2f);
        switch (def.Behavior)
        {
            case EnemyBehavior.Mimic:
            case EnemyBehavior.Puffer:
                StartDash(_actLaneLength, _dashSpeed);
                _abilityTimer = cooldown;
                ContentAudit.NoteAbility(def, def.Behavior == EnemyBehavior.Mimic ? "Lunge" : "Roll");
                return true;

            case EnemyBehavior.Bubbler:
            {
                Vector3 aim = FlatDir(playerFeet - GlobalPosition, _actDir);
                int count = Math.Max(1, def.AbilityCount);
                float spread = Mathf.DegToRad(def.AbilitySpread);
                Vector3 muzzle = GlobalPosition + new Vector3(0f, ShotHeight - def.Hover, 0f) + aim * (_builtRadius + 0.3f);
                for (int i = 0; i < count; i++)
                {
                    float a = count == 1 ? 0f : -spread * 0.5f + spread * i / (count - 1);
                    CuteHazards.Blow(muzzle, aim.Rotated(Vector3.Up, a), def.AbilitySpeed, _abilityDamage, 0.5f, 14f,
                                     def.HoldSeconds, SourceTag);
                }
                ContentAudit.NoteAbility(def, "Bubbles");
                _abilityTimer = cooldown;
                if (Dice.Chance(0.4f)) _strafeSign = -_strafeSign;
                Recover(0.4f);
                return true;
            }

            case EnemyBehavior.Pincher:
            {
                EnemyAttacks.Blast(_actTo, def.AbilityRadius, 0f, _abilityDamage, PinchColor, SourceTag);
                EnemyAttacks.Flash(_actTo, def.AbilityRadius, PinchColor);
                // The slow goes on the real player only, and only if they are
                // standing where the audit's dummy would be hit.
                if (PlayerRef.TryGet(out Node3D pn, out IDamageable? who)
                    && pn.GlobalPosition.DistanceSquaredTo(playerFeet) < 0.01f
                    && Flat(playerFeet - _actTo).Length() <= def.AbilityRadius + 0.4f)
                    CuteHazards.Pinch(who, def.HoldSeconds, 0.45f);
                AddKnockback(_actDir, 0.5f);                 // a little forward hop with the clack
                ContentAudit.NoteAbility(def, "Pinch");
                _abilityTimer = cooldown;
                Recover(RecoverTime);
                return true;
            }
        }
        return false;
    }

    /// <summary>Hook from <c>InterceptHit</c>: a hit wakes a mimic; a puffed
    /// puffer shrugs most of it off.</summary>
    private void CuteIntercept(DamageInfo info)
    {
        if (_dormant)
        {
            Vector3 heading = Vector3.Forward;
            float distance = 99f;
            if (PlayerRef.Node is { } p)
            {
                Vector3 d = Flat(p.GlobalPosition - GlobalPosition);
                distance = d.Length();
                if (distance > 1e-3f) heading = d / distance;
            }
            Wake(distance <= _def!.AbilityRange * 1.6f, heading, distance);
        }
        if (Puffed)
        {
            info.Amount *= 1f - PuffArmor;
            info.Knockback *= 0.2f;
            if (_aliveSeconds >= _puffPopupAt)
            {
                _puffPopupAt = _aliveSeconds + 0.8f;
                EventBus.Publish(new PopupRequested(AimPoint + Vector3.Up * (_halfHeight + 0.4f), "PUFF", PuffTextColor, 0.8f));
            }
        }
    }

    // ----------------------------------------------------------------- poses

    /// <summary>Hook from <c>Pose</c>, before the pose is written: the puffer
    /// swells and rolls, the bubbler puffs its cheeks, crabs crouch to clack.</summary>
    private void CutePose(ref Vector3 pose, ref float glow)
    {
        float t = Mathf.Clamp(_actTime / MathF.Max(0.01f, _actLength), 0f, 1f);
        switch (Behavior)
        {
            case EnemyBehavior.Puffer:
                if (_act == Act.WindUp)
                {
                    float e = 1f - (1f - t) * (1f - t);
                    pose = Vector3.One * (1f + 0.55f * e) + new Vector3(0.04f, -0.04f, 0.04f) * MathF.Sin(_actTime * 30f);
                }
                else if (_act == Act.Dash)
                {
                    pose = Vector3.One * 1.55f;
                    Roll(_actTime * _dashSpeed / MathF.Max(0.2f, (_def?.Height ?? 1f) * 0.5f * 1.55f), pose.Y);
                }
                else if (_act == Act.Recover)
                {
                    // Deflating: a floppy wobble back to size.
                    float w = MathF.Sin(t * 14f) * (1f - t) * 0.18f;
                    pose = new Vector3(1f + w, 1f - w, 1f + w);
                    glow = 0f;
                }
                break;
            case EnemyBehavior.Bubbler:
                if (_act == Act.WindUp) pose = new Vector3(1f + 0.22f * t, 1f + 0.08f * t, 1f + 0.22f * t);
                break;
            case EnemyBehavior.Pincher:
                if (_act == Act.WindUp) pose = new Vector3(1f + 0.12f * t, 1f - 0.12f * t, 1f + 0.12f * t);
                break;
            case EnemyBehavior.Mimic:
                if (_act == Act.WindUp) pose = new Vector3(1f + 0.18f * t, 1f - 0.22f * t, 1f + 0.18f * t);
                break;
        }
    }

    /// <summary>Spin the rig about its own centre, so the ball rolls instead
    /// of cartwheeling round its feet.</summary>
    private void Roll(float angle, float scale)
    {
        if (_rig is null || !IsInstanceValid(_rig) || _def is null) return;
        float hc = _def.Height * 0.5f * scale;
        var euler = new Vector3(-angle, MathF.PI, 0f);
        Basis rot = Basis.FromEuler(euler);
        var c = new Vector3(0f, hc, 0f);
        _rig.Rotation = euler;
        _rig.Position = c - rot * c;
        _rolled = true;
    }

    private void ResetRoll()
    {
        if (!_rolled) return;
        _rolled = false;
        if (_rig is null || !IsInstanceValid(_rig)) return;
        _rig.Rotation = new Vector3(0f, MathF.PI, 0f);
        _rig.Position = Vector3.Zero;
    }

    // ---------------------------------------------------------------- traits

    private void StepTrail(float dt, EnemyDef def)
    {
        _trailTimer -= dt;
        if (_trailTimer > 0f) return;
        Vector3 at = GlobalPosition;
        if (def.Hover > 0f) at.Y -= def.Hover;
        if (Flat(at - _lastDrop).LengthSquared() < 0.36f) return;   // only a moving snail leaves slime
        if (CuteHazards.DropPuddle(at, 0.75f + _bodyRadius * 0.5f, 5f, def.TrailSlow))
        {
            _lastDrop = at;
            _trailTimer = 0.4f;
        }
        else _trailTimer = 0.25f;
    }

    private void StepBrood(float dt, EnemyDef def, float distance)
    {
        _broodTimer -= dt;
        if (_broodTimer > 0f) return;
        _broodTimer = def.BroodEvery * Dice.Range(0.9f, 1.1f);
        if (distance > 26f || EnemyManager.Instance is not { } em || em.AliveCount >= em.MaxAlive - 10) return;
        if (Catalog.EnemyById(def.BroodId) is not { } child) return;

        // Laid behind her, fanned out, so the brood follows rather than leads.
        Vector3 at = GlobalPosition;
        for (int i = 0; i < def.BroodCount; i++)
        {
            float a = Rotation.Y + MathF.PI + (i - (def.BroodCount - 1) * 0.5f) * 0.9f;
            em.SpawnAt(child, at + new Vector3(MathF.Sin(a), 0f, MathF.Cos(a)) * (_bodyRadius + 1.1f));
        }
        EnemyAttacks.Flash(at, _bodyRadius + 1.4f, BroodColor);
        ContentAudit.NoteAbility(def, "Brood");
        _rig?.PlayHit(0.6f);
    }

    private void StepTether(float dt, EnemyDef def, Vector3 playerFeet, IDamageable? player)
    {
        _partnerSearch -= dt;
        if (_partner is not null
            && (!IsInstanceValid(_partner) || !_partner.IsAlive || _partner._dormant
                || Flat(_partner.GlobalPosition - GlobalPosition).Length() > def.TetherLength * 1.2f))
            _partner = null;
        if (_partner is null && _partnerSearch <= 0f)
        {
            _partnerSearch = 0.5f;
            _partner = FindPartner(def);
        }
        if (_partner is null)
        {
            EnemyAttacks.ReleaseDecal(_tetherDecal);
            _tetherDecal = null;
            if (_beam is not null && IsInstanceValid(_beam)) _beam.Visible = false;
            return;
        }

        Vector3 a = GlobalPosition with { Y = GlobalPosition.Y - def.Hover };
        Vector3 b = _partner.GlobalPosition with { Y = _partner.GlobalPosition.Y - (_partner._def?.Hover ?? 0f) };
        Vector3 ab = Flat(b - a);
        float len = ab.Length();
        Vector3 dir = len > 1e-3f ? ab / len : Vector3.Forward;

        // The floor shows exactly where it stings; it pulses so it reads as live.
        float pulse = 0.75f + 0.25f * MathF.Sin(_aliveSeconds * 9f);
        _tetherDecal ??= EnemyAttacks.AcquireDecal();
        _tetherDecal?.SetLane(a, dir, MathF.Max(0.2f, len), TetherReach * 0.7f, 1f, TetherColor with { A = TetherColor.A * pulse });
        DrawBeam(AimPoint, _partner.AimPoint, pulse);

        _stingTimer -= dt;
        if (_stingTimer > 0f || player is not { IsAlive: true }) return;
        _stingTimer = 0.05f;
        // Closest point on the floor segment; feet above the knees clear it.
        float along = Mathf.Clamp((playerFeet - a).Dot(dir), 0f, len);
        Vector3 nearest = a + dir * along;
        Vector3 off = Flat(playerFeet - nearest);
        if (off.Length() > TetherReach + 0.3f || playerFeet.Y > a.Y + 1.0f) return;
        _stingTimer = 0.6f;
        Vector3 push = off.LengthSquared() > 1e-4f ? off.Normalized() : new Vector3(-dir.Z, 0f, dir.X);
        EnemyAttacks.Hit(player, _contactDamage * def.TetherDamage, push, 0.6f, _tetherTag);
        EnemyAttacks.Flash(nearest, 0.7f, TetherColor);
        ContentAudit.NoteAbility(def, "Tether");
    }

    private Enemy? FindPartner(EnemyDef def)
    {
        if (EnemyManager.Instance is not { } em) return null;
        Enemy? best = null;
        float bestScore = float.MaxValue;
        var alive = em.AliveList;
        for (int i = 0; i < alive.Count; i++)
        {
            Enemy e = alive[i];
            // One link per pair: never pick someone already linked to us.
            if (e == this || !e.IsAlive || e.IsBoss || e._partner == this || e._dormant || e._def is null) continue;
            float d = Flat(e.GlobalPosition - GlobalPosition).Length();
            bool kin = e._def.Id == def.Id;
            if (d < 1.2f || d > (kin ? def.TetherLength : def.TetherLength * 0.6f)) continue;
            float score = d + (kin ? 0f : 3f);
            if (score < bestScore) { bestScore = score; best = e; }
        }
        return best;
    }

    private void DrawBeam(Vector3 from, Vector3 to, float pulse)
    {
        if (_beam is null || !IsInstanceValid(_beam))
        {
            _beamMesh ??= new CylinderMesh { TopRadius = 1f, BottomRadius = 1f, Height = 1f, RadialSegments = 6, Rings = 1 };
            _beamMaterial ??= new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                AlbedoColor = new Color(1.00f, 0.70f, 1.00f, 0.75f),
            };
            _beam = new MeshInstance3D
            {
                Name = "Tether",
                Mesh = _beamMesh,
                MaterialOverride = _beamMaterial,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                TopLevel = true,
            };
            AddChild(_beam);
        }
        Vector3 d = to - from;
        float len = d.Length();
        if (len < 0.05f) { _beam.Visible = false; return; }
        Basis along = Visuals.MeshKit.BasisAlong(d / len);
        float r = 0.035f + 0.02f * pulse;
        _beam.GlobalTransform = new Transform3D(new Basis(along.X * r, along.Y * len, along.Z * r), from + d * 0.5f);
        _beam.Visible = true;
    }
}
