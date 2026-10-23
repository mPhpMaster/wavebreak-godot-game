using Godot;

namespace Wavebreak.Meta;

/// <summary>
/// What the command line asked for, as far as the meta layer cares.
///
/// The rule that matters is <see cref="SkipsMenu"/>: every automated run in the
/// repo (smoke test, input guards, content audit, captures) passes game args and
/// expects to land in a live run, not on a title screen waiting for a click that
/// never comes. Rather than list those flags here — and break the next time
/// someone adds one — anything that is not one of the few menu flags counts as
/// "this is a run". A bare launch, which is what a player does, gets the menu.
///
///     --profile=&lt;path&gt;      read/write this profile instead of user://profile.cfg
///     --character=&lt;id&gt;      start the run as this character (if unlocked)
///     --menu-shot=&lt;png&gt;     boot the title, capture, quit
///     --menu-page=&lt;page&gt;    main | play | codex | codex:secrets | settings | keys | graphics | boombox  (with --menu-shot)
///     --lang=en|ar          play in this language (menu flag; automated runs default to English)
///     --latin-report        with --lang=ar: list visible text still holding Latin words at each capture
///     --dump-strings=&lt;txt&gt; write every content string with no Arabic translation, then quit
///     --secret-test         walk to every hidden secret and press interact
///     --offer-check         prove earned items/weapons are in the level-up pool
/// </summary>
public static class Launch
{
    private static bool _parsed;

    public static string? ProfilePath { get; private set; }
    public static string? CharacterId { get; private set; }
    public static string? MenuShot { get; private set; }
    public static string MenuPage { get; private set; } = "main";
    public static bool SecretTest { get; private set; }
    public static bool OfferCheck { get; private set; }
    /// <summary>--lang=&lt;code&gt;: language override (does not skip the menu).</summary>
    public static string? Language { get; private set; }
    /// <summary>--dump-strings=&lt;path&gt;: list untranslated content strings, quit.</summary>
    public static string? DumpStrings { get; private set; }
    /// <summary>--devpanel-menu[=unlock]: open the developer panel on the title
    /// (with --menu-shot); "unlock" also presses Unlock everything.</summary>
    public static string? DevPanelMenu { get; private set; }

    /// <summary>True when any game argument other than the menu's own was given.</summary>
    public static bool SkipsMenu { get; private set; }

    /// <summary>True for any non-interactive launch. Such runs never write the
    /// player's real profile unless a path was given explicitly — a smoke test
    /// that quietly hands out unlocks on the developer's machine would make the
    /// menu lie about what the player has earned.</summary>
    public static bool Automated => SkipsMenu || MenuShot is not null;

    public static void Parse()
    {
        if (_parsed) return;
        _parsed = true;
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--profile=")) ProfilePath = arg["--profile=".Length..];
            else if (arg.StartsWith("--menu-shot=")) MenuShot = arg["--menu-shot=".Length..];
            else if (arg.StartsWith("--menu-page=")) MenuPage = arg["--menu-page=".Length..];
            else if (arg.StartsWith("--lang=")) Language = arg["--lang=".Length..];
            else if (arg.StartsWith("--dump-strings=")) DumpStrings = arg["--dump-strings=".Length..];
            else if (arg.StartsWith("--devpanel-menu")) DevPanelMenu = arg.Length > "--devpanel-menu=".Length ? arg["--devpanel-menu=".Length..] : "open";
            else if (arg.StartsWith("--character="))
            {
                CharacterId = arg["--character=".Length..];
                SkipsMenu = true;
            }
            else if (arg == "--offer-check")
            {
                OfferCheck = true;
                SkipsMenu = true;
            }
            else if (arg == "--secret-test")
            {
                SecretTest = true;
                SkipsMenu = true;
            }
            else if (arg == "--latin-report") { }   // Game/Screenshot: a capture diagnostic, menu or run
            else if (arg.StartsWith("--")) SkipsMenu = true;
        }
    }
}
