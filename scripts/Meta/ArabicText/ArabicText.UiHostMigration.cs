namespace Wavebreak.Meta;

/// <summary>Arabic for host migration, late join and the public games list
/// (Net/NetSession.Migration.cs, NetSession.Snapshot.cs, Net/PublicGames.cs,
/// UI/CoopPage.Browse.cs).</summary>
public static partial class ArabicText
{
    internal static readonly (string En, string Ar)[] UiHostMigration =
    {
        // ---- host migration
        ("The host", "المضيف"),
        ("{0} left — you are now the host", "غادر {0} — أنت المضيف الآن"),
        ("{0} left — {1} is now the host", "غادر {0} — {1} هو المضيف الآن"),
        ("Reconnecting to {0}...", "إعادة الاتصال بـ {0}..."),
        ("Could not reach the new host.", "تعذّر الوصول إلى المضيف الجديد."),
        ("Could not take over as the host ({0}).", "تعذّر تولّي الاستضافة ({0})."),
        ("You are the host now.", "أنت المضيف الآن."),
        ("Back in the game.", "عدت إلى اللعبة."),

        // ---- late join
        ("{0} joined the game", "انضم {0} إلى اللعبة"),
        ("That game is ending. Try again in a moment.", "تلك اللعبة توشك على الانتهاء. حاول بعد قليل."),
        ("That game is starting. Try again in a moment.", "تلك اللعبة تبدأ الآن. حاول بعد قليل."),

        // ---- public / invite only, BROWSE PUBLIC GAMES
        ("Lobby", "الردهة"),
        ("Public", "عامة"),
        ("Invite only", "بالدعوة فقط"),
        ("Anyone can find this game under BROWSE PUBLIC GAMES and join, also during the run.",
         "يمكن لأي أحد أن يجد هذه اللعبة في «تصفّح الألعاب العامة» وينضم إليها، حتى أثناء الجولة."),
        ("Only friends you invite can join, also during the run.",
         "ينضم فقط الأصدقاء الذين تدعوهم، حتى أثناء الجولة."),
        ("BROWSE PUBLIC GAMES", "تصفّح الألعاب العامة"),
        ("REFRESH", "تحديث"),
        ("Looking for public games...", "جارٍ البحث عن ألعاب عامة..."),
        ("No public games right now. Host one!", "لا توجد ألعاب عامة الآن. استضف واحدة!"),
        ("Unknown host", "مضيف مجهول"),
        ("In the lobby", "في الردهة"),
        ("Map {0}/{1}", "الخريطة {0}/{1}"),
        ("Resting after map {0}/{1}", "استراحة بعد الخريطة {0}/{1}"),
    };
}
