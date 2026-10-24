namespace Wavebreak.Meta;

/// <summary>Arabic for the fifteen perks (Data/Tomes.Perks.cs): names,
/// flavour, the rule sentence of each (a template whose holes are the level's
/// numbers), the short labels of the "what this level adds" line, and the few
/// UI words the perk code draws. "Shield" is الترس and "armour" الدرع, as in
/// the stat names.</summary>
public static partial class ArabicText
{
    internal static readonly (string En, string Ar)[] ContentPerks =
    {
        // ---- names, flavour and rules
        ("Stormcount", "عدّاد العاصفة"),
        ("The storm keeps a tally of your blows. When the count comes up, it pays out.", "العاصفة تعدّ ضرباتك، وحين يكتمل العدد تدفع الحساب."),
        ("Every {0:0} hits, lightning jumps to {1:0} more enemies for {2:0}%.", "كل {0:0} ضربة، يقفز برق إلى {1:0} أعداء آخرين بنسبة {2:0}%."),

        ("Bubble Net", "شبكة الفقاعات"),
        ("Big things burst into bubbles when they die. The bubbles are worth catching.", "الضخام ينفجرون فقاعات حين يموتون، والفقاعات تستحق الالتقاط."),
        ("Elites drop a bubble: {0:0} shield for {1:0}s. Other kills: {2:0.#}%.", "النخبة تُسقط فقاعة: ترس {0:0} لمدة {1:0} ث. بقية القتلى: {2:0.#}%."),

        ("Cold Wake", "الأثر البارد"),
        ("Every dash leaves cold, heavy water behind you.", "كل اندفاعة تترك خلفك ماءً باردًا ثقيلًا."),
        ("Dashes leave a {2:0.#} m wake for {0:0.#}s: it chills and deals {1:0} a second.", "الاندفاع يترك أثرًا بعرض {2:0.#} م لمدة {0:0.#} ث: يُبطئ ويضرب {1:0} في الثانية."),

        ("Crab Stomp", "دعسة السلطعون"),
        ("Learn from the crabs: the best way down is onto someone.", "تعلّم من السلطعون: أفضل هبوط هو فوق أحدهم."),
        ("Land on an enemy: {0:0} damage in {1:0.#} m, {2:0.#}s stun, and bounce.", "اهبط على عدو: ضرر {0:0} ضمن {1:0.#} م، صعق {2:0.#} ث، ثم ارتداد."),

        ("Crimson Tide", "المدّ القرمزي"),
        ("A clean cut closes your own wounds.", "الضربة النظيفة تُغلق جراحك أنت."),
        ("Crits heal {0:0.#} (up to {1:0} a second). Megacrits heal {2:0}% max health.", "الحرجة تشفي {0:0.#} (حتى {1:0} في الثانية). والحرجة الكبرى تشفي {2:0}% من أقصى الصحة."),

        ("Flotsam Orbit", "مدار الحطام"),
        ("What the tide brings in, it carries around you for a while.", "ما يجلبه المدّ يدور حولك قليلًا قبل أن يرحل."),
        ("Experience you collect orbits you for {2:0}s, hitting for {1:0}. Up to {0:0} motes.", "الخبرة التي تجمعها تدور حولك {2:0} ث وتضرب بـ {1:0}. حتى {0:0} ذرّات."),

        ("Golden Ballast", "الصابورة الذهبية"),
        ("Weight in the hold, weight in the swing.", "ثِقل في العنبر، وثِقل في الضربة."),
        ("Gold: +{0:0.#}% damage a stack, up to {1:0}, {2:0}s each. Full: a coin burst for {3:0}.", "الذهب: ضرر +{0:0.#}% لكل طبقة، حتى {1:0}، ولكل طبقة {2:0} ث. عند الامتلاء: انفجار عملات بضرر {3:0}."),

        ("Last Light", "الضوء الأخير"),
        ("One lamp kept lit for the worst moment.", "مصباح واحد يبقى مضاءً لأسوأ لحظة."),
        ("Refuse {0:0} lethal blows a map: back at {1:0}% health, 2s immune, a blast for {2:0}.", "ترفض {0:0} ضربات قاتلة في كل خريطة: تعود بـ {1:0}% صحة، حصانة 2 ث، وانفجار بضرر {2:0}."),

        ("Keeper's Beam", "شعاع الحارس"),
        ("A keeper always knows where the wrecks wash up.", "الحارس يعرف دائمًا أين يرسو الحطام."),
        ("Every {0:0}s a beam marks the nearest chest. Opening it: +{1:0} gold, {2:0}% heal.", "كل {0:0} ث يُعلّم شعاعٌ أقرب صندوق. فتحه: ذهب +{1:0}، وشفاء {2:0}%."),

        ("Tidal Pulse", "نبضة المدّ"),
        ("The sea breathes out, and everything near you is pushed away.", "يزفر البحر، فيُدفع كل ما حولك بعيدًا."),
        ("Every {0:0.#}s a pulse hits all within {2:0.#} m for {1:0} and pushes them back.", "كل {0:0.#} ث نبضة تضرب كل ما ضمن {2:0.#} م بضرر {1:0} وتدفعه للخلف."),

        ("Barnacle Hide", "جلد البرنقيل"),
        ("Stay put long enough and the sea starts building on you.", "اثبت في مكانك طويلًا فيبدأ البحر بالبناء عليك."),
        ("Standing still: +{0:0.#} armour a second, up to {1:0}. Each point: +{2:0.##} thorns.", "الثبات: درع +{0:0.#} في الثانية، حتى {1:0}. ولكل نقطة: أشواك +{2:0.##}."),

        ("Following Sea", "البحر المرافق"),
        ("Move with the wind and your hands move faster too.", "تحرّك مع الريح فتتحرك يداك أسرع أيضًا."),
        ("Per m/s you move: +{0:0.#}% attack speed (up to +{1:0}%), +{2:0.#}% shot speed.", "لكل م/ث تتحركها: سرعة هجوم +{0:0.#}% (حتى +{1:0}%)، وسرعة مقذوفات +{2:0.#}%."),

        ("Cornered Eel", "الأنقليس المحاصَر"),
        ("Nothing fights harder than something with nowhere left to go.", "لا شيء يقاتل أشدّ من محاصَر لم يبقَ له مهرب."),
        ("Below half health: up to +{0:0}% damage, {1:0}% lifesteal. Below a quarter: +{2:0} evasion.", "تحت نصف الصحة: ضرر حتى +{0:0}%، وسرقة حياة {1:0}%. تحت الربع: مراوغة +{2:0}."),

        ("Crew Beacon", "راية الطاقم"),
        ("Plant the crew's flag. Everyone near it fights better, you included.", "اغرز راية الطاقم، فيقاتل كل من حولها أفضل، وأنت معهم."),
        ("You and allies within {0:0} m: +{1:0}% attack speed, +{2:0} armour.", "أنت وحلفاؤك ضمن {0:0} م: سرعة هجوم +{1:0}%، ودرع +{2:0}."),

        ("Lifeline", "حبل النجاة"),
        ("A rope for whoever falls, and a last pull for yourself.", "حبل لمن يسقط، وجذبة أخيرة لنفسك."),
        ("Allies you revive get up {0:0}% faster, from {1:0.#} m further. Below 35% health: heal {2:0}% (every 40s).", "حلفاؤك الذين تُنعشهم ينهضون أسرع بـ {0:0}%، ومن أبعد بـ {1:0.#} م. تحت 35% صحة: تُشفى {2:0}% (كل 40 ث)."),

        // ---- labels of the "what this level adds" line
        ("hits", "الضربات"),
        ("jumps", "القفزات"),
        ("damage", "الضرر"),
        ("shield", "الترس"),
        ("time", "المدة"),
        ("chance", "الفرصة"),
        ("width", "العرض"),
        ("radius", "النطاق"),
        ("stun", "الصعق"),
        ("heal", "الشفاء"),
        ("per second", "في الثانية"),
        ("megacrit", "الحرجة الكبرى"),
        ("motes", "الذرّات"),
        ("per stack", "لكل طبقة"),
        ("stacks", "الطبقات"),
        ("burst", "الانفجار"),
        ("charges", "الشحنات"),
        ("health", "الصحة"),
        ("blast", "الانفجار"),
        ("every", "كل"),
        ("gold", "الذهب"),
        ("cap", "الحد"),
        ("thorns", "الأشواك"),
        ("per m/s", "لكل م/ث"),
        ("shot speed", "سرعة المقذوفات"),
        ("lifesteal", "سرقة الحياة"),
        ("evasion", "المراوغة"),
        ("attack speed", "سرعة الهجوم"),
        ("armour", "الدرع"),
        ("revive", "الإنعاش"),
        ("reach", "المدى"),
        ("surge", "الاندفاعة"),

        // ---- UI the perk code draws
        ("· PERK", "· ميزة"),
        ("New perk", "ميزة جديدة"),
        ("uses a tome slot", "تشغل خانة كتاب"),
        ("PERK  ·  {0}", "ميزة  ·  {0}"),
        ("Lv 1: {0}\nmax Lv {1}: {2}", "مستوى 1: {0}\nأعلى مستوى {1}: {2}"),
        ("LAST LIGHT", "الضوء الأخير!"),
        ("BUBBLE", "فقاعة"),
        ("Chest marked", "صندوق مُعلَّم"),
        ("SURGE", "اندفاعة"),
    };
}
