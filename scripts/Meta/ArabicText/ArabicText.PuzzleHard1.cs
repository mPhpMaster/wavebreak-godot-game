namespace Wavebreak.Meta;

/// <summary>Arabic for the harder Ember Hall, Gust Room, Mirror Room and
/// Aqueduct Room (Game/Puzzle/Kinds: BraziersKind, WindKind, MirrorsKind,
/// PipesKind).</summary>
public static partial class ArabicText
{
    internal static readonly (string En, string Ar)[] PuzzleHard1 =
    {
        // ---- Ember Hall (BraziersKind.cs)
        ("Using a brazier flips it and every brazier its chains lead to. Make each one match its plaque: a star burns, a cross stays dark.",
         "استعمال المجمرة يقلب حالها وحال كل مجمرة تقود إليها سلاسلها. اجعل كل واحدة تطابق لوحتها: النجمة تشتعل، والصليب يبقى مطفأً."),
        ("Hint: the marked brazier belongs to the answer. Use each brazier at most once.",
         "تلميح: المجمرة المعلَّمة من الحل. استعمل كل مجمرة مرة واحدة على الأكثر."),
        ("Light or douse the brazier", "أشعل المجمرة أو أطفئها"),
        ("Braziers as their plaques: {0}/{1}", "مجامر تطابق لوحاتها: {0}/{1}"),
        ("fuse {0}s, lit by the first brazier you use", "الفتيل {0} ث، يشتعل مع أول مجمرة تستعملها"),
        ("fuse burning: {0}s left", "الفتيل يحترق: بقي {0} ث"),

        // ---- Gust Room (WindKind.cs)
        ("Step on the numbered plates in order. A plate out of order sends you back to 1; in harder rooms so does a gust that catches you in its ring, or running out of time.",
         "قف على الألواح المرقّمة بالترتيب. اللوح الخطأ يعيدك إلى 1، وفي الغرف الأصعب كذلك هبّةٌ تمسكك داخل حلقتها أو نفاد الوقت."),
        ("Hint: the marked plate is next. Cross a ring only while its arrows lie flat.",
         "تلميح: اللوح المعلَّم هو التالي. لا تعبر حلقة إلا وأسهمها مستلقية."),
        ("next plate: {0} of {1}", "اللوح التالي: {0} من {1}"),
        ("{0}s left", "بقي {0} ث"),
        ("limit {0}s from plate 1", "المهلة {0} ث من اللوح 1"),

        // ---- Mirror Room (MirrorsKind.cs)
        ("Turn the mirrors so every crystal is lit by the beam of its own lamp (same shape). Glass splitters send light on and aside; mirrors sharing a rune turn together.",
         "أدِر المرايا حتى تضيء كل بلّورة بشعاع مصباحها (الشكل نفسه). المقسّمات الزجاجية ترسل الضوء إلى الأمام وإلى الجانب، والمرايا التي تحمل الرمز نفسه تدور معاً."),
        ("Crystals lit: {0}/{1}", "البلّورات المضيئة: {0}/{1}"),

        // ---- Aqueduct Room (PipesKind.cs)
        ("Turn the pipe tiles until water from the spouts fills every basin without a single leak. Bolted tiles never turn; a chained tile also turns the tile its chain points to.",
         "أدِر بلاطات الأنابيب حتى يملأ ماء المصبّات كل حوض دون أي تسرّب. البلاطات المثبّتة بالمسامير لا تدور، والبلاطة المربوطة بسلسلة تدير معها البلاطة التي تشير إليها السلسلة."),
        ("Basins filled: {0}/{1}  ·  leaks: {2}", "الأحواض الممتلئة: {0}/{1}  ·  التسرّبات: {2}"),
    };
}
