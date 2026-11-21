namespace Wavebreak.Meta;

/// <summary>Arabic for the harder Bell Room (clue tablets, the hum), Counting
/// Room (moves, changing counterweights) and Lantern Path (moon, fences,
/// lanterns going dark), and for the puzzle boons a solved room gives
/// (Game/Puzzle/PuzzleBoons, UI/PuzzleBoonHud).</summary>
public static partial class ArabicText
{
    internal static readonly (string En, string Ar)[] PuzzleHard3 =
    {
        // ---- Bell Room (Game/Puzzle/Kinds/RunesKind.cs)
        ("Read the clue tablets by the walls, work out the order and strike the bells in it. A wrong bell starts over.",
         "اقرأ ألواح الأدلة عند الجدران، واستنتج الترتيب، ثم اقرع الأجراس به. الجرس الخاطئ يعيدك إلى البداية."),
        ("Hint: the next bell is marked.", "تلميح: الجرس التالي معلَّم."),
        ("Bells struck in order: {0}/{1}  ·  the hum fades in {2} s", "أجراس قُرعت بالترتيب: {0}/{1}  ·  يخفت الطنين بعد {2} ث"),
        ("{0} rings before {1}", "{0} يرنّ قبل {1}"),
        ("{0} rings right after {1}", "{0} يرنّ مباشرة بعد {1}"),
        ("Strike number {1} is {0}", "القرعة رقم {1} هي {0}"),
        ("Strike number {1} is not {0}", "القرعة رقم {1} ليست {0}"),
        ("{0} and {1} are not struck back to back", "{0} و{1} لا يُقرعان متتاليين"),
        ("{0} stays silent", "{0} يبقى صامتاً"),
        ("Clue", "دليل"),
        // ---- Counting Room (Game/Puzzle/Kinds/ScalesKind.cs)
        ("Level the great balance with numbered weights. Each use of a pedestal moves its weight rack, left pan, right pan, rack, and costs a move; the counterweight may change when the beam levels.",
         "وازِن الميزان الكبير بالأثقال المرقّمة. كل استخدام لقاعدة ينقل ثقلها: الرف، فالكفّة اليسرى، فاليمنى، فالرف، ويكلّف نقلة؛ وقد يتغيّر الثقل المقابل كلما استوى الذراع."),
        ("Hint: use the marked weight's pedestal as many times as the number says. Out of moves? The post puts the weights back.",
         "تلميح: استخدم قاعدة الثقل المعلَّم بعدد المرات الذي يقوله الرقم. نفدت النقلات؟ العمود يعيد الأثقال إلى أماكنها."),
        ("Put the weights back", "أعِد الأثقال"),
        ("Moves left: {0}", "النقلات المتبقية: {0}"),
        ("Counterweight {0}  ·  moves left {1}  ·  levelled {2}/{3}", "الثقل المقابل {0}  ·  النقلات المتبقية {1}  ·  مرات الاستواء {2}/{3}"),
        // ---- Lantern Path (Game/Puzzle/Kinds/TrailKind.cs)
        ("Start on the star tile and light every tile in one unbroken path that never crosses itself, ending on the moon tile. Fences cannot be stepped across; a stalled path goes dark.",
         "ابدأ من بلاطة النجمة وأضئ كل البلاطات في درب واحد متصل لا يقطع نفسه، ينتهي عند بلاطة الهلال. لا تُعبَر الأسوار، والدرب المتوقف ينطفئ."),
        ("Tiles lit: {0}/{1}  ·  the lanterns go dark in {2} s", "بلاطات مضاءة: {0}/{1}  ·  تنطفئ الفوانيس بعد {2} ث"),
        // ---- puzzle boons (Game/Puzzle/PuzzleBoons.cs, UI/PuzzleBoonHud.cs)
        ("Puzzle boon: {0}", "مكافأة اللغز: {0}"),
        ("Lasts until this puzzle map ends.", "تدوم حتى نهاية خريطة الألغاز هذه."),
        ("Lasts for the rest of the gauntlet.", "تدوم لبقية تحدي الألغاز."),
        ("Boons", "المكافآت"),
        ("Swift Feet", "أقدام خفيفة"),
        ("+8% move speed.", "+8% سرعة الحركة."),
        ("Spring Heels", "كعب نابض"),
        ("+1 air jump.", "+1 قفزة في الهواء."),
        ("High Leap", "وثبة عالية"),
        ("+12% jump height.", "+12% ارتفاع القفز."),
        ("Quick Dash", "اندفاع سريع"),
        ("The dash recharges 18% faster.", "يتجدد الاندفاع أسرع بـ 18%."),
        ("Vigor", "حيوية"),
        ("+10 max health, and a 40% heal.", "+10 للصحة القصوى، وشفاء 40%."),
        ("Stone Skin", "جلد صخري"),
        ("+6 armour: spikes and guards hurt less.", "+6 درع: الأشواك والحرّاس يؤذون أقل."),
    };
}
