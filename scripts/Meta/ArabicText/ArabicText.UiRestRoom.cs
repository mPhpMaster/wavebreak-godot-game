namespace Wavebreak.Meta;

/// <summary>Arabic for the rest room between maps: its HUD card, the campfire
/// prompts and popups, the CONTINUE sign, the co-op portal count and the
/// boss portal's closing countdown.</summary>
public static partial class ArabicText
{
    internal static readonly (string En, string Ar)[] UiRestRoom =
    {
        ("REST STOP", "استراحة"),
        ("REST STOP  ·  CLOCK PAUSED", "استراحة  ·  الوقت متوقف"),
        ("CONTINUE", "متابعة"),
        ("Shop at the merchant, rest by the campfire, then step into the portal.",
         "تسوّق عند التاجر، واسترح عند نار المخيم، ثم ادخل البوابة."),
        ("Next: {0}", "التالي: {0}"),
        ("In the portal: {0}/{1}", "في البوابة: {0}/{1}"),
        ("leaving in {0}s", "المغادرة بعد {0} ث"),
        ("rest by the campfire", "استرح عند نار المخيم"),
        ("{0} — rest by the campfire · heal to full", "{0} — استرح عند نار المخيم · صحة كاملة"),
        ("{0} — rest by the campfire (health full)", "{0} — استرح عند نار المخيم (صحتك كاملة)"),
        ("Rested: health full", "استرحت: صحتك كاملة"),
        ("already at full health", "صحتك كاملة أصلًا"),
        ("RestRoom", "الاستراحة"),
        // ---- boss portal countdown
        ("CLOSES IN {0}", "تُغلق بعد {0}"),
        ("PORTAL OPEN  —  GO  ·  CLOSES IN {0}", "البوابة مفتوحة  —  انطلق  ·  تُغلق بعد {0}"),
    };
}
