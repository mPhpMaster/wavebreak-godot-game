using Godot;

namespace Wavebreak.Visuals;

/// <summary>
/// How a skin repaints a character model. Imported Kenney models carry their
/// colours in a texture, so a plain tint cannot change them; this is applied
/// in the shader instead, per pixel, in two steps:
///
/// 1. an HSV adjustment (<see cref="HueShift"/> in turns, <see cref="Saturation"/>
///    and <see cref="Value"/> as multipliers), then
/// 2. a dye: the pixel takes <see cref="Dye"/>'s hue and most of its saturation
///    while keeping its own brightness, so shading and the texture's light/dark
///    pattern survive, blended in by <see cref="DyeAmount"/>.
///
/// Skin tones are detected (warm hue, moderate saturation) and protected by
/// <see cref="KeepSkin"/>, so a blue dye repaints the clothes, not the face.
/// Near-grey pixels (eyes, outlines, white trim) take the dye only partially.
///
/// <see cref="None"/> is exactly the original model. A record struct, so two
/// skins with the same numbers share one material (see
/// <see cref="ToonMaterials.Recolored"/>).
/// </summary>
public readonly record struct SkinRecolor(float HueShift, float Saturation, float Value, Color Dye, float DyeAmount, float KeepSkin = 0.85f)
{
    public static readonly SkinRecolor None = new(0f, 1f, 1f, Colors.White, 0f);

    public bool IsIdentity => HueShift == 0f && Saturation == 1f && Value == 1f && DyeAmount <= 0f;

    /// <summary>Repaint toward a colour: <paramref name="amount"/> 0..1 of the
    /// way, optionally brighter or darker overall.</summary>
    public static SkinRecolor Dyed(Color dye, float amount = 0.8f, float value = 1f, float saturation = 1f)
        => new(0f, saturation, value, dye, amount);

    /// <summary>Rotate every hue by <paramref name="turns"/> (0.5 = opposite colour).</summary>
    public static SkinRecolor Shifted(float turns, float saturation = 1f, float value = 1f)
        => new(turns, saturation, value, Colors.White, 0f);

    /// <summary>Shader parameter: (hue shift, saturation, value, dye amount).</summary>
    public Vector4 Adjust => new(HueShift, Saturation, Value, DyeAmount);

    /// <summary>Shader parameter: dye colour in rgb, skin protection in alpha.</summary>
    public Color DyeParam => new(Dye.R, Dye.G, Dye.B, KeepSkin);
}
