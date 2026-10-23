using Godot;

namespace Wavebreak.Visuals;

/// <summary>
/// Skin recolouring on a rig (see <see cref="SkinRecolor"/>).
///
/// Procedural models already draw with <see cref="ToonMaterials.Character"/>,
/// which reads the recolour from two instance uniforms, so only those are set.
/// Imported models draw with their own StandardMaterial3D, which knows nothing
/// of recolouring; each surface gets a surface override with the cached
/// stand-in from <see cref="ToonMaterials.Recolored"/> (same texture, same
/// colour, plus the recolour), and then the same instance uniforms. The hit
/// flash and elite rim are material overlays, so they keep working on top.
/// An identity recolour removes the overrides, leaving the artist's material.
/// </summary>
public sealed partial class CharacterRig
{
    private static readonly StringName PRecolor = "recolor", PRecolorTint = "recolor_tint";

    public SkinRecolor Recolor { get; private set; } = SkinRecolor.None;

    public void SetRecolor(SkinRecolor recolor)
    {
        Recolor = recolor;
        bool identity = recolor.IsIdentity;
        Vector4 adjust = recolor.Adjust;
        Color dye = recolor.DyeParam;

        foreach (GeometryInstance3D g in _geoms)
        {
            if (Spec.IsImported && g is MeshInstance3D mi && mi.Mesh is { } mesh) RecolorImported(mi, mesh, identity);
            g.SetInstanceShaderParameter(PRecolor, adjust);
            g.SetInstanceShaderParameter(PRecolorTint, dye);
        }
    }

    /// <summary>The model's own materials, where the importer put them on the
    /// node rather than the mesh, so an identity recolour can put them back.</summary>
    private System.Collections.Generic.Dictionary<(MeshInstance3D, int), Material>? _ownMaterials;

    private void RecolorImported(MeshInstance3D mi, Mesh mesh, bool identity)
    {
        // A whole-node override (rare in imports) wins over surface overrides.
        if (mi.MaterialOverride is { } whole && whole is not ShaderMaterial)
        {
            _ownMaterials ??= new();
            _ownMaterials[(mi, -1)] = whole;
        }
        if (_ownMaterials is not null && _ownMaterials.TryGetValue((mi, -1), out Material? ownWhole))
        {
            mi.MaterialOverride = identity ? ownWhole : ToonMaterials.Recolored(ownWhole) ?? ownWhole;
            return;
        }

        int surfaces = mesh.GetSurfaceCount();
        for (int s = 0; s < surfaces; s++)
        {
            Material? own = mesh.SurfaceGetMaterial(s);
            if (own is null)
            {
                Material? current = mi.GetSurfaceOverrideMaterial(s);
                if (current is not null && current is not ShaderMaterial)
                {
                    _ownMaterials ??= new();
                    _ownMaterials[(mi, s)] = current;
                }
                if (_ownMaterials is null || !_ownMaterials.TryGetValue((mi, s), out own)) continue;
                mi.SetSurfaceOverrideMaterial(s, identity ? own : ToonMaterials.Recolored(own) ?? own);
                continue;
            }
            mi.SetSurfaceOverrideMaterial(s, identity ? null : ToonMaterials.Recolored(own));
        }
    }
}
