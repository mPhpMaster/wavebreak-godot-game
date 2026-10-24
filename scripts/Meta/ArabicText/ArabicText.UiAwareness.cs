namespace Wavebreak.Meta;

/// <summary>Arabic for the team-awareness HUD: minimap settings, name tags and
/// edge arrows, the boss-kill toast, the down alert and the scoreboard.</summary>
public static partial class ArabicText
{
    internal static readonly (string En, string Ar)[] UiAwareness =
    {
        // ---- settings (graphics page, CAMERA & HUD) and key bindings
        ("Show minimap", "إظهار الخريطة المصغّرة"),
        ("Minimap size", "حجم الخريطة المصغّرة"),
        ("Small", "صغير"),
        ("Large", "كبير"),
        ("Scoreboard (hold)", "لوحة النتائج (اضغط مطوّلًا)"),

        // ---- name tags, edge arrows, alerts
        ("{0}m", "{0} م"),
        ("{0} is down! Go revive them", "سقط {0}! روح أنهضه"),
        ("BOSS DEFEATED", "هُزم الزعيم"),
        ("{0} defeated {1}!", "{0} هزم {1}!"),

        // ---- scoreboard
        ("SCOREBOARD", "لوحة النتائج"),
        ("Bosses", "الزعماء"),
        ("Downs", "السقطات"),
        ("Revives", "الإنهاضات"),
        ("Damage dealt", "الضرر المُحدَث"),
        ("Gold earned", "الذهب المكتسب"),
        ("hold {0}", "اضغط مطوّلًا {0}"),
        ("hold {0}  ·  the host keeps the score", "اضغط مطوّلًا {0}  ·  المضيف يحسب النتائج"),
        ("SCOREBOARD  [{0}]", "لوحة النتائج  [{0}]"),

        // ---- results screen
        ("Held the Final Swarm for {0}  ·  {1} kills  ·  enemy hp +{2}%", "صمدت أمام الموجة الأخيرة {0}  ·  {1} إسقاط  ·  صحة الأعداء +{2}%"),
        ("Map {0} of {1}  ·  {2}", "الخريطة {0} من {1}  ·  {2}"),
        ("No items this run.", "ما فيه أغراض في هذه الجولة."),
        ("No Pearls this run.", "ما فيه لؤلؤ في هذه الجولة."),
        ("Enter  /  R", "Enter  أو  R"),
        ("TOTAL", "المجموع"),
        ("Balance", "الرصيد"),
        ("NEW BEST RUN", "أفضل جولة جديدة"),
        ("LEADERBOARD #{0}", "لوحة الصدارة #{0}"),
        ("MAX", "أقصى"),
        ("Lv {0}", "مستوى {0}"),
        // ---- pings
        ("Ping (mark a spot)", "إشارة (علّم مكانًا)"),
        ("Enemy here", "عدو هنا"),
        ("Go here", "روحوا هنا"),
        ("Shrine", "مزار"),
        ("Help!", "ساعدوني!"),
        ("{0}: {1}", "{0}: {1}"),
    };
}
