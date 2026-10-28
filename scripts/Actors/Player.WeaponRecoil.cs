using Godot;
using Wavebreak.Audio;
using Wavebreak.Combat.Weapons;

namespace Wavebreak.Actors;

public sealed partial class Player
{
    private Vector3 _swimWeaponRecoil;
    private ShotgunAudio? _shotgunAudio;
    private Vector3 _shotgunFacing;
    private float _shotgunFacingFor;
    public int ShotgunRecoilBursts { get; private set; }
    public float ShotgunRecoilSpeed => _swimWeaponRecoil.Length();
    public Vector3 PrepareShotgunMuzzle(Vector3 heading)
    {
        if (_rig.HoldsShotgun && heading.IsFinite() && new Vector2(heading.X, heading.Z).LengthSquared() > .01f)
        {
            _shotgunFacing = heading with { Y = 0 }; _shotgunFacingFor = .65f;
            _yaw = Mathf.Atan2(-heading.X, -heading.Z);
            _visual.Rotation = _visual.Rotation with { Y = _yaw };
        }
        _rig.AimShotgun(heading);
        return _rig.HoldsShotgun ? _rig.ShotgunMuzzle : CenterPosition + heading * .6f;
    }
    public void ShotgunFeedback(Vector3 heading)
    {
        if (!IsAlive || !heading.IsFinite()) return;
        if (heading.LengthSquared() < .01f) return;
        PrepareShotgunMuzzle(heading);
        // Replace rather than accumulate: attack-speed upgrades cannot push the swimmer backwards indefinitely.
        _swimWeaponRecoil = -heading.Normalized() * (IsSwimming ? .8f : .35f);
        if (!IsSwimming) _swimWeaponRecoil = _swimWeaponRecoil with { Y = 0 };
        _rig.AimShotgun(heading, true);
        Net.NetSession.Instance?.SendShotgunPose(heading);
        ShotgunRecoilBursts++;
        if (_shotgunAudio is null) { _shotgunAudio = new ShotgunAudio(); AddChild(_shotgunAudio); }
        _shotgunAudio.Play();
        if (IsSwimming) ShotgunBubbles.Spawn(CenterPosition + heading * .7f, heading);
        PlayerCamera.Instance?.Shake(.045f, .12f);
    }
    private Vector3 SwimmingRecoil(float dt)
    {
        Vector3 value = _swimWeaponRecoil;
        _swimWeaponRecoil = _swimWeaponRecoil.MoveToward(Vector3.Zero, 8 * dt);
        return value;
    }
}
