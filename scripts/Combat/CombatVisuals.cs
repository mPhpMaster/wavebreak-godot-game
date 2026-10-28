using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Core;

namespace Wavebreak.Combat;

/// <summary>
/// Shared geometry and materials for everything combat draws.
///
/// Every bolt of a given weapon is the same shape in the same colour, so building
/// a mesh or material per instance would allocate hundreds of copies of identical
/// data and give the renderer nothing to batch. Meshes are unit-sized and scaled
/// by the instance; materials are cached by colour.
/// </summary>
internal static class CombatVisuals
{
    private static readonly Dictionary<(Color Color, bool Additive), StandardMaterial3D> Materials = new();

    /// <summary>Unit bolt: radius 1, length 3, long axis on +Y.</summary>
    public static readonly CapsuleMesh Bolt = new()
    {
        Radius = 1f,
        Height = 3f,
        RadialSegments = 6,
        Rings = 2,
    };

    /// <summary>Unit sphere, radius 1.</summary>
    public static readonly SphereMesh Orb = new()
    {
        Radius = 1f,
        Height = 2f,
        RadialSegments = 8,
        Rings = 4,
    };

    /// <summary>Unit cube. Scaled into slabs and wedges.</summary>
    public static readonly BoxMesh Slab = new() { Size = Vector3.One };

    /// <summary>Flat unit disc, radius 1. The aura footprint.</summary>
    public static readonly CylinderMesh Disc = new()
    {
        TopRadius = 1f,
        BottomRadius = 1f,
        Height = 0.06f,
        RadialSegments = 28,
        Rings = 1,
    };

    /// <summary>
    /// A self-lit material in the given colour.
    ///
    /// Unshaded on purpose: a projectile has to be readable against whatever it
    /// happens to be flying over, and the alternative — relying on scene lighting
    /// to make a bullet visible — fails exactly when the screen is busiest. The
    /// colour is pushed past 1 so it blooms rather than sitting flat.
    /// </summary>
    /// <param name="color">Base colour. Alpha below 1 gives a translucent field.</param>
    /// <param name="additive">Add rather than blend. Right for auras and flashes,
    /// where overlapping copies should build up instead of hiding each other.</param>
    public static StandardMaterial3D Glow(Color color, bool additive)
    {
        (Color color, bool additive) key = (color, additive);
        if (Materials.TryGetValue(key, out StandardMaterial3D? cached) && GodotObject.IsInstanceValid(cached))
            return cached;

        StandardMaterial3D material = new()
        {
            AlbedoColor = new Color(color.R * 1.35f, color.G * 1.35f, color.B * 1.35f, color.A),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            // Alpha even when opaque: GeometryInstance3D.Transparency, which is
            // how everything here fades, needs a blendable material to write to.
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };

        if (additive) material.BlendMode = BaseMaterial3D.BlendModeEnum.Add;
        Weapons.FxGuard.Track(material);   // fades out between the camera and the player

        Materials[key] = material;
        return material;
    }
}
