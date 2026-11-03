namespace Wavebreak.Meta;

/// <summary>Arabic for the puzzle overhaul: the new room kinds (ice, pipes,
/// braziers, scales, lantern path), the Puzzle Gauntlet (PUZZLES ONLY on the
/// PLAY page and in the CO-OP lobby) and its results screen.</summary>
public static partial class ArabicText
{
    internal static readonly (string En, string Ar)[] UiPuzzle2 =
    {
        // ---- Ice Hall (Game/Puzzle/Kinds/GlideKind.cs)
        ("Ice Hall", "قاعة الجليد"),
        ("Push the rune stone across the ice: it slides until it hits something. Stop it on its socket.",
         "ادفع الحجر الرونيّ على الجليد: ينزلق حتى يصطدم بشيء. أوقفه على قاعدته."),
        ("Hint: push the marked stone the way the arrow points. The post resets the stone.",
         "تلميح: ادفع الحجر المعلَّم باتجاه السهم. العمود يعيد الحجر إلى مكانه."),
        ("Reset the stone", "أعِد الحجر"),
        ("Pushes: {0}  ·  fewest: {1}", "الدفعات: {0}  ·  الأقل: {1}"),
        // ---- Aqueduct Room (PipesKind.cs)
        ("Aqueduct Room", "غرفة القناة"),
        ("Stand on a pipe tile and turn it until the water runs from the spout to the basin.",
         "قف على بلاطة أنبوب وأدِرها حتى يجري الماء من الصنبور إلى الحوض."),
        ("Hint: turn the marked tile as many times as its number says.", "تلميح: أدِر البلاطة المعلَّمة بعدد المرات الذي يقوله رقمها."),
        ("Turn the pipe", "أدِر الأنبوب"),
        ("Pipes carrying water: {0}", "أنابيب يجري فيها الماء: {0}"),
        // ---- Ember Hall (BraziersKind.cs)
        ("Ember Hall", "قاعة الجمر"),
        ("Light every brazier so that all of them burn at the same time. Each one burns out after a while.",
         "أشعل كل المجامر بحيث تشتعل كلها في الوقت نفسه. كل مجمرة تنطفئ بعد مدة."),
        ("Hint: light the marked brazier next: it starts the shortest round.", "تلميح: أشعل المجمرة المعلَّمة تالياً: منها تبدأ أقصر جولة."),
        ("Light the brazier", "أشعل المجمرة"),
        ("Braziers burning: {0}/{1}  ·  each burns {2}s", "مجامر مشتعلة: {0}/{1}  ·  كل واحدة تشتعل {2} ث"),
        // ---- Counting Room (ScalesKind.cs)
        ("Counting Room", "غرفة الحساب"),
        ("Level the great balance: move numbered weights from their pedestals onto the pans.",
         "اجعل الميزان الكبير مستوياً: انقل الأثقال المرقّمة من قواعدها إلى الكفّتين."),
        ("Hint: use the marked weight's pedestal as many times as the number says.",
         "تلميح: استخدم قاعدة الثقل المعلَّم بعدد المرات الذي يقوله الرقم."),
        ("Move the weight", "انقل الثقل"),
        ("Left pan {0}  ·  right pan {1}", "الكفّة اليسرى {0}  ·  الكفّة اليمنى {1}"),
        // ---- Lantern Path (TrailKind.cs)
        ("Lantern Path", "درب الفوانيس"),
        ("Start on the star tile and light every tile in one unbroken path: each step must go to a tile next to the last one lit.",
         "ابدأ من بلاطة النجمة وأضئ كل البلاطات في درب واحد متصل: كل خطوة إلى بلاطة بجانب آخر بلاطة أُضيئت."),
        ("Hint: step onto the marked tile next. The post starts the path over.", "تلميح: اخطُ على البلاطة المعلَّمة تالياً. العمود يبدأ الدرب من جديد."),
        ("Put the lanterns out", "أطفئ الفوانيس"),
        ("Tiles lit: {0}/{1}", "بلاطات مضاءة: {0}/{1}"),
        // ---- the Puzzle Gauntlet
        ("Puzzle Gauntlet", "تحدي الألغاز"),
        ("Six puzzle maps back to back, each harder than the last. No fights: just medals.",
         "ست خرائط ألغاز متتالية، كل واحدة أصعب مما قبلها. بلا قتال: الميداليات فقط."),
        ("Step into the exit portal for the next puzzle.", "ادخل بوابة الخروج إلى اللغز التالي."),
        ("PUZZLES ONLY: ON", "ألغاز فقط: مُفعّل"),
        ("PUZZLES ONLY: OFF", "ألغاز فقط: مُطفأ"),
        ("Six puzzle maps back to back, no fights. Press to go back to the route you had.",
         "ست خرائط ألغاز متتالية بلا قتال. اضغط للعودة إلى مسارك السابق."),
        ("Press for the Puzzle Gauntlet: six puzzle maps back to back, no fights, a medal for each.",
         "اضغط لتحدي الألغاز: ست خرائط ألغاز متتالية بلا قتال، وميدالية لكل واحدة."),
        ("{0} puzzles, no fights", "{0} ألغاز، بلا قتال"),
        ("best {0}/{1}", "الأفضل {0}/{1}"),
        ("Puzzle maps only, back to back, for everyone.", "خرائط ألغاز فقط، متتالية، للجميع."),
        // ---- results
        ("PUZZLES COMPLETE", "اكتملت الألغاز"),
        ("GAUNTLET OVER", "انتهى التحدي"),
        ("{0}  ·  puzzles {1}/{2}  ·  score {3}/{4}", "{0}  ·  الألغاز {1}/{2}  ·  النقاط {3}/{4}"),
        ("NEW BEST GAUNTLET", "أفضل تحدٍّ جديد"),
        ("Best score", "أفضل نتيجة"),
        ("Puzzle medals", "ميداليات الألغاز"),
    };
}
