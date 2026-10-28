using System;
using Godot;

namespace Wavebreak.Visuals;

public sealed partial class CharacterRig
{
    // ---------------------------------------------------------------- hooks

    /// <summary>Begin the anticipation pose for an attack landing in
    /// <paramref name="seconds"/>. Releases on its own at the end if nobody
    /// calls <see cref="PlayAttack"/>.</summary>
    public void PlayAttackWindup(float seconds)
    {
        if (_dying) return;
        _windupDur = Mathf.Max(0.05f, seconds);
        _windup = 0f;
    }

    /// <summary>The strike itself: snap from the windup into the lunge.</summary>
    public void PlayAttack()
    {
        if (_dying) return;
        Release();
        _sinceStrike = 0f;
    }

    private void Release()
    {
        _windupDur = 0f;
        _strike = 1f;
    }

    /// <summary>Flash white and recoil.</summary>
    public void PlayHit(float strength = 1f)
    {
        if (_dying) return;
        _hit = Mathf.Max(_hit, Mathf.Clamp(strength, 0.2f, 1f));
        _flash = Mathf.Max(_flash, Mathf.Clamp(strength, 0.3f, 1f));
    }

    /// <summary>Crumple and dissolve, then hide and raise <see cref="DeathFinished"/>.</summary>
    public void PlayDeath()
    {
        if (_dying) return;
        _dying = true;
        _death = 0f;
        _flash = 1f;
        _deathDir = (GetInstanceId() & 1) == 0 ? 1f : -1f;
        if (_player is not null) PlayClip(AnimSlot.Death, 0.1f);
    }

    /// <summary>Double-jump flip: one forward somersault.</summary>
    public void PlayFlip() => _flip = 0f;

    /// <summary>Tint emission (dash streak, slam heat). Alpha is strength.</summary>
    public void SetGlow(Color color, float amount) => _glow = new Color(color.R, color.G, color.B, amount);

    /// <summary>Directly set the white flash (0..1); the player drives its own.</summary>
    public void SetFlash(float amount) => _flash = Mathf.Max(_flash, amount);

    /// <summary>Back to a fresh, living pose. Pools call this on reuse.</summary>
    public void ResetState()
    {
        _dying = false;
        _death = 0f;
        _hit = _flash = _strike = 0f;
        _windupDur = 0f;
        _sinceStrike = -1f;
        _flip = -1f;
        _havePos = false;
        _speed = 0f;
        _glow = new Color(0, 0, 0, 0);
        Visible = true;
        PushShader(force: true);
        if (_player is not null)
        {
            _clip = "";
            PlayClip(AnimSlot.Idle, 0f);
            _hips.Transform = Transform3D.Identity;
        }
    }

    // ---------------------------------------------------------------- frame

    public override void _Process(double delta)
    {
        float dt = Mathf.Min((float)delta, 0.1f);
        _time += dt;
        TrackMotion(dt);

        _hit = Mathf.Max(0f, _hit - dt * 5f);
        _flash = Mathf.Max(0f, _flash - dt * 7f);
        _strike = Mathf.Max(0f, _strike - dt * 3.2f);
        if (_windupDur > 0f)
        {
            _windup += dt;
            if (_windup >= _windupDur) Release();
        }
        else if (_sinceStrike >= 0f && AttackCooldown > 0f)
        {
            _sinceStrike += dt;
            float lead = Mathf.Min(0.45f, AttackCooldown * 0.5f);
            if (_sinceStrike >= AttackCooldown - lead)
            {
                _sinceStrike = -1f; // one anticipation per real strike
                PlayAttackWindup(lead);
            }
        }
        if (_flip >= 0f)
        {
            _flip += dt;
            if (_flip > FlipDuration) _flip = -1f;
        }

        if (_dying)
        {
            _death += dt / (_player is not null ? ImportedDeathDuration : DeathDuration);
            if (_death >= 1f)
            {
                _death = 1f;
                Visible = false;
                PushShader();
                DeathFinished?.Invoke(this);
                return;
            }
        }

        PushShader();

        if (Spec.Id.StartsWith("reef:", StringComparison.Ordinal) && !_dying)
        { PoseMarine(dt); return; }
        if (Swimming && !_dying && !Spec.NativeSwimmingClip)
        {
            PoseSwimming(dt);
            return;
        }
        if (_swimWasActive) EndSwimmingAnimation();

        if (_player is not null)
        {
            AnimateImported(dt);
            return;
        }

        // LOD: every few frames, not every frame — the far mesh is swapped in by
        // the engine anyway, this only decides whether posing is worth doing.
        if (Spec.LodDistance > 0f && --_lodTick <= 0)
        {
            _lodTick = 6 + (int)(GetInstanceId() % 4);
            _far = CameraDistanceSq() > Spec.LodDistance * Spec.LodDistance;
        }
        if (_far && !_dying) return;

        switch (Spec.Body)
        {
            case BodyType.Quadruped: PoseQuadruped(dt); break;
            case BodyType.Blob: PoseBlob(dt); break;
            case BodyType.Flyer: PoseFlyer(dt); break;
            case BodyType.Serpent: PoseSerpent(dt); break;
            default: PoseBiped(dt); break;
        }
    }

    private const float DeathDuration = 0.7f;
    private const float ImportedDeathDuration = 1.15f;
    private const float FlipDuration = 0.42f;

    private void TrackMotion(float dt)
    {
        Vector3 v;
        if (ManualMotion) v = Velocity;
        else
        {
            Vector3 p = GlobalPosition;
            v = _havePos && dt > 0f ? (p - _lastPos) / dt : Vector3.Zero;
            _lastPos = p;
            _havePos = true;
            if (v.LengthSquared() > 900f) v = Vector3.Zero; // teleports and pool reuse
        }
        float planar = new Vector2(v.X, v.Z).Length();
        if (_dying) planar = 0f;
        _speed = Mathf.Lerp(_speed, planar, 1f - Mathf.Exp(-10f * dt));
    }

    private float CameraDistanceSq()
    {
        ulong frame = Engine.GetProcessFrames();
        if (frame != _camFrame)
        {
            _camFrame = frame;
            Camera3D? cam = GetViewport()?.GetCamera3D();
            _haveCam = cam is not null;
            if (cam is not null) _camPos = cam.GlobalPosition;
        }
        return _haveCam ? GlobalPosition.DistanceSquaredTo(_camPos) : 0f;
    }

    private void PushShader(bool force = false)
    {
        if (_geoms.Length == 0 || _player is not null) return;
        float flash = _flash * _flash;
        float dissolve = _dying ? Mathf.SmoothStep(0.15f, 1f, _death) : 0f;
        if (force || Mathf.Abs(flash - _sentFlash) > 0.02f || (flash == 0f && _sentFlash != 0f))
        {
            _sentFlash = flash;
            foreach (GeometryInstance3D g in _geoms) g.SetInstanceShaderParameter(PFlash, flash);
        }
        if (force || Mathf.Abs(dissolve - _sentDissolve) > 0.01f || (dissolve == 0f && _sentDissolve != 0f))
        {
            _sentDissolve = dissolve;
            foreach (GeometryInstance3D g in _geoms) g.SetInstanceShaderParameter(PDissolve, dissolve);
        }
        if (force || !_glow.IsEqualApprox(_sentGlow))
        {
            _sentGlow = _glow;
            foreach (GeometryInstance3D g in _geoms) g.SetInstanceShaderParameter(PGlow, _glow);
        }
    }

    private static readonly StringName PFlash = "flash", PDissolve = "dissolve", PGlow = "glow";

}

