using System;
using Godot;

namespace Wavebreak.Visuals;

public sealed partial class CharacterRig
{
    // ---------------------------------------------------------------- poses

    /// <summary>Shared whole-body terms: the hit recoil, the windup lean, the
    /// strike lunge and the death fall. Every body type adds its own on top.</summary>
    private void Common(float dt, out float pitch, out float lift, out float squash, out float lunge,
                        out float windup01)
    {
        windup01 = _windupDur > 0f ? Mathf.Clamp(_windup / _windupDur, 0f, 1f) : 0f;
        float w = Ease(windup01);
        float s = _strike * _strike;
        pitch = 0.28f * w - 0.45f * s + 0.3f * _hit;
        squash = 1f + 0.07f * w - 0.2f * _hit * (1f - _hit) * 4f * 0.5f;
        lunge = -0.22f * s * _model.Height;
        lift = 0f;
        if (_dying)
        {
            float d = Ease(_death);
            pitch = Mathf.Lerp(pitch, 1.35f * _deathDir, d);
            squash = Mathf.Lerp(squash, 0.55f, d);
            lift = -_model.HipsOffset.Y * 0.55f * d;
        }
    }

    private static float Ease(float t) => t * t * (3f - 2f * t);

    private static float Approach(float current, float target, float rate, float dt)
        => Mathf.Lerp(current, target, 1f - Mathf.Exp(-rate * dt));

    private void SetHips(float x, float y, float z, float pitch, float roll, float yaw, float squash, float extraPitch = 0f)
    {
        float widen = 1f / Mathf.Sqrt(Mathf.Max(0.3f, squash));
        Basis b = Basis.FromEuler(new Vector3(pitch + extraPitch, yaw, roll)).Scaled(new Vector3(widen, squash, widen));
        _hips.Transform = new Transform3D(b, _model.HipsOffset + new Vector3(x, y, z));
    }

    private static void SetJoint(MeshInstance3D node, Vector3 pivot, float x, float z, float y = 0f)
        => node.Transform = new Transform3D(Basis.FromEuler(new Vector3(x, y, z)), pivot);

    private void PoseBiped(float dt)
    {
        bool brute = Spec.Body == BodyType.Brute;
        float H = _model.Height;
        float stride = Mathf.Max(0.2f, _model.StrideLength);

        // Weights for the hero's verbs ease in and out so poses blend.
        _wAir = Approach(_wAir, Pose == RigPose.Air || (!Grounded && Pose == RigPose.Locomotion) ? 1f : 0f, 12f, dt);
        _wDash = Approach(_wDash, Pose == RigPose.Dash ? 1f : 0f, 22f, dt);
        _wSlide = Approach(_wSlide, Pose == RigPose.Slide ? 1f : 0f, 16f, dt);
        _wSlam = Approach(_wSlam, Pose == RigPose.Slam ? 1f : 0f, 18f, dt);

        Common(dt, out float pitch, out float lift, out float squash, out float lunge, out float wind);

        float run = Mathf.Clamp(_speed / 4.5f, 0f, 1.25f) * (1f - _wAir) * (1f - _wSlide);
        _phase += dt * _speed / stride * Mathf.Tau * (1f - _wSlide);
        float sn = Mathf.Sin(_phase), cs = Mathf.Cos(_phase);
        float swing = _model.LimbSwing * run;

        float legL = sn * swing, legR = -sn * swing;
        float kneeL = Mathf.Max(0f, cs) * 1.1f * run + 0.05f, kneeR = Mathf.Max(0f, -cs) * 1.1f * run + 0.05f;
        float armL = -legL * (brute ? 0.6f : 0.85f), armR = -legR * (brute ? 0.6f : 0.85f);
        float elbowL = 0.25f + 0.55f * run, elbowR = elbowL;
        float splay = (brute ? 0.22f : 0.1f) + 0.04f * Mathf.Sin(_time * 1.7f + _seed);
        float splayL = -splay, splayR = splay;
        float breathe = 1f + 0.022f * Mathf.Sin(_time * 2.3f + _seed) * (1f - run);
        float bob = Mathf.Abs(cs) * 0.035f * H * run;
        float lean = -0.1f * run - (brute ? 0.12f : 0f);
        float roll = sn * 0.05f * run;

        // Windup: arms rise overhead (both for brutes, the weapon arm otherwise);
        // the strike swings them through.
        float w = Ease(wind), s = _strike;
        armR = Mathf.Lerp(armR, 2.5f, w); elbowR = Mathf.Lerp(elbowR, 0.6f, w);
        if (brute) { armL = Mathf.Lerp(armL, 2.5f, w); elbowL = Mathf.Lerp(elbowL, 0.6f, w); }
        armR = Mathf.Lerp(armR, 0.55f, s); elbowR = Mathf.Lerp(elbowR, 0.1f, s);
        if (brute) armL = Mathf.Lerp(armL, 0.55f, s);
        legL = Mathf.Lerp(legL, 0.35f, s * 0.6f); legR = Mathf.Lerp(legR, -0.3f, s * 0.6f);

        // Air: one knee up, arms out for balance.
        float a = _wAir;
        float rising = Mathf.Clamp(Velocity.Y / 6f, -1f, 1f);
        legL = Mathf.Lerp(legL, 0.75f + 0.2f * rising, a); kneeL = Mathf.Lerp(kneeL, 1.3f, a);
        legR = Mathf.Lerp(legR, -0.25f, a); kneeR = Mathf.Lerp(kneeR, 0.45f, a);
        armL = Mathf.Lerp(armL, 0.4f, a); armR = Mathf.Lerp(armR, -0.3f, a);
        splayL = Mathf.Lerp(splayL, -0.75f, a); splayR = Mathf.Lerp(splayR, 0.75f, a);

        // Dash: everything streams back behind a forward-thrown chest.
        float d = _wDash;
        lean = Mathf.Lerp(lean, -0.55f, d);
        armL = Mathf.Lerp(armL, -1.35f, d); armR = Mathf.Lerp(armR, -1.35f, d);
        splayL = Mathf.Lerp(splayL, -0.35f, d); splayR = Mathf.Lerp(splayR, 0.35f, d);
        elbowL = Mathf.Lerp(elbowL, 0.15f, d); elbowR = Mathf.Lerp(elbowR, 0.15f, d);
        legL = Mathf.Lerp(legL, -0.75f, d); kneeL = Mathf.Lerp(kneeL, 0.9f, d);
        legR = Mathf.Lerp(legR, 0.25f, d); kneeR = Mathf.Lerp(kneeR, 0.5f, d);

        // Slide: low, leaning back, lead leg straight out, trail leg folded.
        float sl = _wSlide;
        float drop = -_model.HipsOffset.Y * 0.62f * sl;
        lean = Mathf.Lerp(lean, 0.5f, sl);
        legL = Mathf.Lerp(legL, 1.45f, sl); kneeL = Mathf.Lerp(kneeL, 0.1f, sl);
        legR = Mathf.Lerp(legR, 0.55f, sl); kneeR = Mathf.Lerp(kneeR, 2.0f, sl);
        armL = Mathf.Lerp(armL, 1.1f, sl); splayL = Mathf.Lerp(splayL, -0.6f, sl);
        armR = Mathf.Lerp(armR, -0.7f, sl); splayR = Mathf.Lerp(splayR, 0.25f, sl);

        // Slam: a cannonball — knees to chest, fists overhead, nose down.
        float sm = _wSlam;
        lean = Mathf.Lerp(lean, -0.35f, sm);
        legL = Mathf.Lerp(legL, 1.5f, sm); kneeL = Mathf.Lerp(kneeL, 2.3f, sm);
        legR = Mathf.Lerp(legR, 1.4f, sm); kneeR = Mathf.Lerp(kneeR, 2.3f, sm);
        armL = Mathf.Lerp(armL, 2.9f, sm); armR = Mathf.Lerp(armR, 2.9f, sm);
        splayL = Mathf.Lerp(splayL, -0.2f, sm); splayR = Mathf.Lerp(splayR, 0.2f, sm);
        elbowL = Mathf.Lerp(elbowL, 0.3f, sm); elbowR = Mathf.Lerp(elbowR, 0.3f, sm);

        // Flip: one somersault on the double jump, tucked.
        float flipAngle = 0f;
        if (_flip >= 0f)
        {
            float t = _flip / FlipDuration;
            flipAngle = -Mathf.Tau * (1f - (1f - t) * (1f - t));
            float tuck = Mathf.Sin(t * Mathf.Pi);
            legL = Mathf.Lerp(legL, 1.6f, tuck); legR = Mathf.Lerp(legR, 1.6f, tuck);
            kneeL = Mathf.Lerp(kneeL, 2.2f, tuck); kneeR = Mathf.Lerp(kneeR, 2.2f, tuck);
            armL = Mathf.Lerp(armL, 1.2f, tuck); armR = Mathf.Lerp(armR, 1.2f, tuck);
        }

        if (_dying)
        {
            float dd = Ease(_death);
            armL = Mathf.Lerp(armL, 1.2f * _deathDir, dd); armR = Mathf.Lerp(armR, 0.9f * _deathDir, dd);
            kneeL = Mathf.Lerp(kneeL, 1.2f, dd); kneeR = Mathf.Lerp(kneeR, 0.8f, dd);
        }

        // Flip spins about the body's middle, not the hips.
        float midY = _model.Height * 0.2f;
        Vector3 flipOffset = flipAngle != 0f
            ? new Vector3(0f, midY - midY * Mathf.Cos(flipAngle), midY * Mathf.Sin(flipAngle))
            : Vector3.Zero;
        SetHips(flipOffset.X, bob + lift + drop + flipOffset.Y, lunge + flipOffset.Z, pitch + lean, roll, 0f,
                squash * breathe, flipAngle);

        for (int i = 0; i < _parts.Length; i++)
        {
            ref Part p = ref _parts[i];
            switch (p.Def.Role)
            {
                case PartRole.LegL: Limb(ref p, legL, 0f, -kneeL); break;
                case PartRole.LegR: Limb(ref p, legR, 0f, -kneeR); break;
                case PartRole.ArmL: Limb(ref p, armL, splayL, elbowL); break;
                case PartRole.ArmR: Limb(ref p, armR, splayR, elbowR); break;
                case PartRole.WingL: SetJoint(p.Upper, p.Def.Pivot, 0f, -0.25f + 0.15f * Mathf.Sin(_time * 3f), 0.5f); break;
                case PartRole.WingR: SetJoint(p.Upper, p.Def.Pivot, 0f, 0.25f - 0.15f * Mathf.Sin(_time * 3f), -0.5f); break;
            }
        }
    }

    /// <summary>A limb with an optional second joint. Unjointed limbs fold the
    /// bend into the upper angle so a knee-up still reads.</summary>
    private static void Limb(ref Part p, float x, float z, float bend)
    {
        if (p.Lower is null)
        {
            SetJoint(p.Upper, p.Def.Pivot, x + Mathf.Abs(bend) * 0.3f, z);
            return;
        }
        SetJoint(p.Upper, p.Def.Pivot, x, z);
        SetJoint(p.Lower, p.Def.LowerPivot, bend, 0f);
    }

    private void PoseQuadruped(float dt)
    {
        Common(dt, out float pitch, out float lift, out float squash, out float lunge, out _);
        float stride = Mathf.Max(0.2f, _model.StrideLength);
        _phase += dt * _speed / stride * Mathf.Tau;
        float run = Mathf.Clamp(_speed / 4f, 0f, 1.3f);
        float sn = Mathf.Sin(_phase);
        float swing = _model.LimbSwing * run;
        float bob = Mathf.Abs(Mathf.Cos(_phase)) * 0.04f * _model.Height * run;
        float rock = Mathf.Sin(_phase * 2f) * 0.04f * run;
        float breathe = 1f + 0.02f * Mathf.Sin(_time * 2.6f + _seed) * (1f - Mathf.Min(1f, run));
        // Windup for a four-legged thing is a crouch, not a raised arm.
        float crouch = _windupDur > 0f ? Ease(Mathf.Clamp(_windup / _windupDur, 0f, 1f)) : 0f;
        SetHips(0f, bob + lift - crouch * 0.12f * _model.Height, lunge * 1.4f, pitch * 0.6f + rock - 0.1f * _strike,
                0f, 0f, squash * breathe);

        for (int i = 0; i < _parts.Length; i++)
        {
            ref Part p = ref _parts[i];
            float a = p.Def.Role switch
            {
                PartRole.LegFL or PartRole.LegBR => sn * swing,
                PartRole.LegFR or PartRole.LegBL => -sn * swing,
                _ => 0f,
            };
            if (p.Def.Role is PartRole.WingL or PartRole.WingR)
            {
                SetJoint(p.Upper, p.Def.Pivot, 0f, p.Def.Side * (0.2f + 0.5f * Mathf.Sin(_time * 9f)));
                continue;
            }
            if (_dying) a = Mathf.Lerp(a, (p.Def.Role is PartRole.LegFL or PartRole.LegBL ? -0.6f : 0.6f), _death);
            SetJoint(p.Upper, p.Def.Pivot, a - crouch * 0.3f, 0f);
        }
    }

    private void PoseBlob(float dt)
    {
        Common(dt, out float pitch, out float lift, out float squash, out float lunge, out float wind);
        float stride = Mathf.Max(0.2f, _model.StrideLength);
        float run = Mathf.Clamp(_speed / 3f, 0f, 1.2f);
        _phase += dt * (_speed / stride * Mathf.Tau + 0.5f);
        // Hop: up in the air on |sin|, squashed at each landing.
        float hop = Mathf.Abs(Mathf.Sin(_phase));
        float y = hop * 0.22f * _model.Height * run;
        float sq = 1f + (hop - 0.35f) * 0.28f * run;
        float idle = 1f + 0.05f * Mathf.Sin(_time * 3.1f + _seed) * (1f - Mathf.Min(1f, run));
        float w = Ease(wind);
        SetHips(0f, y + lift, lunge * 1.5f, pitch * 0.5f - 0.12f * run, 0f, 0f,
                squash * sq * idle * (1f - 0.18f * w) + 0.25f * _strike);
    }

    private void PoseFlyer(float dt)
    {
        Common(dt, out float pitch, out float lift, out float squash, out float lunge, out float wind);
        float run = Mathf.Clamp(_speed / 4f, 0f, 1.2f);
        float bob = Mathf.Sin(_time * 2.4f + _seed * 7f) * 0.07f * _model.Height;
        float flapRate = 9f + 4f * run + 8f * _strike;
        float flap = Mathf.Sin(_time * flapRate + _seed * 3f);
        if (_dying) { bob = 0f; flap *= 1f - _death; lift = -_model.HipsOffset.Y * 0.8f * Ease(_death); }
        float w = Ease(wind);
        SetHips(0f, bob + lift + w * 0.1f * _model.Height, lunge * 1.6f,
                pitch - 0.3f * run + 0.25f * w, Mathf.Sin(_time * 1.3f + _seed) * 0.08f, 0f, squash);
        for (int i = 0; i < _parts.Length; i++)
        {
            ref Part p = ref _parts[i];
            float side = p.Def.Side;
            SetJoint(p.Upper, p.Def.Pivot, -0.2f * w, side * (0.15f + flap * 0.7f) - side * 0.5f * w, side * -0.25f);
        }
    }

    private void PoseSerpent(float dt)
    {
        Common(dt, out float pitch, out float lift, out float squash, out float lunge, out float wind);
        float stride = Mathf.Max(0.2f, _model.StrideLength);
        float run = Mathf.Clamp(_speed / 3f, 0.25f, 1.2f);
        _phase += dt * (_speed / stride * Mathf.Tau + 1.2f);
        float amp = _model.Height * 0.12f * run;
        float w = Ease(wind);
        float headSway = Mathf.Sin(_phase + 1.1f) * amp * 0.5f;
        // Windup coils the head back; the strike throws it forward.
        SetHips(headSway, lift, lunge * 1.8f + w * 0.1f * _model.Height, pitch * 0.5f,
                0f, Mathf.Cos(_phase + 1.1f) * 0.25f * run, squash);
        for (int i = 0; i < _parts.Length; i++)
        {
            ref Part p = ref _parts[i];
            float ph = _phase - (p.Def.Index + 1) * 0.85f;
            float x = Mathf.Sin(ph) * amp * (1f + p.Def.Index * 0.12f) - headSway;
            float yaw = Mathf.Cos(ph) * 0.5f * run;
            float sc = p.Def.Scale * (_dying ? 1f - 0.3f * _death : 1f);
            p.Upper.Transform = new Transform3D(Basis.FromEuler(new Vector3(0f, yaw, 0f)).Scaled(Vector3.One * sc),
                                                p.Def.Pivot + new Vector3(x, 0f, 0f));
        }
    }

}
