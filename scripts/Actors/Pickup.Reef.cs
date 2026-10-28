using Godot;
using Wavebreak.Game;
using Wavebreak.Visuals;

namespace Wavebreak.Actors;

public sealed partial class Pickup
{
    private Node3D? _reefGem, _reefGold;
    private bool _reef;

    private void DressReef()
    {
        _reef = Arena.Current.Underwater;
        _mesh.Visible = !_reef || _kind == PickupKind.Health;
        if (_reefGem is not null) _reefGem.Visible = false;
        if (_reefGold is not null) _reefGold.Visible = false;
        if (!_reef || _kind == PickupKind.Health) return;
        ref Node3D? visual = ref (_kind == PickupKind.Gold ? ref _reefGold : ref _reefGem);
        if (visual is null)
        {
            string file = _kind == PickupKind.Gold ? "coin-gold" : "jewel";
            var scene = ResourceLoader.Load<PackedScene>($"res://assets/kenney/reef-pickups/{file}.glb");
            visual = scene.Instantiate<Node3D>();
            AddChild(visual);
        }
        visual.Visible = true;
        visual.Scale = _mesh.Scale * 1.8f;
        _tilt = Vector3.Zero;
    }

    private void StepReefVisual()
    {
        if (!_reef) return;
        Node3D? visual = _kind == PickupKind.Gold ? _reefGold : _reefGem;
        if (visual is not null) visual.Rotation = _settled && !_attracted && _kind == PickupKind.Gold
            ? new Vector3(0, _phase, 0) : _mesh.Rotation;
    }
}
