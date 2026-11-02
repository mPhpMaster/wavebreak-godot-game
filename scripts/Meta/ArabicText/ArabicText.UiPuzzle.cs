namespace Wavebreak.Meta;

/// <summary>Arabic for the puzzle route (Game/Puzzle): the route, the puzzle
/// maps' names, rooms, objectives, hints, prompts, symbols, medals, prizes and
/// the puzzle card on the HUD.</summary>
public static partial class ArabicText
{
    internal static readonly (string En, string Ar)[] UiPuzzle =
    {
        // ---- the route
        ("Puzzle Route", "مسار الألغاز"),
        ("Classic's maps, each after a short puzzle map. Solve it fast for a better reward for the fight ahead.",
         "خرائط الوضع الكلاسيكي، وقبل كل منها خريطة ألغاز قصيرة. حُلّها بسرعة لتنال مكافأة أفضل للمعركة القادمة."),
        ("Puzzle", "لغز"),
        // ---- puzzle maps
        ("Tidelock Vaults", "أقبية قفل المدّ"),
        ("Mirrorwater Grotto", "مغارة المياه المرآتية"),
        ("Bellstone Halls", "قاعات حجر الأجراس"),
        ("Windward Locks", "أقفال مهبّ الريح"),
        ("Lantern Works", "ورشة الفوانيس"),
        ("Saltgear Cloister", "دير التروس المالحة"),
        // ---- rooms
        ("Antechamber", "الردهة"),
        ("Weighing Room", "غرفة الأوزان"),
        ("Mirror Room", "غرفة المرايا"),
        ("Gust Room", "غرفة الهبّات"),
        ("Bell Room", "غرفة الأجراس"),
        ("Lamp Room", "غرفة المصابيح"),
        ("Twin Plates", "اللوحان التوأمان"),
        ("Reward Vault", "قبو المكافآت"),
        // ---- objectives
        ("Head through the door. Solve each room to open the next.", "اعبر الباب. حُلّ كل غرفة ليُفتح باب التالية."),
        ("Push crates onto the plates, then stand on the last one.", "ادفع الصناديق فوق الألواح، ثم قف على اللوح الأخير."),
        ("Turn the mirrors until the beam reaches the crystal.", "أدِر المرايا حتى يصل الشعاع إلى البلّورة."),
        ("Cross the gust rings while the wind is calm and touch every plate.", "اعبر حلقات الهبّات حين تهدأ الريح والمس كل لوح."),
        ("Strike the bells in the order they rang.", "اقرع الأجراس بالترتيب الذي رنّت به."),
        ("Pull levers until every lamp stands tall and lit.", "اسحب الأذرع حتى يقف كل مصباح عالياً مضاءً."),
        ("Step on one plate, then reach the other before its latch runs out.", "قف على لوح، ثم اذهب إلى الآخر قبل أن ينتهي قفله."),
        ("Two players: stand on both plates at the same time.", "لاعبان: قفا على اللوحين في الوقت نفسه."),
        ("Claim one reward, then step into the exit portal.", "خذ مكافأة واحدة، ثم ادخل بوابة الخروج."),
        // ---- hints
        ("Hint: the marked crate moves next, in the arrow's direction. The bell resets the crates.",
         "تلميح: الصندوق المعلَّم يتحرك تالياً باتجاه السهم. الجرس يعيد الصناديق إلى أماكنها."),
        ("Hint: the marked mirror is turned the wrong way.", "تلميح: المرآة المعلَّمة مُدارة في الاتجاه الخطأ."),
        ("Hint: wait at the ring's edge until the arrows drop, then run straight in.", "تلميح: انتظر عند حافة الحلقة حتى تنخفض الأسهم، ثم اركض إلى الداخل مباشرة."),
        ("Hint: the next bell is marked. Strike the centre bell to hear the order again.", "تلميح: الجرس التالي معلَّم. اقرع الجرس الأوسط لتسمع الترتيب مرة أخرى."),
        ("Hint: the marked lever is part of the answer.", "تلميح: الذراع المعلَّم جزء من الحل."),
        ("Hint: the plate keeps its latch for a few seconds after you step off.", "تلميح: يبقى اللوح مقفلاً بضع ثوانٍ بعد أن تنزل عنه."),
        ("Hint: solve the room to open its door.", "تلميح: حُلّ الغرفة ليُفتح بابها."),
        // ---- prompts
        ("Turn the mirror", "أدِر المرآة"),
        ("Pull the lever", "اسحب الذراع"),
        ("Strike the bell", "اقرع الجرس"),
        ("Ring the order again", "أعِد قرع الترتيب"),
        ("Reset the crates", "أعِد الصناديق"),
        ("Claim: {0}", "خذ: {0}"),
        // ---- bell symbols
        ("Triangle", "مثلث"),
        ("Circle", "دائرة"),
        ("Square", "مربع"),
        ("Diamond", "معيَّن"),
        ("Star", "نجمة"),
        ("Cross", "علامة زائد"),
        // ---- medals and prizes
        ("GOLD MEDAL", "ميدالية ذهبية"),
        ("SILVER MEDAL", "ميدالية فضية"),
        ("BRONZE MEDAL", "ميدالية برونزية"),
        ("SKIPPED", "تم التخطي"),
        ("Epic chest", "صندوق ملحمي"),
        ("Rare chest", "صندوق نادر"),
        ("Evolution catalyst", "محفّز التطور"),
        ("Weapon pick", "اختيار سلاح"),
        ("An item of epic rarity or better.", "غرض بندرة ملحمية أو أعلى."),
        ("An item of rare rarity or better.", "غرض بندرة نادرة أو أعلى."),
        ("An item, as from a chest.", "غرض، كما يخرج من صندوق."),
        ("The catalyst one of your weapons evolves with.", "المحفّز الذي يتطور به أحد أسلحتك."),
        ("A new weapon, or a level for one you hold.", "سلاح جديد، أو مستوى لسلاح تحمله."),
        ("Extra level-up rerolls.", "إعادات سحب إضافية عند رفع المستوى."),
        ("Evolves {0}", "يطوّر {0}"),
        // ---- the HUD
        ("PUZZLE  ·  CLOCK PAUSED", "لغز  ·  الوقت متوقف"),
        ("PUZZLE {0}/{1}  —  {2}", "لغز {0}/{1}  —  {2}"),
        ("PUZZLE {0}  ·  {1}", "لغز {0}  ·  {1}"),
        ("Solve every room. Faster, with fewer hints, pays better.", "حُلّ كل الغرف. الأسرع وبتلميحات أقل يُكافأ أكثر."),
        ("Room {0}/{1}  ·  {2}  ·  solved {3}/{1}", "الغرفة {0}/{1}  ·  {2}  ·  المحلولة {3}/{1}"),
        ("{0}  ·  solved in {1}", "{0}  ·  حُلّ في {1}"),
        ("Time {0}  ·  SKIP PORTAL OPEN (lowest reward)", "الوقت {0}  ·  بوابة التخطي مفتوحة (أدنى مكافأة)"),
        ("Time {0}  ·  gold under {1}  ·  skip portal in {2}", "الوقت {0}  ·  الذهبية تحت {1}  ·  بوابة التخطي بعد {2}"),
        ("Hints used: {0}  ·  next hint in {1}s", "التلميحات المستخدمة: {0}  ·  التلميح التالي بعد {1} ث"),
        ("{0}: hint (costs the gold medal)  ·  used: {1}", "{0}: تلميح (يُفقدك الميدالية الذهبية)  ·  المستخدمة: {1}"),
        ("In an exit: {0}/{1}", "عند المخرج: {0}/{1}"),
        ("Skipped. A small consolation is on its way.", "تم التخطي. جائزة ترضية صغيرة في الطريق."),
        ("Solved in {0}  ·  hints: {1}", "حُلّ في {0}  ·  التلميحات: {1}"),
        ("Claim one reward in the vault, then take the exit.", "خذ مكافأة واحدة من القبو، ثم اسلك المخرج."),
        ("SOLVED", "تم الحل"),
        ("GUST  ·  calm in {0}s", "هبّة  ·  الهدوء بعد {0} ث"),
        ("CALM  ·  gust in {0}s", "هدوء  ·  الهبّة بعد {0} ث"),
        ("plates {0}/{1}", "الألواح {0}/{1}"),
        ("Listen and watch the order...", "استمع وراقب الترتيب..."),
        ("Bells struck in order: {0}/{1}", "أجراس قُرعت بالترتيب: {0}/{1}"),
        ("Lamps lit: {0}/{1}", "مصابيح مضاءة: {0}/{1}"),
        ("Plates held: {0}/{1}", "ألواح مضغوطة: {0}/{1}"),
        ("Stand by a mirror to turn it.", "قف بجانب مرآة لتديرها."),
        ("A held", "A مضغوط"),
        ("A free", "A حر"),
        ("B held", "B مضغوط"),
        ("B free", "B حر"),
        ("EXIT", "خروج"),
        ("Help, puzzle hint", "المساعدة، تلميح اللغز"),
    };

    /// <summary>The puzzle route's quests (Meta/Quests.Mastery.cs).</summary>
    internal static readonly (string En, string Ar)[] ContentPuzzle =
    {
        ("Puzzle Box", "صندوق الألغاز"),
        ("Solve 3 puzzle maps in total.", "حُلّ 3 خرائط ألغاز إجمالاً."),
        ("Master Locksmith", "معلّم الأقفال"),
        ("Solve 12 puzzle maps in total.", "حُلّ 12 خريطة ألغاز إجمالاً."),
        ("Golden Logic", "منطق ذهبي"),
        ("Earn a gold medal on a puzzle map.", "احصل على ميدالية ذهبية في خريطة ألغاز."),
    };
}
