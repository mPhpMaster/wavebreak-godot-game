using Godot;
using Wavebreak.Visuals;

namespace Wavebreak.Actors;

public sealed partial class Pickup
{


    private void DressPickupVisual()
    {

        _mesh.Visible = true;
        if (_kind == PickupKind.Health) return;
        if (PickupModels.Get(_kind) is not { } imported) return;
        _mesh.Mesh = imported;
        _tilt = Vector3.Zero;
    }
}

