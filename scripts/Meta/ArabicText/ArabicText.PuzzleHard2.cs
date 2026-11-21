namespace Wavebreak.Meta;

/// <summary>Arabic for the harder Ice Hall, Weighing Room and Lamp Room
/// (Game/Puzzle/Kinds/GlideKind.cs, CratesKind.cs, LeversKind.cs): ordered
/// sockets, cracked ice, arrows and ice blocks; ordered plates that lock,
/// iron crates and the star plate; padlocked levers, the halo pattern and the
/// counted pulls.</summary>
public static partial class ArabicText
{
    internal static readonly (string En, string Ar)[] PuzzleHard2 =
    {
        // ---- Ice Hall
        ("Slide the rune stone onto its sockets in number order: it slides until something stops it. Arrows turn it, cracked ice becomes a ridge once crossed, and ice blocks slide too.",
         "ازلق الحجر الرونيّ إلى قواعده بترتيب أرقامها: ينزلق حتى يوقفه شيء. الأسهم تغيّر اتجاهه، والجليد المتشقق يصير حاجزاً بعد أن يعبره حجر، وكتل الجليد تنزلق هي أيضاً."),
        ("Hint: push the marked stone the way the arrow points. The post resets the hall.",
         "تلميح: ادفع الحجر المعلَّم باتجاه السهم. العمود يعيد القاعة إلى حالها الأولى."),
        ("Reset the hall", "أعِد القاعة"),
        ("Sockets: {0}/{1}  ·  pushes: {2}  ·  fewest: {3}", "القواعد: {0}/{1}  ·  الدفعات: {2}  ·  الأقل: {3}"),
        // ---- Weighing Room
        ("Push crates onto the plates in number order: only the next plate is open, and a crate on it locks for good. Round iron plates need an iron crate. Then stand on the star plate.",
         "ادفع الصناديق فوق الألواح بترتيب أرقامها: اللوح التالي وحده مفتوح، والصندوق الذي يستقر عليه يُقفَل نهائياً. الألواح الحديدية المستديرة تحتاج صندوقاً حديدياً. ثم قف على لوح النجمة."),
        ("Hint: the marked crate moves next, in the arrow's direction. The post resets the crates.",
         "تلميح: الصندوق المعلَّم يتحرك تالياً باتجاه السهم. العمود يعيد الصناديق إلى أماكنها."),
        ("Plates locked: {0}/{1}", "ألواح مقفلة: {0}/{1}"),
        // ---- Lamp Room
        ("Light exactly the lamps that wear a halo. A padlocked lever only moves while the lamp with its rune is lit. Pulls are counted: run out and the board resets.",
         "أضئ المصابيح التي تعلوها هالة فقط. الذراع المقفل لا يتحرك إلا والمصباح الذي يحمل رمزه مضاء. السحبات معدودة: إن نفدت عادت اللوحة إلى بدايتها."),
        ("Hint: pull the marked lever next. The post resets the board.", "تلميح: اسحب الذراع المعلَّم تالياً. العمود يعيد اللوحة إلى بدايتها."),
        ("Locked: light the lamp with its rune", "مقفل: أضئ المصباح الذي يحمل رمزه"),
        ("Reset the levers", "أعِد الأذرع"),
        ("Lamps right: {0}/{1}  ·  pulls left: {2}", "مصابيح صحيحة: {0}/{1}  ·  سحبات متبقية: {2}"),
    };
}
