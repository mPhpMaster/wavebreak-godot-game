using Godot;

namespace Wavebreak.Visuals;

public sealed partial class GrappleTether : MeshInstance3D
{
    public override void _Ready()
    {
        TopLevel = true;
        GlobalTransform = Transform3D.Identity;
        Mesh = new CylinderMesh { TopRadius = 0.035f, BottomRadius = 0.035f, Height = 1f, RadialSegments = 6, Rings = 1 };
        MaterialOverride = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(0.74f, 0.95f, 1f),
            EmissionEnabled = true, Emission = new Color(0.4f, 0.8f, 1f),
        };
        CastShadow = ShadowCastingSetting.Off;
    }

    public void Set(Vector3 from, Vector3 to, bool active)
    {
        Visible = active;
        if (!active) return;
        Vector3 delta = to - from;
        float length = delta.Length();
        if (length < 0.01f) { Visible = false; return; }
        Basis aligned = MeshKit.BasisAlong(delta);
        var stretched = new Basis(aligned.X, aligned.Y * length, aligned.Z);
        GlobalTransform = new Transform3D(stretched, (from + to) * 0.5f);
    }
}
