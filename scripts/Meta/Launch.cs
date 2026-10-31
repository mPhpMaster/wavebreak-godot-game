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
///     --menu-page=&lt;page&gt;    main | play | codex | codex:secrets | codex:evolutions | settings | keys | graphics | boombox  (with --menu-shot)
///     --lang=en|ar          play in this language (menu flag; automated runs default to English)
///     --latin-report        with --lang=ar: list visible text still holding Latin words at each capture
///     --dump-strings=&lt;txt&gt; write every content string with no Arabic translation, then quit
///     --secret-test         walk to every hidden secret and press interact
///     --offer-check         prove earned items/weapons are in the level-up pool
///     --steam-test          let an automated run (--menu-shot) start Steam: live co-op checks
///     --connect-lobby=&lt;id&gt;  join this Steam lobby at start (same as Steam's +connect_lobby)
///     --steam-selftest      check the Steam transport over a fake in-memory Steam, then quit
///     --dev                 in an exported release: allow the developer panel and test cheats
///     --check-pad-menu      walk every title page with an injected pad (menu flag)
///     --colorblind=&lt;id&gt;    deuteranopia | protanopia | tritanopia, in an automated run (menu flag)
///     --reduce-flashes      the same for reduced flashes (menu flag)
/// </summary>
public static class Launch
{
    private static bool _parsed;

    public static string? ProfilePath { get; private set; }
    public static string? CharacterId { get; private set; }
    public static string? MenuExpeditionCheck { get; private set; }
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

    /// <summary>True in the editor and debug builds, and in a release launched
    /// with --dev (given before or after the "--"). Everything that cheats is
    /// behind this: a player's copy has no developer panel, and its automated
    /// launches touch no profile (see <see cref="Profile.EnsureLoaded"/>).</summary>
    public static bool DevAllowed
        => OS.IsDebugBuild() || OS.HasFeature("editor")
           || System.Array.IndexOf(OS.GetCmdlineArgs(), "--dev") >= 0 || System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--dev") >= 0;

    /// <summary>--check-pad-menu: walk the title's pages with an injected pad.</summary>
    public static bool PadMenuCheck { get; private set; }

    /// <summary>True when any game argument other than the menu's own was given.</summary>
    public static bool SkipsMenu { get; private set; }

    /// <summary>True for any non-interactive launch. Such runs never write the
    /// player's real profile unless a path was given explicitly — a smoke test
    /// that quietly hands out unlocks on the developer's machine would make the
    /// menu lie about what the player has earned.</summary>
    public static bool Automated => SkipsMenu || MenuShot is not null || MenuExpeditionCheck is not null || PadMenuCheck;

    public static void Parse()
    {
        if (_parsed) return;
        _parsed = true;
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--menu-expedition-check=")) MenuExpeditionCheck = arg["--menu-expedition-check=".Length..];
            else if (arg.StartsWith("--profile=")) ProfilePath = arg["--profile=".Length..];
            else if (arg.StartsWith("--menu-shot=")) MenuShot = arg["--menu-shot=".Length..];
            else if (arg.StartsWith("--menu-page=")) MenuPage = arg["--menu-page=".Length..];
            else if (arg.StartsWith("--lang=")) Language = arg["--lang=".Length..];
            else if (arg.StartsWith("--hat=") && MenuShot is not null) { }   // title hero hat for captures (MainMenu.Hats)
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
            else if (arg == "--dev") { }   // a release with the developer panel, otherwise a normal launch
            else if (arg.StartsWith("--menu-review=")) { } // Menu test captures do not skip the title.
            else if (arg.StartsWith("--update-fixture=")) { }
            else if (arg == "--check-pad-menu") PadMenuCheck = true;
            else if (arg.StartsWith("--colorblind=") || arg == "--reduce-flashes") { }   // Meta/Accessibility: menu captures too
            else if (arg == "--latin-report") { }   // Game/Screenshot: a capture diagnostic, menu or run
            // Net/Steam: allow Steam in an automated run; join a lobby at start (menu flags).
            else if (arg == "--steam-test" || arg.StartsWith("--steam-test=") || arg.StartsWith("--connect-lobby")) { }
            // Net/PublicGames: a made-up public games list (captures); the lobby type.
            else if (arg == "--fake-browse" || arg.StartsWith("--lobby=")) { }
            else if (arg.StartsWith("--")) SkipsMenu = true;
        }
    }
}
