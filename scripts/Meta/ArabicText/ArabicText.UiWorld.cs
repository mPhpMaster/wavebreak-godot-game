namespace Wavebreak.Meta;

/// <summary>Arabic for the in-world UI: results screen, chest/station/shrine
/// prompts and panels, world popups and banners, challenge runtimes.</summary>
public static partial class ArabicText
{
    internal static readonly (string En, string Ar)[] UiWorld =
    {
        // ---- results screen
        ("RUN OVER", "انتهت الجولة"),
        ("RUN", "الجولة"),
        ("BUILD", "العُدّة"),
        ("MAIN MENU", "القائمة الرئيسية"),
        ("enter  ·  R   restart        esc   main menu", "Enter  ·  R   من جديد        Esc   القائمة الرئيسية"),
        ("YOU SURVIVED", "نجوت!"),
        ("YOU DIED", "سقطت…"),
        ("seed {0}", "البذرة {0}"),
        ("Run won  ·  then held the Final Swarm for {0} ({1} kills, enemy hp +{2}%)",
         "فزت بالجولة  ·  ثم صمدت أمام السرب الأخير {0} ({1} إقصاء، صحة الأعداء +{2}%)"),
        ("Time\nLevel\nKills\nElites\nGold\nWeapons\nItems\nExperience",
         "الوقت\nالمستوى\nالإقصاءات\nالنخبة\nالذهب\nالأسلحة\nالأغراض\nالخبرة"),
        ("Balance  ·  {0}", "الرصيد  ·  {0}"),
        ("{0}  ·  MAX", "{0}  ·  أقصى"),
        ("{0}  ·  Lv {1}", "{0}  ·  مستوى {1}"),
        ("WEAPONS", "الأسلحة"),
        ("ITEMS", "الأغراض"),

        // ---- pearls payout (Meta/RunRewards)
        ("+{0} {1}  ({2} x{3:0.##})", "+{0} {1}  (مكافأة {2} ×{3:0.##})"),
        ("  (challenges x{0:0.##})", "  (التحديات ×{0:0.##})"),
        ("{0} UNLOCKED", "انفتح {0}!"),
        ("Kills ({0})", "الإقصاءات ({0})"),
        ("Elites ({0})", "النخبة ({0})"),
        ("Phases cleared ({0})", "المراحل المُنجزة ({0})"),
        ("Bosses ({0})", "الزعماء ({0})"),
        ("Victory", "الفوز"),
        ("Final Swarm ({0}, {1} kills)", "السرب الأخير ({0}، {1} إقصاء)"),

        // ---- effect lines (Data/Catalog.DescribeEffect)
        ("/min", "/دقيقة"),

        // ---- shrine blessing panel
        ("SHRINE CHARGED", "المزار امتلأ!"),
        ("Choose a blessing for this run", "اختر بركة لهذه الجولة"),
        ("press  1  ·  2  ·  3", "اضغط  1  ·  2  ·  3"),

        // ---- merchant
        ("SHADY MERCHANT", "التاجر المريب"),
        ("\"Everything must go. Mostly to you.\"", "«كل شيء للبيع… ومعظمه لك.»"),
        ("press  1 · 2 · 3  to buy      Esc / E to leave", "اضغط  1 · 2 · 3  للشراء      Esc / E للمغادرة"),
        ("Your gold: {0}", "ذهبك: {0}"),
        ("SOLD", "بِيع!"),
        ("   · held x{0}", "   · لديك ×{0}"),
        ("   · NEW", "   · جديد"),
        ("MAXED", "مكتمل"),
        ("Buy · {0} gold", "شراء · {0} ذهب"),
        ("{0} gold (need {1})", "{0} ذهب (ينقصك {1})"),
        ("{0} gold", "{0} ذهب"),

        // ---- microwave
        ("MICROWAVE", "الميكروويف"),
        ("Put one item in, get one more stack out. Works once.", "ضع غرضًا، واحصل على نسخة إضافية منه. يعمل مرة واحدة."),
        ("You have nothing to put in it.", "ليس لديك ما تضعه فيه."),
        ("click an item or press 1-9      Esc / E to leave", "انقر غرضًا أو اضغط 1-9      Esc / E للمغادرة"),

        // ---- prompts over chests, stations and shrines
        ("{0} — open free chest", "{0} — افتح الصندوق المجاني"),
        ("{0} — open chest · {1} gold", "{0} — افتح الصندوق · {1} ذهب"),
        ("{0} — open chest · {1} gold  (need {2} more)", "{0} — افتح الصندوق · {1} ذهب  (ينقصك {2})"),
        ("{0} — merchant (sold out)", "{0} — التاجر (نفدت البضاعة)"),
        ("{0} — trade with merchant", "{0} — بادِل التاجر"),
        ("microwave (used)", "الميكروويف (مستعمل)"),
        ("{0} — use microwave · from {1} gold", "{0} — استخدم الميكروويف · من {1} ذهب"),
        ("trade with merchant", "بادِل التاجر"),
        ("use microwave", "استخدم الميكروويف"),
        ("charging  {0:0}%", "يمتلئ  {0:0}%"),
        ("another challenge is running", "هناك تحدٍّ آخر قائم"),
        ("{0} — start challenge\nwin: gilded reward · lose: nothing", "{0} — ابدأ التحدي\nالفوز: جائزة مذهّبة · الخسارة: لا شيء"),
        ("{0} — offer {1} gold for a blessing\n{2} left", "{0} — قدّم {1} ذهب مقابل بركة\nالمتبقي: {2}"),
        ("{0} — offer {1} gold  (need {2} more)", "{0} — قدّم {1} ذهب  (ينقصك {2})"),
        ("{0} — accept a curse  ({1} left)", "{0} — اقبل لعنة  (المتبقي: {1})"),
        ("{0} — find your balance\nspread your {1} around", "{0} — جِد توازنك\nوزّع {1} على البقية"),
        ("come back with a stronger bonus", "ارجع ومعك مكافأة أقوى"),
        ("{0} — toss {1} gold and make a wish\n{2} wish left", "{0} — ارمِ {1} ذهب وتمنَّ أمنية\nالمتبقي: أمنية {2}"),
        ("{0} — toss {1} gold and make a wish\n{2} wishes left", "{0} — ارمِ {1} ذهب وتمنَّ أمنية\nالمتبقي: {2} أمنيات"),
        ("{0} — toss {1} gold  (need {2} more)", "{0} — ارمِ {1} ذهب  (ينقصك {2})"),

        // ---- shrine challenge banner and markers
        ("CHALLENGE  ·  KILL {0}/{1}  ·  {2}s", "تحدٍّ  ·  أقصِ {0}/{1}  ·  {2}ث"),
        ("CHALLENGE  ·  SLAY THE GUARDIANS {0}/{1}  ·  {2}s", "تحدٍّ  ·  اهزم الحرّاس {0}/{1}  ·  {2}ث"),
        ("GREED x{0}", "الطمع ×{0}"),
        ("{0:0}m", "{0:0}م"),
        ("PORTAL  {0:0}m", "البوابة  {0:0}م"),

        // ---- Shrine of Balance panel
        ("1  Balance me!", "1  وازنّي!"),
        ("2  Not now", "2  ليس الآن"),
        ("Takes half of your strongest bonus and shares it, plus {0:0}%, with the other {1}. Once per shrine.",
         "يأخذ نصف أقوى مكافأة لديك ويوزّعها، مع زيادة {0:0}%، على الـ{1} الباقية. مرة لكل مزار."),

        // ---- world popups (translated centrally in UI/DamagePopup)
        ("-{0} gold", "-{0} ذهب"),
        ("+{0} gold", "+{0} ذهب"),
        ("need {0} more gold", "ينقصك {0} ذهب"),
        ("{0} +1", "{0} +1"),
        ("-{0} gold  ·  splash!", "-{0} ذهب  ·  طرطشة!"),
        ("{0}, +{1:0.#} units to the rest", "{0}، و+{1:0.#} وحدة للبقية"),
        ("IN BALANCE!", "توازن تام!"),
        ("CHALLENGE!", "تحدٍّ!"),
        ("CHALLENGE FAILED", "فشل التحدي"),
        ("CHALLENGE COMPLETE", "اكتمل التحدي!"),
        ("KILL {0} IN {1}s", "أقصِ {0} خلال {1}ث"),
        ("SLAY {0} GUARDIANS", "اهزم {0} من الحرّاس"),
        ("CURSED  x{0}", "ملعون  ×{0}"),
        ("Gold cache", "كنز ذهب"),
        ("SUPPLY", "مؤن"),
        ("FREE CHEST", "صندوق مجاني!"),
        ("FINAL SWARM", "السرب الأخير"),
        ("SWARM SURGE {0}", "هيجان السرب {0}"),
        ("ELITE WAVE", "موجة النخبة"),
        ("FROZEN", "متجمّد"),
        ("ENRAGED", "هائج!"),
        ("SURPRISE!", "مفاجأة!"),
        ("PUFF", "بوف"),
        ("BLOCK", "صدّ"),
        ("MEND", "التئام"),
        ("dodge", "مراوغة"),

        // ---- run challenges (Meta/ChallengeRuntime, MapChallengeRuntime)
        ("EBB CLOCK {0}s", "ساعة الجَزْر {0}ث"),
        ("EBB CLOCK  ·  {0}s", "ساعة الجَزْر  ·  {0}ث"),
        ("BECALMED {0}s", "سكون الريح {0}ث"),
        ("SPRING TIDE", "المدّ العالي"),
        ("WEAPONS FREE", "الأسلحة حرّة!"),
        ("THE TIDE TOOK YOU", "أخذك المدّ…"),
        ("Final Swarm {0:0}/{1:0}s", "السرب الأخير {0:0}/{1:0}ث"),
        ("Complete!", "تمّ!"),
        ("Reward unlocked", "انفتحت الجائزة"),
        ("MAP CHALLENGE COMPLETE!", "اكتمل تحدي الخريطة!"),
    };
}
