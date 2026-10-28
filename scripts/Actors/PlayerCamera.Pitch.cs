using Godot;

namespace Wavebreak.Actors;

public partial class PlayerCamera
{
    public void SetCityCollision(bool city)
    {
        if (_arm is not null) _arm.CollisionMask = CollisionMask | (city ? 1u << 6 : 0u);
    }

    public void SetPitchDegrees(float degrees)
    {
        if (!float.IsFinite(degrees)) return;
        _pitch = Mathf.Clamp(Mathf.DegToRad(degrees), Mathf.DegToRad(MinPitchDegrees), Mathf.DegToRad(MaxPitchDegrees));
    }
}
