using Godot;

namespace Wavebreak.Actors;

public sealed partial class Enemy
{
    private MeshInstance3D[]? _wounds;
    private int _woundLevel = -1;
    private static readonly StandardMaterial3D WoundMaterial = new()
    {
        AlbedoColor = new Color(.65f, .035f, .025f), EmissionEnabled = true,
        Emission = new Color(.22f, .01f, .005f), Roughness = 1,
    };
    private void RefreshWounds()
    {
        int level = IsCityGiant && !_dead && _maxHealth > 0 ? Mathf.Clamp((int)((1 - _health / _maxHealth) * 4), 0, 3) : 0;
        if (level == _woundLevel) return;
        _woundLevel = level;
        if (_wounds is null && level > 0)
        {
            _wounds = new MeshInstance3D[3];
            for (int i = 0; i < _wounds.Length; i++)
            {
                _wounds[i] = new MeshInstance3D
                {
                    Mesh = new BoxMesh { Size = new Vector3(1.4f, .22f, .1f) },
                    MaterialOverride = WoundMaterial, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                    Rotation = new Vector3(0, 0, .5f + i * .2f),
                };
                AddChild(_wounds[i]);
            }
        }
        if (_wounds is null) return;
        float height = _def?.Height ?? 26;
        for (int i = 0; i < _wounds.Length; i++)
        {
            _wounds[i].Position = new Vector3((i - 1) * .6f, height * (.58f + i * .04f), -height * .105f);
            _wounds[i].Visible = i < level;
        }
    }
}
