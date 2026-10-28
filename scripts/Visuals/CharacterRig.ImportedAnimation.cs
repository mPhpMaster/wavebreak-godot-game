using System;
using Godot;

namespace Wavebreak.Visuals;

public sealed partial class CharacterRig
{
    // ------------------------------------------------------------- imported

    private void AnimateImported(float dt)
    {
        if (_player is null) return;

        ApplyOverlay(_flash > 0.05f ? ToonMaterials.FlashOverlay : _rimMaterial);

        AnimSlot slot = _dying ? AnimSlot.Death
                      : _strike > 0.45f ? AnimSlot.Attack
                      : _windupDur > 0f ? AnimSlot.Windup
                      : _hit > 0.55f && Spec.Clips.ContainsKey(AnimSlot.Hit) ? AnimSlot.Hit
                      : Pose == RigPose.Dash ? AnimSlot.Dash
                      : Pose == RigPose.Slide ? AnimSlot.Slide
                      : Pose == RigPose.Slam ? AnimSlot.Slam
                      : !Grounded ? (Velocity.Y > 0f ? AnimSlot.Jump : AnimSlot.Fall)
                      : _speed > Spec.RunThreshold ? AnimSlot.Run
                      : _speed > 0.35f ? AnimSlot.Walk
                      : AnimSlot.Idle;
        PlayClip(slot, 0.12f);
        float rate = slot switch
        {
            AnimSlot.Walk => Mathf.Clamp(_speed / 2.2f, 0.6f, 2.2f),
            AnimSlot.Run or AnimSlot.Dash => Mathf.Clamp(_speed / 6f, 0.8f, 1.8f),
            _ => 1f,
        };

        // Animation LOD: near models animate every frame, mid-range every other,
        // far every fourth (with the skipped time folded in, so they keep pace).
        if (Spec.LodDistance > 0f && --_lodTick <= 0)
        {
            _lodTick = 6 + (int)(GetInstanceId() % 4);
            float d2 = CameraDistanceSq();
            float lod = Spec.LodDistance;
            _animStep = d2 > lod * lod ? 4 : d2 > lod * lod * 0.4f ? 2 : 1;
        }
        _animAccum += dt * rate;
        if (++_animCounter >= _animStep || _dying)
        {
            _animCounter = 0;
            _player.Advance(_animAccum);
            _animAccum = 0f;
        }

        // Procedural layer on top of the clip: hit squash, windup lean, strike
        // lunge, the flip, and the death sink.
        Common(dt, out float pitch, out float lift, out float squash, out float lunge, out _);
        float flip = 0f;
        if (_flip >= 0f)
        {
            float t = _flip / FlipDuration;
            flip = -Mathf.Tau * (1f - (1f - t) * (1f - t));
        }
        float y = 0f, roll = 0f;
        if (_dying)
        {
            // The clip (if any) plays the fall; afterwards the body sinks away.
            float sink = Mathf.Clamp((_death - 0.55f) / 0.45f, 0f, 1f);
            y = -sink * _model.Height * 0.9f;
            squash = 1f;
            pitch = 0f;
            lunge = 0f;
            lift = 0f;
            if (!Spec.Clips.ContainsKey(AnimSlot.Death)) roll = Ease(Mathf.Min(1f, _death * 2.5f)) * 1.5f * _deathDir;
        }
        if (Pose == RigPose.Slam) pitch -= 0.5f;
        float mid = _model.Height * 0.45f;
        Vector3 flipOffset = flip != 0f ? new Vector3(0f, mid - mid * Mathf.Cos(flip), mid * Mathf.Sin(flip)) : Vector3.Zero;
        SetHips(0f, y + lift + flipOffset.Y, lunge + flipOffset.Z, pitch * 0.6f, roll, 0f, squash, flip);
    }


    private void PlayClip(AnimSlot slot, float blend)
    {
        if (_player is null) return;
        string? name = ResolveClip(slot);
        if (name is null || name == _clip) return;
        _clip = name;
        _player.Play(name, blend);
    }

    private string? ResolveClip(AnimSlot slot)
    {
        AnimSlot s = slot;
        for (int guard = 0; guard < 4; guard++)
        {
            if (Spec.Clips.TryGetValue(s, out string? n) && _player!.HasAnimation(n)) return n;
            s = s switch
            {
                AnimSlot.Run or AnimSlot.Dash or AnimSlot.Slide => AnimSlot.Walk,
                AnimSlot.Windup or AnimSlot.Slam => AnimSlot.Attack,
                AnimSlot.Fall => AnimSlot.Jump,
                AnimSlot.Walk or AnimSlot.Jump or AnimSlot.Hit or AnimSlot.Attack => AnimSlot.Idle,
                _ => AnimSlot.Idle,
            };
        }
        return null;
    }
}
