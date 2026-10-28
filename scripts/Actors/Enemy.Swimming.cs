using Godot;
using Wavebreak.Game;

namespace Wavebreak.Actors;

public sealed partial class Enemy
{
    private void UpdateMarinePose(Vector3 velocity)
    {
        if (_rig is null) return;
        bool reef = Arena.Current.Underwater;
        _rig.ManualMotion = reef;
        if (!reef) { _rig.Swimming = false; return; }
        _rig.Velocity = velocity;
        _rig.Grounded = IsOnFloor();
        _rig.Swimming = !IsOnFloor();
    }
}
