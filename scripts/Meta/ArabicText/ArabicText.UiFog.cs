namespace Wavebreak.Meta;

/// <summary>Arabic for the fog of war and the Tab sheet's big map, and the
/// Stormwatch music titles.</summary>
public static partial class ArabicText
{
    internal static readonly (string En, string Ar)[] UiFog =
    {
        ("{0}  ·  {1} explored", "{0}  ·  {1} مستكشفة"),
        ("Rest stop: no map here", "استراحة: ما فيه خريطة هنا"),
        ("No map", "ما فيه خريطة"),

        // ---- music (Boombox)
        ("Stormwatch Lanterns", "فوانيس مرصد العاصفة"),
        ("Lighthouse Shanty", "أهزوجة المنارة"),
        ("Stormwatch remix", "ريمكس مرصد العاصفة"),
    };
}
