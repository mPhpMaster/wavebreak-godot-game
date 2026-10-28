using Godot;
using Wavebreak.Audio;
using Wavebreak.Combat.Weapons;

namespace Wavebreak.Actors;

public sealed partial class Player
{
    private Vector3 _swimWeaponRecoil;
    private ShotgunAudio? _shotgunAudio;
    public int ShotgunRecoilBursts { get; private set; }
    public void ShotgunFeedback(Vector3 heading)
    {
        if (!IsAlive || !heading.IsFinite()) return;
        Vector3 recoil = -heading.Normalized() * (IsSwimming ? 5 : 2.5f);
        if (IsSwimming) _swimWeaponRecoil = (_swimWeaponRecoil + recoil).LimitLength(10);
        else _externalVelocity = (_externalVelocity + (recoil with { Y = 0 })).LimitLength(16);
        ShotgunRecoilBursts++;
        if (_shotgunAudio is null) { _shotgunAudio = new ShotgunAudio(); AddChild(_shotgunAudio); }
        _shotgunAudio.Play();
        if (IsSwimming) ShotgunBubbles.Spawn(CenterPosition + heading * .7f, heading);
        PlayerCamera.Instance?.Shake(.045f, .12f);
    }
    private Vector3 SwimmingRecoil(float dt)
    {
        Vector3 value = _swimWeaponRecoil;
        _swimWeaponRecoil = _swimWeaponRecoil.MoveToward(Vector3.Zero, 10 * dt);
        return value;
    }
}
