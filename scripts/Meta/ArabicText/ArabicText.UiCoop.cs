namespace Wavebreak.Meta;

/// <summary>Arabic for co-op: the CO-OP page and lobby, connection messages,
/// the in-run team panel, down/revive lines and the pause menu's leave button.</summary>
public static partial class ArabicText
{
    internal static readonly (string En, string Ar)[] UiCoop =
    {
        // ---- title: CO-OP page
        ("CO-OP", "لعب جماعي"),
        ("Your name", "اسمك"),
        ("Port", "المنفذ"),
        ("Address", "العنوان"),
        ("HOST", "استضافة"),
        ("JOIN", "انضمام"),
        ("Character", "الشخصية"),
        ("Skin", "المظهر"),
        ("PLAYERS (up to 4)", "اللاعبون (حتى 4)"),
        ("READY", "جاهز!"),
        ("NOT READY", "لست جاهزًا"),
        ("START", "ابدأ"),
        ("LEAVE", "مغادرة"),
        ("host", "المضيف"),
        ("not ready", "غير جاهز"),
        ("Host a game or join one.", "استضف لعبة أو انضم إلى واحدة."),
        ("The port must be a number from 1 to 65535.", "يجب أن يكون المنفذ رقمًا من 1 إلى 65535."),
        ("Direct connection. On the same network (LAN), join the host's local address, for example 192.168.1.20:7777. " +
         "Over the internet the host must forward the port (UDP) on their router, or everyone joins the same free virtual LAN " +
         "such as ZeroTier or Tailscale and uses that address. Each player keeps their own character, weapons, level-ups and items; " +
         "experience is shared. A downed player is revived by a teammate standing next to them.",
         "اتصال مباشر. على الشبكة نفسها (LAN) انضم إلى العنوان المحلي للمضيف، مثل 192.168.1.20:7777. " +
         "عبر الإنترنت يجب على المضيف فتح المنفذ (UDP) في الراوتر، أو ينضم الجميع إلى شبكة افتراضية مجانية واحدة " +
         "مثل ZeroTier أو Tailscale ويستخدمون عنوانها. لكل لاعب شخصيته وأسلحته وترقياته وأغراضه؛ " +
         "والخبرة مشتركة. اللاعب الساقط ينهضه زميل يقف بجانبه."),

        // ---- connection and lobby messages
        ("Could not host on port {0} ({1}).", "تعذّرت الاستضافة على المنفذ {0} ({1})."),
        ("Hosting on port {0}. Waiting for players...", "تستضيف على المنفذ {0}. بانتظار اللاعبين..."),
        ("Enter an address like 192.168.1.20:7777.", "اكتب عنوانًا مثل 192.168.1.20:7777."),
        ("Could not connect to {0} ({1}).", "تعذّر الاتصال بـ {0} ({1})."),
        ("Connecting to {0}...", "جارٍ الاتصال بـ {0}..."),
        ("No answer from the host. Check the address, the port and the firewall.", "لا رد من المضيف. تحقق من العنوان والمنفذ والجدار الناري."),
        ("Connected. Waiting for the host...", "تم الاتصال. بانتظار المضيف..."),
        ("Could not reach the host.", "تعذّر الوصول إلى المضيف."),
        ("The host left the game.", "غادر المضيف اللعبة."),
        ("Different game version.", "نسخة اللعبة مختلفة."),
        ("That game has already started.", "تلك اللعبة بدأت بالفعل."),
        ("That game is full.", "تلك اللعبة ممتلئة."),
        ("In the lobby. Press READY when you are set.", "أنت في الردهة. اضغط «جاهز!» عندما تستعد."),
        ("Everyone is ready. Press START.", "الجميع جاهزون. اضغط «ابدأ»."),
        ("Waiting for everyone to press READY...", "بانتظار أن يضغط الجميع «جاهز!»..."),

        // ---- in a run
        ("Waiting for everyone to load...", "بانتظار تحميل الجميع..."),
        ("You are down. A teammate standing next to you revives you ({0}%).", "سقطت. زميل يقف بجانبك ينهضك ({0}%)."),
        ("{0}  DOWN", "{0}  ساقط"),
        ("{0} (you)", "{0} (أنت)"),
        ("{0} (down)", "{0} (ساقط)"),
        ("{0} is down!", "سقط {0}!"),
        ("{0} is back up!", "نهض {0}!"),
        ("{0} left the game", "غادر {0} اللعبة"),
        ("DOWN! A teammate can revive you", "سقطت! يستطيع زميلك أن ينهضك"),
        ("REVIVED", "نهضت!"),
        ("LEAVE GAME", "مغادرة اللعبة"),
    };
}
