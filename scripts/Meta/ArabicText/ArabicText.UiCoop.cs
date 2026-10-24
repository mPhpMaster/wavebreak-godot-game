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
        ("Play with your Steam friends: HOST (Steam) opens a friends-only lobby and INVITE FRIENDS opens the Steam overlay. " +
         "Friends join from the invite, with \"Join Game\" on your name in Steam, or with JOIN in their list. No addresses or ports. " +
         "Steam shows the game as \"Spacewar\" (Valve's test app) until Wavebreak has its own Steam page. " +
         "Each player keeps their own character, weapons, level-ups and items; experience is shared. " +
         "A downed player is revived by a teammate standing next to them.",
         "العب مع أصدقائك على Steam: زر «استضافة (Steam)» يفتح ردهة للأصدقاء فقط، وزر «دعوة الأصدقاء» يفتح واجهة Steam. " +
         "ينضم أصدقاؤك من الدعوة، أو بخيار «الانضمام إلى اللعبة» على اسمك في Steam، أو بزر «انضمام» في قائمتهم. بلا عناوين ولا منافذ. " +
         "يعرض Steam اللعبة باسم «Spacewar» (تطبيق Valve التجريبي) إلى أن تحصل Wavebreak على صفحتها في Steam. " +
         "لكل لاعب شخصيته وأسلحته وترقياته وأغراضه؛ والخبرة مشتركة. " +
         "اللاعب الساقط ينهضه زميل يقف بجانبه."),

        // ---- Steam (Net/Steam, UI/CoopPage)
        ("HOST (Steam)", "استضافة (Steam)"),
        ("INVITE FRIENDS", "دعوة الأصدقاء"),
        ("FRIENDS IN A GAME", "أصدقاء في لعبة"),
        ("RETRY", "إعادة المحاولة"),
        ("No friends are hosting right now.", "لا يستضيف أي صديق لعبة الآن."),
        ("Steam must be running to play co-op.", "يجب أن يكون Steam قيد التشغيل للعب الجماعي."),
        ("Start Steam and sign in, then press RETRY.", "شغّل Steam وسجّل الدخول، ثم اضغط «إعادة المحاولة»."),
        ("Host a game and invite your Steam friends, or join a friend's game.", "استضف لعبة وادعُ أصدقاءك على Steam، أو انضم إلى لعبة صديق."),
        ("No Steam window? Invite from your Steam friends list: right-click a friend, Invite to Lobby.",
         "لم تظهر نافذة Steam؟ ادعُ من قائمة أصدقائك في Steam: انقر بالزر الأيمن على صديق واختر الدعوة إلى الردهة (Invite to Lobby)."),
        ("Creating a Steam lobby...", "جارٍ إنشاء ردهة على Steam..."),
        ("Could not create a Steam lobby ({0}).", "تعذّر إنشاء ردهة على Steam ({0})."),
        ("A Steam invite is waiting. It opens here when the run ends.", "دعوة Steam بانتظارك. ستُفتح هنا عند انتهاء الجولة."),
        ("Joining {0}'s game...", "جارٍ الانضمام إلى لعبة {0}..."),
        ("Joining the lobby...", "جارٍ الانضمام إلى الردهة..."),
        ("That game no longer exists.", "تلك اللعبة لم تعد موجودة."),
        ("Could not join that lobby ({0}).", "تعذّر الانضمام إلى تلك الردهة ({0})."),
        ("That lobby is not a Wavebreak game.", "تلك الردهة ليست لعبة Wavebreak."),
        ("Could not reach the host through Steam.", "تعذّر الوصول إلى المضيف عبر Steam."),
        ("Lobby open. Invite friends with INVITE FRIENDS or from your Steam friends list.",
         "الردهة مفتوحة. ادعُ أصدقاءك بزر «دعوة الأصدقاء» أو من قائمة أصدقائك في Steam."),
        ("Connecting to {0} through Steam...", "جارٍ الاتصال بـ {0} عبر Steam..."),

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
