namespace Wavebreak.Meta;

/// <summary>Arabic for the compact level-up hand, the auto-pick toast,
/// Settings > Gameplay and the CO-OP lobby's line-up, hat and difficulty rows.</summary>
public static partial class ArabicText
{
    internal static readonly (string En, string Ar)[] UiCoopUx =
    {
        // ---- compact level-up and auto-pick (UI/LevelUpPanel.Compact.cs)
        ("1 · 2 · 3 or click", "1 · 2 · 3 أو انقر"),
        ("Auto-picked: {0} ({1})", "اختيار تلقائي: {0} ({1})"),

        // ---- Settings > Gameplay (UI/GameplayPanel.cs)
        ("GAMEPLAY", "اللعب"),
        ("Compact level-up", "ارتقاء مصغّر"),
        ("Auto-pick on level up", "اختيار تلقائي عند الارتقاء"),
        ("Auto-pick rule", "قاعدة الاختيار التلقائي"),
        ("Auto-pick starts at", "يبدأ الاختيار التلقائي من"),
        ("First level-up", "أول ترقية"),
        ("Level {0}", "المستوى {0}"),
        ("Best rarity first", "الأندر أولًا"),
        ("Prefer weapons", "الأسلحة أولًا"),
        ("Prefer items", "الأغراض أولًا"),
        ("Prefer tomes", "الكتب أولًا"),
        ("Upgrade what I have", "طوّر ما أملكه"),
        ("Random", "عشوائي"),
        ("Compact level-up: small cards at the bottom of the screen and the game keeps running, so you can keep moving while you choose " +
         "(pick with 1 / 2 / 3 or a click). Co-op always uses it. " +
         "Auto-pick: every level-up takes a card at once by the rule, and a note shows what it took.",
         "الارتقاء المصغّر: بطاقات صغيرة أسفل الشاشة واللعبة مستمرة، فتواصل الحركة وأنت تختار " +
         "(اختر بـ 1 / 2 / 3 أو بالنقر). اللعب الجماعي يستخدمه دائمًا. " +
         "الاختيار التلقائي: كل ارتقاء يأخذ بطاقة فورًا حسب القاعدة، وتظهر ملاحظة بما أخذه."),

        // ---- CO-OP page (UI/CoopPage.cs, UI/CoopLineup.cs)
        ("Hat", "القبعة"),
        ("Difficulty", "الصعوبة"),
        ("{0} (the host picks)", "{0} (يختارها المضيف)"),
        ("PLAYERS {0}/{1}   ·   ready {2}/{3}", "اللاعبون {0}/{1}   ·   جاهزون {2}/{3}"),
    };
}
